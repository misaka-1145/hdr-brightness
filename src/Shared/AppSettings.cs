using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;

namespace HdrBrightness
{
    /// <summary>分组联动模式。</summary>
    internal static class GroupMode
    {
        /// <summary>组内显示器设为同一个数值。</summary>
        public const string Sync = "sync";

        /// <summary>拖动时按增量同步，保持组内各显示器之间的差值。</summary>
        public const string Relative = "relative";
    }

    [DataContract]
    internal sealed class GroupConfig
    {
        [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Order = 2)] public string Name = "分组";
        [DataMember(Order = 3)] public string Mode = GroupMode.Sync;

        /// <summary>成员显示器的 DisplayTarget.Key 列表。</summary>
        [DataMember(Order = 4)] public List<string> Monitors = new List<string>();
    }

    /// <summary>定时规则的执行目标。</summary>
    internal static class ScheduleTarget
    {
        public const string All = "all";
        public const string Group = "group";
        public const string Monitor = "monitor";
    }

    [DataContract]
    internal sealed class ScheduleRule
    {
        [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");

        /// <summary>触发时间（24 小时制）。</summary>
        [DataMember(Order = 2)] public int Hour = 8;
        [DataMember(Order = 3)] public int Minute = 0;

        /// <summary>生效的星期，1=周一 … 7=周日，例如 "12345" 表示工作日。</summary>
        [DataMember(Order = 4)] public string Days = ScheduleDays.EveryDay;

        [DataMember(Order = 5)] public string TargetKind = ScheduleTarget.All;

        /// <summary>分组 Id 或显示器 Key，TargetKind = all 时为空。</summary>
        [DataMember(Order = 6)] public string TargetId = string.Empty;

        [DataMember(Order = 7)] public int Percent = 100;
        [DataMember(Order = 8)] public bool Enabled = true;

        public string TimeText { get { return Hour.ToString("00") + ":" + Minute.ToString("00"); } }
    }

    /// <summary>星期的表示与常用组合。</summary>
    internal static class ScheduleDays
    {
        public const string EveryDay = "1234567";

        public static bool AppliesOn(string days, DateTime date)
        {
            if (string.IsNullOrEmpty(days)) return false;
            int index = ((int)date.DayOfWeek + 6) % 7 + 1;   // 周一 = 1 … 周日 = 7
            return days.IndexOf((char)('0' + index)) >= 0;
        }

        public static string Describe(string days)
        {
            if (days == EveryDay) return "每天";
            if (days == "12345") return "工作日";
            if (days == "67") return "周末";
            if (string.IsNullOrEmpty(days)) return "从不";

            var names = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            var parts = new List<string>();
            for (int i = 1; i <= 7; i++)
            {
                if (days.IndexOf((char)('0' + i)) >= 0) parts.Add(names[i - 1]);
            }
            return string.Join("、", parts.ToArray());
        }

        public static readonly string[] OptionLabels = new[]
        {
            "每天", "工作日", "周末", "周一", "周二", "周三", "周四", "周五", "周六", "周日",
        };

        public static readonly string[] OptionValues = new[]
        {
            "1234567", "12345", "67", "1", "2", "3", "4", "5", "6", "7",
        };
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Order = 1)] public bool AutoStart = true;
        [DataMember(Order = 2)] public bool StartMinimizedToTray = false;
        [DataMember(Order = 3)] public bool RestoreOnStartup = true;
        [DataMember(Order = 4)] public bool LockBrightness = true;
        [DataMember(Order = 5)] public int CheckIntervalSeconds = 20;
        /// <summary>显示器 Key → 目标白点值。</summary>
        [DataMember(Order = 8)] public Dictionary<string, uint> Desired = new Dictionary<string, uint>();

        [DataMember(Order = 9)] public List<GroupConfig> Groups = new List<GroupConfig>();

        [DataMember(Order = 10)] public Dictionary<string, string> Nicknames = new Dictionary<string, string>();

        [DataMember(Order = 11)] public bool FirstRunDone;

        [DataMember(Order = 12)] public List<ScheduleRule> Schedules = new List<ScheduleRule>();

        // ---------- 持久化 ----------

        public static string ConfigDirectory
        {
            get
            {
                // 便于测试/便携使用：设置了这个环境变量就把它当配置目录
                string custom = Environment.GetEnvironmentVariable("HDRBRIGHTNESS_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(custom)) return custom;

                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HdrBrightness");
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "settings.json"); }
        }

        public static AppSettings Load()
        {
            LastLoadError = null;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                // 配置损坏时用默认值，不要让程序起不来
                LastLoadError = ex.GetType().Name + ": " + ex.Message;
            }

            var settings = new AppSettings();
            settings.Normalize();
            return settings;
        }

        /// <summary>最近一次读取配置失败的原因（成功时为 null），仅用于排查问题。</summary>
        public static string LastLoadError { get; private set; }

        private void Normalize()
        {
            if (Desired == null) Desired = new Dictionary<string, uint>();
            if (Groups == null) Groups = new List<GroupConfig>();
            if (Nicknames == null) Nicknames = new Dictionary<string, string>();
            if (Schedules == null) Schedules = new List<ScheduleRule>();
            foreach (var group in Groups)
            {
                if (group.Monitors == null) group.Monitors = new List<string>();
                if (string.IsNullOrEmpty(group.Id)) group.Id = Guid.NewGuid().ToString("N");
                if (group.Mode != GroupMode.Relative) group.Mode = GroupMode.Sync;
            }
            if (CheckIntervalSeconds < 5) CheckIntervalSeconds = 5;

            foreach (var rule in Schedules)
            {
                if (string.IsNullOrEmpty(rule.Id)) rule.Id = Guid.NewGuid().ToString("N");
                if (rule.Hour < 0 || rule.Hour > 23) rule.Hour = 8;
                if (rule.Minute < 0 || rule.Minute > 59) rule.Minute = 0;
                if (rule.Percent < 0) rule.Percent = 0;
                if (rule.Percent > 100) rule.Percent = 100;
                if (rule.TargetKind != ScheduleTarget.Group && rule.TargetKind != ScheduleTarget.Monitor)
                {
                    rule.TargetKind = ScheduleTarget.All;
                }
                if (rule.Days == null) rule.Days = ScheduleDays.EveryDay;

                var keep = new StringBuilder();
                for (int i = 1; i <= 7; i++)
                {
                    if (rule.Days.IndexOf((char)('0' + i)) >= 0) keep.Append((char)('0' + i));
                }
                rule.Days = keep.Length > 0 ? keep.ToString() : ScheduleDays.EveryDay;
            }
        }

        public ScheduleRule FindSchedule(string id)
        {
            foreach (var rule in Schedules)
            {
                if (rule.Id == id) return rule;
            }
            return null;
        }

        public GroupConfig FindGroupById(string id)
        {
            foreach (var group in Groups)
            {
                if (group.Id == id) return group;
            }
            return null;
        }

        public GroupConfig FindGroupOfMonitor(string monitorKey)
        {
            foreach (var group in Groups)
            {
                if (group.Monitors.Contains(monitorKey)) return group;
            }
            return null;
        }

        public void AssignMonitorToGroup(string monitorKey, string groupId)
        {
            foreach (var group in Groups)
            {
                group.Monitors.Remove(monitorKey);
            }

            if (!string.IsNullOrEmpty(groupId))
            {
                var group = FindGroupById(groupId);
                if (group != null && !group.Monitors.Contains(monitorKey))
                {
                    group.Monitors.Add(monitorKey);
                }
            }
        }

        public string GetDisplayName(DisplayTarget target)
        {
            string nickname;
            if (Nicknames.TryGetValue(target.Key, out nickname) && !string.IsNullOrWhiteSpace(nickname))
            {
                return nickname;
            }
            return target.DisplayName;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);

                string temp = ConfigPath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                File.Move(temp, ConfigPath);
            }
            catch (Exception)
            {
                // 保存失败不影响本次运行
            }
        }
    }
}
