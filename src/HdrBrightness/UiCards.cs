using System;
using System.Drawing;
using System.Windows.Forms;

namespace HdrBrightness
{
    /// <summary>卡片共用的排版参数与辅助方法。</summary>
    internal static class CardLayout
    {
        public const int Padding = 20;
        public const int Row1 = 16;
        public const int Row2 = 56;
        public const int Row3 = 96;
        public const int RowHeight = 30;
        public const int Height = 142;

        public static Label MakeLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = FlatTheme.Surface,
                AutoSize = true,
            };
        }

        public static void PlaceValueLabel(Label label, FlatCard card, int rightPadding)
        {
            label.Location = new Point(
                Math.Max(FlatTheme.Scale(card, 8), card.Width - FlatTheme.Scale(card, rightPadding) - label.Width),
                FlatTheme.Scale(card, Row2 + 2));
        }

        public static string Ellipsize(string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (TextRenderer.MeasureText(text, font).Width <= maxWidth) return text;
            for (int length = text.Length - 1; length > 1; length--)
            {
                string candidate = text.Substring(0, length) + "…";
                if (TextRenderer.MeasureText(candidate, font).Width <= maxWidth) return candidate;
            }
            return text;
        }
    }

    /// <summary>单台显示器卡片：百分比滑块 + 常用档位 + 分组归属。</summary>
    internal sealed class MonitorCard : FlatCard
    {
        private readonly BrightnessService _service;
        private readonly DisplayTarget _target;
        private readonly Label _nameLabel;
        private readonly FlatBadge _statusBadge;
        private readonly FlatDropdown _groupDropdown;
        private readonly FlatSlider _slider;
        private readonly Label _valueLabel;
        private readonly Label _metaLabel;
        private readonly FlatSegmented _segmented;
        private readonly ToolTip _toolTip;
        private readonly Action _groupChanged;
        private string _metaFullText = string.Empty;
        private bool _updating;

        public MonitorCard(BrightnessService service, DisplayTarget target, Action groupChanged)
        {
            _service = service;
            _target = target;
            _groupChanged = groupChanged;

            Height = UiScale(CardLayout.Height);
            BorderColor = FlatTheme.Border;

            _nameLabel = CardLayout.MakeLabel(service.Settings.GetDisplayName(target), FlatTheme.CardTitle, FlatTheme.TextPrimary);
            _nameLabel.Location = new Point(UiScale(CardLayout.Padding), UiScale(CardLayout.Row1 + 2));
            _nameLabel.Cursor = Cursors.Hand;
            _nameLabel.DoubleClick += delegate { RenameMonitor(); };

            _statusBadge = new FlatBadge { Text = "", Font = FlatTheme.Small };

            _groupDropdown = new FlatDropdown { Font = FlatTheme.Small };
            _groupDropdown.SelectionChanged = delegate (GroupChoice choice)
            {
                if (_updating || choice == null) return;
                _service.Settings.AssignMonitorToGroup(_target.Key, choice.Id);
                _service.SaveSoon();
                if (_groupChanged != null) _groupChanged();
            };
            FillGroupDropdown();

            _slider = new FlatSlider
            {
                ValueChanged = delegate (int value)
                {
                    if (_updating) return;
                    ApplyPercent(value);
                },
                DragCompleted = delegate { _service.SaveSoon(); },
            };
            _updating = true;
            _slider.Value = target.WhiteLevelKnown ? DisplayService.WhiteLevelToPercent(target.WhiteLevel) : 0;
            _updating = false;

            _valueLabel = CardLayout.MakeLabel("", FlatTheme.Value, FlatTheme.TextPrimary);

            _metaLabel = CardLayout.MakeLabel("", FlatTheme.Small, FlatTheme.TextMuted);
            _metaLabel.Location = new Point(UiScale(CardLayout.Padding), UiScale(CardLayout.Row3 + 8));
            _toolTip = new ToolTip();

            _segmented = new FlatSegmented(SetPercent) { Font = FlatTheme.Small };

            Controls.AddRange(new Control[]
            {
                _nameLabel, _statusBadge, _groupDropdown, _slider, _valueLabel, _metaLabel, _segmented
            });

            UpdateTexts();
            _service.ValuesChanged += OnServiceValuesChanged;
        }

        public DisplayTarget Target { get { return _target; } }

        private void FillGroupDropdown()
        {
            var items = new System.Collections.Generic.List<GroupChoice>();
            items.Add(new GroupChoice(null, "（不分组）"));
            foreach (var group in _service.Settings.Groups)
            {
                items.Add(new GroupChoice(group.Id, group.Name));
            }

            var current = _service.Settings.FindGroupOfMonitor(_target.Key);
            _updating = true;
            _groupDropdown.SetItems(items, current == null ? null : current.Id);
            _updating = false;
        }

        private void RenameMonitor()
        {
            string current = _service.Settings.GetDisplayName(_target);
            string result = PromptForm.Ask(FindForm(), "给这台显示器起个名字", "例如：主屏 / 副屏", current);
            if (result == null) return;

            if (string.IsNullOrWhiteSpace(result) || result == _target.DisplayName)
            {
                _service.Settings.Nicknames.Remove(_target.Key);
            }
            else
            {
                _service.Settings.Nicknames[_target.Key] = result.Trim();
            }

            _nameLabel.Text = _service.Settings.GetDisplayName(_target);
            _service.SaveSoon();
            if (_groupChanged != null) _groupChanged();
        }

        private void SetPercent(int percent)
        {
            if (_updating) return;
            _updating = true;
            _slider.Value = Math.Max(0, Math.Min(100, percent));
            _updating = false;
            ApplyPercent(percent);
        }

        private void ApplyPercent(int percent)
        {
            uint whiteLevel = DisplayService.PercentToWhiteLevel(percent);
            string error;
            _service.TryApply(_target, whiteLevel, out error);
            _service.SaveSoon();
            UpdateTexts();

            if (error != null)
            {
                _metaLabel.ForeColor = FlatTheme.Amber;
                _metaFullText = "设置失败：" + error;
                _toolTip.SetToolTip(_metaLabel, _metaFullText);
                PerformLayout();
            }
        }

        private void OnServiceValuesChanged(object sender, EventArgs e)
        {
            _updating = true;
            _slider.Value = _target.WhiteLevelKnown ? DisplayService.WhiteLevelToPercent(_target.WhiteLevel) : 0;
            _updating = false;
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            bool known = _target.WhiteLevelKnown;
            int percent = known ? DisplayService.WhiteLevelToPercent(_target.WhiteLevel) : 0;

            _valueLabel.Text = known ? percent + "%" : "--";
            _segmented.SetSelected(known ? (int?)percent : null);

            if (known)
            {
                _metaLabel.ForeColor = FlatTheme.TextMuted;
                _metaFullText = (string.IsNullOrEmpty(_target.GdiName) ? "" : _target.GdiName + " · ")
                    + "原始值 " + _target.WhiteLevel;
            }
            else
            {
                _metaLabel.ForeColor = FlatTheme.Amber;
                _metaFullText = "读取当前值失败（" + DisplayService.DescribeError(_target.WhiteLevelError) + "）";
            }
            _metaLabel.Text = _metaFullText;
            _toolTip.SetToolTip(_metaLabel, _metaFullText);

            if (!_target.AdvancedColorKnown)
            {
                SetBadge("HDR 状态未知", FlatTheme.AmberSoft, FlatTheme.Amber);
            }
            else if (!_target.HdrSupported)
            {
                SetBadge("HDR 不支持", FlatTheme.SurfaceSubtle, FlatTheme.TextMuted);
            }
            else if (_target.HdrEnabled)
            {
                SetBadge("HDR 已开启", FlatTheme.GreenSoft, FlatTheme.Green);
            }
            else
            {
                SetBadge("HDR 未开启", FlatTheme.AmberSoft, FlatTheme.Amber);
            }

            PerformLayout();
        }

        private void SetBadge(string text, Color background, Color foreground)
        {
            _statusBadge.Text = text;
            _statusBadge.BadgeColor = background;
            _statusBadge.BadgeTextColor = foreground;
            _statusBadge.BackColor = SurfaceColor;
            _statusBadge.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_slider == null || _nameLabel == null) return;

            int pad = UiScale(CardLayout.Padding);
            int row1 = UiScale(CardLayout.Row1);
            int row2 = UiScale(CardLayout.Row2);
            int row3 = UiScale(CardLayout.Row3);

            int badgeWidth = TextRenderer.MeasureText(_statusBadge.Text, FlatTheme.Small).Width + UiScale(20);
            _statusBadge.SetBounds(_nameLabel.Right + UiScale(10), row1 + 2, badgeWidth, UiScale(22));

            int dropdownWidth = UiScale(162);
            _groupDropdown.SetBounds(Width - pad - dropdownWidth, row1 - 2, dropdownWidth, UiScale(30));

            int valueWidth = UiScale(84);
            _slider.SetBounds(pad, row2, Math.Max(UiScale(120), Width - pad * 2 - valueWidth), UiScale(28));
            _valueLabel.Location = new Point(
                Math.Max(pad, Width - pad - _valueLabel.Width), row2 + UiScale(2));

            int segmentedWidth = Math.Min(UiScale(230), Math.Max(UiScale(160), (Width - pad * 2) * 2 / 5));
            _segmented.SetBounds(Width - pad - segmentedWidth, row3, segmentedWidth, UiScale(30));

            _metaLabel.Location = new Point(pad, row3 + UiScale(8));
            _metaLabel.Text = CardLayout.Ellipsize(_metaFullText, FlatTheme.Small,
                Math.Max(UiScale(60), _segmented.Left - pad - UiScale(10)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _service.ValuesChanged -= OnServiceValuesChanged;
                if (_toolTip != null) _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>分组卡片：一个滑块带动组内所有显示器。</summary>
    internal sealed class GroupCard : FlatCard
    {
        private readonly BrightnessService _service;
        private readonly GroupConfig _group;
        private readonly Label _nameLabel;
        private readonly FlatBadge _badge;
        private readonly FlatDropdown _modeDropdown;
        private readonly FlatButton _deleteButton;
        private readonly FlatSlider _slider;
        private readonly Label _valueLabel;
        private readonly Label _infoLabel;
        private readonly FlatSegmented _segmented;
        private readonly Action _changed;
        private string _infoFullText = string.Empty;
        private bool _updating;
        private bool _dragging;
        private int _dragBase;

        public GroupCard(BrightnessService service, GroupConfig group, Action changed)
        {
            _service = service;
            _group = group;
            _changed = changed;

            Height = UiScale(CardLayout.Height);
            BorderColor = FlatTheme.Border;

            _nameLabel = CardLayout.MakeLabel(group.Name, FlatTheme.CardTitle, FlatTheme.TextPrimary);
            _nameLabel.Location = new Point(UiScale(CardLayout.Padding), UiScale(CardLayout.Row1 + 2));
            _nameLabel.Cursor = Cursors.Hand;
            _nameLabel.DoubleClick += delegate { RenameGroup(); };

            _badge = new FlatBadge
            {
                Text = "分组",
                Font = FlatTheme.Small,
                BadgeColor = FlatTheme.VioletSoft,
                BadgeTextColor = FlatTheme.Violet,
                BackColor = FlatTheme.Surface,
            };

            _modeDropdown = new FlatDropdown { Font = FlatTheme.Small };
            _modeDropdown.SelectionChanged = delegate (GroupChoice choice)
            {
                if (_updating || choice == null) return;
                _group.Mode = choice.Id == GroupMode.Relative ? GroupMode.Relative : GroupMode.Sync;
                _service.SaveSoon();
                UpdateTexts();
            };
            _modeDropdown.SetItems(new[]
            {
                new GroupChoice(GroupMode.Sync, "同步"),
                new GroupChoice(GroupMode.Relative, "相对"),
            }, group.Mode);

            _deleteButton = new FlatButton
            {
                Text = "解散",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Small,
                Clicked = Disband,
            };

            _slider = new FlatSlider
            {
                ValueChanged = delegate (int value)
                {
                    if (_updating) return;
                    OnSliderMoved(value);
                },
                DragCompleted = delegate
                {
                    _dragging = false;
                    if (_group.Mode == GroupMode.Relative) _service.EndGroupAdjustment();
                    _service.SaveSoon();
                },
            };
            _slider.MouseDown += delegate
            {
                _dragging = true;
                _dragBase = _slider.Value;
                if (_group.Mode == GroupMode.Relative) _service.BeginGroupAdjustment(_group);
            };
            _updating = true;
            _slider.Value = RepresentativePercent();
            _updating = false;

            _valueLabel = CardLayout.MakeLabel("", FlatTheme.Value, FlatTheme.TextPrimary);
            _infoLabel = CardLayout.MakeLabel("", FlatTheme.Small, FlatTheme.TextMuted);

            _segmented = new FlatSegmented(SetPercentToAll) { Font = FlatTheme.Small };

            Controls.AddRange(new Control[]
            {
                _nameLabel, _badge, _modeDropdown, _deleteButton, _slider, _valueLabel, _infoLabel, _segmented
            });

            UpdateTexts();
            _service.ValuesChanged += OnServiceValuesChanged;
        }

        public GroupConfig Group { get { return _group; } }

        private int RepresentativePercent()
        {
            foreach (var target in _service.Targets)
            {
                if (_group.Monitors.Contains(target.Key) && target.WhiteLevelKnown)
                {
                    return DisplayService.WhiteLevelToPercent(target.WhiteLevel);
                }
            }
            return 0;
        }

        private int ConnectedCount()
        {
            int count = 0;
            foreach (var target in _service.Targets)
            {
                if (_group.Monitors.Contains(target.Key)) count++;
            }
            return count;
        }

        private bool AnyMemberKnown()
        {
            foreach (var target in _service.Targets)
            {
                if (_group.Monitors.Contains(target.Key) && target.WhiteLevelKnown) return true;
            }
            return false;
        }

        private void OnSliderMoved(int value)
        {
            if (_group.Mode == GroupMode.Relative)
            {
                int deltaPercent = value - _dragBase;
                _dragBase = value;
                if (deltaPercent != 0)
                {
                    _service.ApplyGroupDelta(_group, (int)(deltaPercent * DisplayService.WhiteLevelPerPercent));
                }
            }
            else
            {
                _service.ApplyGroupValue(_group, DisplayService.PercentToWhiteLevel(value));
            }
            UpdateTexts();
        }

        private void SetPercentToAll(int percent)
        {
            if (_updating) return;
            _updating = true;
            _slider.Value = Math.Max(0, Math.Min(100, percent));
            _updating = false;
            _service.ApplyGroupValue(_group, DisplayService.PercentToWhiteLevel(percent));
            UpdateTexts();
        }

        private void Disband()
        {
            if (MessageBox.Show(FindForm(),
                "确定要解散分组“" + _group.Name + "”吗？显示器本身不会受影响。",
                "解散分组", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            _service.Settings.Groups.Remove(_group);
            _service.SaveSoon();
            if (_changed != null) _changed();
        }

        private void RenameGroup()
        {
            string result = PromptForm.Ask(FindForm(), "分组名称", "例如：主副屏一起调", _group.Name);
            if (string.IsNullOrWhiteSpace(result)) return;

            _group.Name = result.Trim();
            _nameLabel.Text = _group.Name;
            _service.SaveSoon();
            if (_changed != null) _changed();
        }

        private void OnServiceValuesChanged(object sender, EventArgs e)
        {
            if (_dragging && _group.Mode == GroupMode.Relative) return;

            _updating = true;
            _slider.Value = RepresentativePercent();
            _updating = false;
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            bool known = AnyMemberKnown();
            int percent = RepresentativePercent();
            _valueLabel.Text = known ? percent + "%" : "--";
            _segmented.SetSelected(known ? (int?)percent : null);
            _infoFullText = "组内 " + ConnectedCount() + " 台已连接"
                + (_group.Mode == GroupMode.Relative ? " · 按增量同步，保持差值" : " · 统一设为同一数值");
            _infoLabel.Text = _infoFullText;
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_slider == null || _nameLabel == null) return;

            int pad = UiScale(CardLayout.Padding);
            int row1 = UiScale(CardLayout.Row1);
            int row2 = UiScale(CardLayout.Row2);
            int row3 = UiScale(CardLayout.Row3);

            _badge.SetBounds(_nameLabel.Right + UiScale(10), row1 + 2, UiScale(44), UiScale(22));

            int deleteWidth = UiScale(52);
            _deleteButton.SetBounds(Width - pad - deleteWidth, row1 - 2, deleteWidth, UiScale(30));

            int modeWidth = UiScale(90);
            _modeDropdown.SetBounds(_deleteButton.Left - UiScale(8) - modeWidth, row1 - 2, modeWidth, UiScale(30));

            int valueWidth = UiScale(84);
            _slider.SetBounds(pad, row2, Math.Max(UiScale(120), Width - pad * 2 - valueWidth), UiScale(28));
            _valueLabel.Location = new Point(Math.Max(pad, Width - pad - _valueLabel.Width), row2 + UiScale(2));

            int segmentedWidth = Math.Min(UiScale(230), Math.Max(UiScale(160), (Width - pad * 2) * 2 / 5));
            _segmented.SetBounds(Width - pad - segmentedWidth, row3, segmentedWidth, UiScale(30));

            _infoLabel.Location = new Point(pad, row3 + UiScale(8));
            _infoLabel.Text = CardLayout.Ellipsize(_infoFullText, FlatTheme.Small,
                Math.Max(UiScale(60), _segmented.Left - pad - UiScale(10)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _service.ValuesChanged -= OnServiceValuesChanged;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>“全部显示器”卡片。</summary>
    internal sealed class AllMonitorsCard : FlatCard
    {
        private readonly BrightnessService _service;
        private readonly Label _nameLabel;
        private readonly FlatBadge _badge;
        private readonly FlatSlider _slider;
        private readonly Label _valueLabel;
        private readonly Label _infoLabel;
        private readonly FlatSegmented _segmented;
        private string _infoFullText = "所有输出统一设为同一个百分比";
        private bool _updating;

        public AllMonitorsCard(BrightnessService service)
        {
            _service = service;

            Height = UiScale(CardLayout.Height);
            BorderColor = FlatTheme.Border;

            _nameLabel = CardLayout.MakeLabel("全部显示器", FlatTheme.CardTitle, FlatTheme.TextPrimary);
            _nameLabel.Location = new Point(UiScale(CardLayout.Padding), UiScale(CardLayout.Row1 + 2));

            _badge = new FlatBadge
            {
                Text = "一次调所有输出",
                Font = FlatTheme.Small,
                BadgeColor = FlatTheme.AccentSoft,
                BadgeTextColor = FlatTheme.AccentText,
                BackColor = FlatTheme.Surface,
            };

            _slider = new FlatSlider
            {
                ValueChanged = delegate (int value)
                {
                    if (_updating) return;
                    _service.ApplyGlobalValue(DisplayService.PercentToWhiteLevel(value));
                    UpdateTexts();
                },
                DragCompleted = delegate { _service.SaveSoon(); },
            };
            _updating = true;
            _slider.Value = RepresentativePercent();
            _updating = false;

            _valueLabel = CardLayout.MakeLabel("", FlatTheme.Value, FlatTheme.TextPrimary);
            _infoLabel = CardLayout.MakeLabel(_infoFullText, FlatTheme.Small, FlatTheme.TextMuted);

            _segmented = new FlatSegmented(SetPercentToAll) { Font = FlatTheme.Small };

            Controls.AddRange(new Control[]
            {
                _nameLabel, _badge, _slider, _valueLabel, _infoLabel, _segmented
            });

            UpdateTexts();
            _service.ValuesChanged += OnServiceValuesChanged;
        }

        private int RepresentativePercent()
        {
            foreach (var target in _service.Targets)
            {
                if (target.WhiteLevelKnown) return DisplayService.WhiteLevelToPercent(target.WhiteLevel);
            }
            return 0;
        }

        private bool AnyKnown()
        {
            foreach (var target in _service.Targets)
            {
                if (target.WhiteLevelKnown) return true;
            }
            return false;
        }

        private void SetPercentToAll(int percent)
        {
            if (_updating) return;
            _updating = true;
            _slider.Value = Math.Max(0, Math.Min(100, percent));
            _updating = false;
            _service.ApplyGlobalValue(DisplayService.PercentToWhiteLevel(percent));
            UpdateTexts();
        }

        private void OnServiceValuesChanged(object sender, EventArgs e)
        {
            _updating = true;
            _slider.Value = RepresentativePercent();
            _updating = false;
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            bool known = AnyKnown();
            int percent = RepresentativePercent();
            _valueLabel.Text = known ? percent + "%" : "--";
            _segmented.SetSelected(known ? (int?)percent : null);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_slider == null || _nameLabel == null) return;

            int pad = UiScale(CardLayout.Padding);
            int row1 = UiScale(CardLayout.Row1);
            int row2 = UiScale(CardLayout.Row2);
            int row3 = UiScale(CardLayout.Row3);

            int badgeWidth = TextRenderer.MeasureText(_badge.Text, FlatTheme.Small).Width + UiScale(20);
            _badge.SetBounds(_nameLabel.Right + UiScale(10), row1 + 2, badgeWidth, UiScale(22));

            int valueWidth = UiScale(84);
            _slider.SetBounds(pad, row2, Math.Max(UiScale(120), Width - pad * 2 - valueWidth), UiScale(28));
            _valueLabel.Location = new Point(Math.Max(pad, Width - pad - _valueLabel.Width), row2 + UiScale(2));

            int segmentedWidth = Math.Min(UiScale(230), Math.Max(UiScale(160), (Width - pad * 2) * 2 / 5));
            _segmented.SetBounds(Width - pad - segmentedWidth, row3, segmentedWidth, UiScale(30));

            _infoLabel.Location = new Point(pad, row3 + UiScale(8));
            _infoLabel.Text = CardLayout.Ellipsize(_infoFullText, FlatTheme.Small,
                Math.Max(UiScale(60), _segmented.Left - pad - UiScale(10)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _service.ValuesChanged -= OnServiceValuesChanged;
            }
            base.Dispose(disposing);
        }
    }
}
