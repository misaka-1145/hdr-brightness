using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HdrBrightness
{
    /// <summary>
    /// 业务核心：枚举显示器、读写 SDR 白点值、处理分组联动、锁定回写与配置保存。
    /// 全部在 UI 线程上跑，DisplayConfig 调用都是微秒级，不需要额外线程。
    /// </summary>
    internal sealed class BrightnessService : IDisposable
    {
        private readonly List<DisplayTarget> _targets = new List<DisplayTarget>();
        private readonly Dictionary<string, uint> _groupStartValues = new Dictionary<string, uint>();

        private Timer _watchdogTimer;
        private Timer _saveTimer;
        private Timer _refreshTimer;
        private Timer _startupTimer;
        private Timer _scheduleTimer;
        private DateTime _scheduleLastCheck;
        private const int StartupCatchUpHours = 12;
        private bool _adjustingGroup;
        private bool _saving;

        public BrightnessService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public IList<DisplayTarget> Targets { get { return _targets; } }

        /// <summary>最近一次枚举显示器的错误信息（没有错误时为 null）。</summary>
        public string EnumerationError { get; private set; }

        /// <summary>是否存在读取不到显示器信息的情况。</summary>
        public bool HasProblems
        {
            get
            {
                if (!string.IsNullOrEmpty(EnumerationError)) return true;
                foreach (var target in _targets)
                {
                    if (!target.WhiteLevelKnown) return true;
                }
                return false;
            }
        }

        public event EventHandler TargetsChanged;
        public event EventHandler ValuesChanged;
        public event EventHandler<string> StatusChanged;

        public void Start()
        {
            Refresh();

            _saveTimer = new Timer { Interval = 800 };
            _saveTimer.Tick += delegate { _saveTimer.Stop(); SaveNow(); };

            _watchdogTimer = new Timer { Interval = Math.Max(5, Settings.CheckIntervalSeconds) * 1000 };
            _watchdogTimer.Tick += delegate { EnforceDesiredValues(false); };

            _refreshTimer = new Timer { Interval = 2000 };
            _refreshTimer.Tick += delegate
            {
                _refreshTimer.Stop();
                Refresh();
            };

            if (Settings.LockBrightness) _watchdogTimer.Start();

            StartSchedules();

            if (Settings.RestoreOnStartup)
            {
                // 开机/启动时显示器还没枚举完，等几秒再把上次的数值写回去
                _startupTimer = new Timer { Interval = 3000 };
                _startupTimer.Tick += delegate
                {
                    _startupTimer.Stop();
                    _startupTimer.Dispose();
                    _startupTimer = null;
                    EnforceDesiredValues(true, true);
                };
                _startupTimer.Start();
            }

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        // ---------- 枚举 ----------

        public void Refresh()
        {
            string error;
            var latest = DisplayService.Enumerate(out error);
            EnumerationError = error;

            // 保留已有对象的引用关系没意义，但要让已知的“目标值”对新出现的显示器生效
            _targets.Clear();
            _targets.AddRange(latest);

            foreach (var target in _targets)
            {
                if (!Settings.Desired.ContainsKey(target.Key) && target.WhiteLevelKnown)
                {
                    Settings.Desired[target.Key] = target.WhiteLevel;
                }
            }

            RaiseTargetsChanged();

            if (!string.IsNullOrEmpty(error))
            {
                RaiseStatus(error);
            }
            else
            {
                RaiseStatus("已识别 " + _targets.Count + " 个显示输出。");
            }
        }

        private void RaiseTargetsChanged()
        {
            var handler = TargetsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseValuesChanged()
        {
            var handler = ValuesChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseStatus(string message)
        {
            var handler = StatusChanged;
            if (handler != null) handler(this, message);
        }

        // ---------- 读写 ----------

        public uint Clamp(uint whiteLevel)
        {
            if (whiteLevel < DisplayService.MinWhiteLevel) return DisplayService.MinWhiteLevel;
            if (whiteLevel > DisplayService.MaxWhiteLevel) return DisplayService.MaxWhiteLevel;
            return whiteLevel;
        }

        public bool TryApply(DisplayTarget target, uint whiteLevel, out string error)
        {
            error = null;
            uint value = Clamp(whiteLevel);
            int code = DisplayService.SetWhiteLevel(target, value);
            if (code != DisplayConfigNative.ErrorSuccess)
            {
                error = DisplayService.DescribeError(code);
                return false;
            }

            // 写成功后读回一次：驱动可能会取整，读回的值才是真实生效的
            uint actual;
            int readError;
            if (!DisplayService.TryGetWhiteLevel(target, out actual, out readError))
            {
                actual = value;
            }

            target.WhiteLevel = actual;
            target.WhiteLevelKnown = true;
            Settings.Desired[target.Key] = actual;
            return true;
        }

        public void ApplyGlobalValue(uint whiteLevel)
        {
            string firstError = null;
            foreach (var target in _targets)
            {
                string error;
                if (!TryApply(target, whiteLevel, out error) && firstError == null)
                {
                    firstError = target.DisplayName + "：" + error;
                }
            }
            FinishChange(firstError);
        }

        public void ApplyGroupValue(GroupConfig group, uint whiteLevel)
        {
            string firstError = null;
            foreach (var target in _targets)
            {
                if (!group.Monitors.Contains(target.Key)) continue;
                string error;
                if (!TryApply(target, whiteLevel, out error) && firstError == null)
                {
                    firstError = target.DisplayName + "：" + error;
                }
            }
            FinishChange(firstError);
        }

        /// <summary>相对模式：记下拖动开始时的各显示器数值。</summary>
        public void BeginGroupAdjustment(GroupConfig group)
        {
            _groupStartValues.Clear();
            foreach (var target in _targets)
            {
                if (group.Monitors.Contains(target.Key))
                {
                    _groupStartValues[target.Key] = target.WhiteLevelKnown
                        ? target.WhiteLevel
                        : DisplayService.DefaultWhiteLevel;
                }
            }
            _adjustingGroup = true;
        }

        /// <summary>相对模式：对组内每个显示器按同样的增量调整，差值保持不变。</summary>
        public void ApplyGroupDelta(GroupConfig group, int delta)
        {
            string firstError = null;
            foreach (var target in _targets)
            {
                uint start;
                if (!group.Monitors.Contains(target.Key)) continue;

                if (!_groupStartValues.TryGetValue(target.Key, out start))
                {
                    start = target.WhiteLevelKnown ? target.WhiteLevel : DisplayService.DefaultWhiteLevel;
                    _groupStartValues[target.Key] = start;
                }

                long value = (long)start + delta;
                string error;
                if (!TryApply(target, (uint)Math.Max(0, value), out error) && firstError == null)
                {
                    firstError = target.DisplayName + "：" + error;
                }
            }
            FinishChange(firstError);
        }

        public void EndGroupAdjustment()
        {
            _adjustingGroup = false;
            _groupStartValues.Clear();
        }

        private void FinishChange(string error)
        {
            RaiseValuesChanged();

            if (!string.IsNullOrEmpty(error)) RaiseStatus("部分显示器设置失败：" + error);

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void UpdateWatchdog()
        {
            if (_watchdogTimer == null) return;

            _watchdogTimer.Interval = Math.Max(5, Settings.CheckIntervalSeconds) * 1000;
            if (Settings.LockBrightness)
            {
                if (!_watchdogTimer.Enabled) _watchdogTimer.Start();
            }
            else
            {
                _watchdogTimer.Stop();
            }
        }

        // ---------- 定时规则 ----------

        private void StartSchedules()
        {
            // 启动时往前看一段时间，把“本来该生效”的那条补上（关机/重启期间错过的也算）
            _scheduleLastCheck = DateTime.Now.AddHours(-StartupCatchUpHours);

            _scheduleTimer = new Timer { Interval = 20000 };
            _scheduleTimer.Tick += delegate { ApplyDueSchedules(false); };
            _scheduleTimer.Start();
        }

        /// <summary>检查并执行到点的定时规则。</summary>
        public void ApplyDueSchedules(bool quiet)
        {
            DateTime now = DateTime.Now;
            var due = new List<KeyValuePair<DateTime, ScheduleRule>>();

            foreach (var rule in Settings.Schedules)
            {
                if (!rule.Enabled) continue;

                for (DateTime day = _scheduleLastCheck.Date; day <= now.Date; day = day.AddDays(1))
                {
                    if (!ScheduleDays.AppliesOn(rule.Days, day)) continue;

                    DateTime when = day.AddHours(rule.Hour).AddMinutes(rule.Minute);
                    if (when > _scheduleLastCheck && when <= now)
                    {
                        due.Add(new KeyValuePair<DateTime, ScheduleRule>(when, rule));
                    }
                }
            }

            _scheduleLastCheck = now;
            if (due.Count == 0) return;

            due.Sort(delegate (KeyValuePair<DateTime, ScheduleRule> a, KeyValuePair<DateTime, ScheduleRule> b)
            {
                return a.Key.CompareTo(b.Key);
            });

            string summary = null;
            foreach (var item in due)
            {
                summary = ApplySchedule(item.Value);
            }

            if (!quiet && summary != null) RaiseStatus(summary);
        }

        /// <summary>立即执行一条规则，返回给界面显示的结果描述。</summary>
        public string ApplySchedule(ScheduleRule rule)
        {
            uint level = DisplayService.PercentToWhiteLevel(rule.Percent);
            int applied = 0;

            if (rule.TargetKind == ScheduleTarget.Group)
            {
                var group = Settings.FindGroupById(rule.TargetId);
                if (group != null)
                {
                    ApplyGroupValue(group, level);
                    foreach (var target in _targets)
                    {
                        if (group.Monitors.Contains(target.Key)) applied++;
                    }
                }
            }
            else if (rule.TargetKind == ScheduleTarget.Monitor)
            {
                var target = FindTarget(rule.TargetId);
                if (target != null)
                {
                    string error;
                    if (TryApply(target, level, out error)) applied = 1;
                }
            }
            else
            {
                ApplyGlobalValue(level);
                applied = _targets.Count;
            }

            RaiseValuesChanged();
            string text = "定时 " + rule.TimeText + " → " + rule.Percent + "%（" + DescribeScheduleTarget(rule) + "）已执行";
            if (applied == 0) text += "，但没有匹配到显示器";
            return text;
        }

        public DisplayTarget FindTarget(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var target in _targets)
            {
                if (target.Key == key) return target;
            }
            return null;
        }

        public string DescribeScheduleTarget(ScheduleRule rule)
        {
            if (rule.TargetKind == ScheduleTarget.Group)
            {
                var group = Settings.FindGroupById(rule.TargetId);
                return group == null ? "分组（已删除）" : "分组 " + group.Name;
            }
            if (rule.TargetKind == ScheduleTarget.Monitor)
            {
                var target = FindTarget(rule.TargetId);
                if (target != null) return Settings.GetDisplayName(target);

                // 显示器可能没插，用保存过的名字或 Key 兜底
                string nickname;
                if (Settings.Nicknames.TryGetValue(rule.TargetId, out nickname)) return nickname + "（未连接）";
                return "某台显示器（未连接）";
            }
            return "全部显示器";
        }

        /// <summary>把与目标值不一致的显示器改回来（HDR 切换、唤醒、重新插拔后）。</summary>
        public void EnforceDesiredValues(bool quiet)
        {
            EnforceDesiredValues(quiet, false);
        }

        public void EnforceDesiredValues(bool quiet, bool force)
        {
            if (!Settings.LockBrightness && !force) return;
            if (_adjustingGroup) return;

            bool changed = false;
            foreach (var target in _targets)
            {
                uint desired;
                if (!Settings.Desired.TryGetValue(target.Key, out desired)) continue;

                uint current;
                int error;
                if (!DisplayService.TryGetWhiteLevel(target, out current, out error))
                {
                    if (!quiet) RaiseStatus(target.DisplayName + "：读取当前数值失败，" + DisplayService.DescribeError(error));
                    continue;
                }

                target.WhiteLevel = current;
                target.WhiteLevelKnown = true;
                if (current == desired) continue;

                string setError;
                if (TryApply(target, desired, out setError))
                {
                    changed = true;
                }
                else if (!quiet)
                {
                    RaiseStatus(target.DisplayName + "：恢复失败，" + setError);
                }
            }

            if (changed)
            {
                RaiseValuesChanged();
                RaiseStatus("已按上次的数值恢复显示器亮度。");
            }
        }

        /// <summary>启动或显示器变化后重新读一次当前值（不写回）。</summary>
        public void ReloadCurrentValues()
        {
            foreach (var target in _targets)
            {
                uint current;
                int error;
                if (DisplayService.TryGetWhiteLevel(target, out current, out error))
                {
                    target.WhiteLevel = current;
                    target.WhiteLevelKnown = true;
                }
            }
            RaiseValuesChanged();
        }

        public void SaveNow()
        {
            if (_saving) return;
            _saving = true;
            try
            {
                Settings.Save();
            }
            finally
            {
                _saving = false;
            }
        }

        public void SaveSoon()
        {
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // ---------- 系统事件 ----------

        /// <summary>
        /// 调试用：用假数据填充显示器列表，用来检查界面排版（只在 --demo 时调用）。
        /// </summary>
        public void LoadDemoTargets()
        {
            _targets.Clear();
            _targets.Add(MakeDemoTarget("\\\\.\\DISPLAY3", "外接显示器 HKC G271Q", true, true, 1600, 10));
            _targets.Add(MakeDemoTarget("\\\\.\\DISPLAY1", "内置屏 NE160QDM", true, true, 1000, 11));
            _targets.Add(MakeDemoTarget("\\\\.\\DISPLAY2", "投影仪", false, false, 1200, 6));

            if (Settings.Groups.Count == 0)
            {
                var group = new GroupConfig();
                group.Name = "笔记本 + 外接屏";
                group.Mode = GroupMode.Sync;
                group.Monitors.Add("demo|\\\\.\\DISPLAY1");
                group.Monitors.Add("demo|\\\\.\\DISPLAY3");
                Settings.Groups.Add(group);
            }

            RaiseTargetsChanged();
            RaiseValuesChanged();
        }

        private static DisplayTarget MakeDemoTarget(
            string gdiName, string friendlyName, bool hdrSupported, bool hdrEnabled, uint whiteLevel, int technology)
        {
            var target = new DisplayTarget();
            target.FriendlyName = friendlyName;
            target.DevicePath = "demo|" + gdiName;
            target.GdiName = gdiName;
            target.HdrSupported = hdrSupported;
            target.HdrEnabled = hdrEnabled;
            target.AdvancedColorKnown = true;
            target.WhiteLevel = whiteLevel;
            target.WhiteLevelKnown = whiteLevel > 0;
            target.OutputTechnology = technology;
            return target;
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // 分辨率/HDR 切换后显示器句柄可能变了，稍等再重新枚举并回写
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                EnforceDesiredValues(true);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                EnforceDesiredValues(true);
            }
        }

        public void Dispose()
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (Exception)
            {
            }

            if (_watchdogTimer != null) _watchdogTimer.Dispose();
            if (_saveTimer != null) _saveTimer.Dispose();
            if (_refreshTimer != null) _refreshTimer.Dispose();
            if (_startupTimer != null) _startupTimer.Dispose();
            if (_scheduleTimer != null) _scheduleTimer.Dispose();
        }
    }
}
