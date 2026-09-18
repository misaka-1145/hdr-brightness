using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HdrBrightness
{
    /// <summary>“定时规则”卡片：列出所有规则，可增删改。</summary>
    internal sealed class ScheduleCard : FlatCard
    {
        private readonly BrightnessService _service;
        private readonly Action _changed;
        private readonly Label _titleLabel;
        private readonly FlatBadge _badge;
        private readonly FlatButton _addButton;
        private readonly Label _emptyLabel;
        private readonly List<Control> _rowControls = new List<Control>();

        public ScheduleCard(BrightnessService service, Action changed)
        {
            _service = service;
            _changed = changed;
            BorderColor = FlatTheme.Border;

            _titleLabel = CardLayout.MakeLabel("定时规则", FlatTheme.CardTitle, FlatTheme.TextPrimary);
            _titleLabel.Location = new Point(UiScale(20), UiScale(16));

            _badge = new FlatBadge
            {
                Text = "到点自动调整",
                Font = FlatTheme.Small,
                BadgeColor = FlatTheme.AccentSoft,
                BadgeTextColor = FlatTheme.AccentText,
                BackColor = FlatTheme.Surface,
            };

            _addButton = new FlatButton
            {
                Text = "添加规则",
                Kind = FlatButtonKind.Primary,
                Font = FlatTheme.Small,
                Clicked = AddRule,
            };

            _emptyLabel = CardLayout.MakeLabel("还没有定时规则。点“添加规则”，例如 08:00 设为 100%、20:00 设为 40%。",
                FlatTheme.Body, FlatTheme.TextMuted);

            Controls.AddRange(new Control[] { _titleLabel, _badge, _addButton, _emptyLabel });
            BuildRows();
        }

        private void BuildRows()
        {
            foreach (var control in _rowControls)
            {
                Controls.Remove(control);
                control.Dispose();
            }
            _rowControls.Clear();

            foreach (var rule in _service.Settings.Schedules)
            {
                var row = new ScheduleRowControl(_service, rule, EditRule, DeleteRule);
                Controls.Add(row);
                _rowControls.Add(row);
            }

            _emptyLabel.Visible = _service.Settings.Schedules.Count == 0;
            Height = UiScale(56 + Math.Max(1, _service.Settings.Schedules.Count) * 40 + 12);
            LayoutChildren();
        }

        private void AddRule()
        {
            var rule = new ScheduleRule();
            rule.Hour = 8;
            rule.Percent = 100;
            using (var form = new ScheduleEditForm(_service, rule, true))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK) return;
                _service.Settings.Schedules.Add(form.Result);
                _service.SaveNow();
                BuildRows();
                if (_changed != null) _changed();
            }
        }

        private void EditRule(ScheduleRule rule)
        {
            using (var form = new ScheduleEditForm(_service, rule, false))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK) return;

                var edited = form.Result;
                rule.Hour = edited.Hour;
                rule.Minute = edited.Minute;
                rule.Days = edited.Days;
                rule.TargetKind = edited.TargetKind;
                rule.TargetId = edited.TargetId;
                rule.Percent = edited.Percent;
                rule.Enabled = edited.Enabled;

                _service.SaveNow();
                BuildRows();
                if (_changed != null) _changed();
            }
        }

        private void DeleteRule(ScheduleRule rule)
        {
            if (MessageBox.Show(FindForm(),
                "删除定时规则“" + rule.TimeText + " → " + rule.Percent + "%”吗？",
                "删除定时规则", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            _service.Settings.Schedules.Remove(rule);
            _service.SaveNow();
            BuildRows();
            if (_changed != null) _changed();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChildren();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_titleLabel == null) return;

            int pad = UiScale(20);
            int badgeWidth = TextRenderer.MeasureText(_badge.Text, FlatTheme.Small).Width + UiScale(20);
            _badge.SetBounds(_titleLabel.Right + UiScale(10), UiScale(18), badgeWidth, UiScale(22));

            int buttonWidth = UiScale(96);
            _addButton.SetBounds(Width - pad - buttonWidth, UiScale(14), buttonWidth, UiScale(30));
            _emptyLabel.Location = new Point(pad, UiScale(62));

            int top = UiScale(56);
            foreach (var control in _rowControls)
            {
                control.SetBounds(pad - UiScale(6), top, Width - pad * 2 + UiScale(12), UiScale(36));
                top += UiScale(40);
            }
        }
    }

    /// <summary>一条定时规则。</summary>
    internal sealed class ScheduleRowControl : Panel
    {
        private readonly BrightnessService _service;
        private readonly ScheduleRule _rule;
        private readonly FlatCheckBox _enabledCheck;
        private readonly Label _timeLabel;
        private readonly Label _targetLabel;
        private readonly Label _percentLabel;
        private readonly FlatButton _editButton;
        private readonly FlatButton _deleteButton;
        private string _targetFullText = string.Empty;

        public ScheduleRowControl(BrightnessService service, ScheduleRule rule, Action<ScheduleRule> edit, Action<ScheduleRule> delete)
        {
            _service = service;
            _rule = rule;
            BackColor = FlatTheme.Surface;

            _enabledCheck = new FlatCheckBox
            {
                Text = "",
                BackColor = FlatTheme.Surface,
                Checked = rule.Enabled,
                Size = new Size(FlatTheme.Scale(this, 22), FlatTheme.Scale(this, 24)),
            };
            _enabledCheck.CheckedChanged = delegate
            {
                _rule.Enabled = _enabledCheck.Checked;
                _service.SaveNow();
                UpdateTexts();
            };

            _timeLabel = CardLayout.MakeLabel(rule.TimeText, FlatTheme.BodyBold, FlatTheme.TextPrimary);
            _targetLabel = CardLayout.MakeLabel("", FlatTheme.Small, FlatTheme.TextSecondary);
            _percentLabel = CardLayout.MakeLabel(rule.Percent + "%", FlatTheme.BodyBold, FlatTheme.TextPrimary);

            _editButton = new FlatButton
            {
                Text = "修改",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Small,
                Clicked = delegate { if (edit != null) edit(_rule); },
            };

            _deleteButton = new FlatButton
            {
                Text = "删除",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Small,
                Clicked = delegate { if (delete != null) delete(_rule); },
            };

            Controls.AddRange(new Control[]
            {
                _enabledCheck, _timeLabel, _targetLabel, _percentLabel, _editButton, _deleteButton
            });

            // 点空白处也能打开编辑
            Click += delegate { if (edit != null) edit(_rule); };
            _timeLabel.Cursor = Cursors.Hand;
            _timeLabel.Click += delegate { if (edit != null) edit(_rule); };
            _targetLabel.Cursor = Cursors.Hand;
            _targetLabel.Click += delegate { if (edit != null) edit(_rule); };

            UpdateTexts();
        }

        private void UpdateTexts()
        {
            _targetFullText = ScheduleDays.Describe(_rule.Days) + " · " + _service.DescribeScheduleTarget(_rule);
            _targetLabel.Text = _targetFullText;
            _percentLabel.Text = _rule.Percent + "%";

            Color color = _rule.Enabled ? FlatTheme.TextPrimary : FlatTheme.TextMuted;
            _timeLabel.ForeColor = color;
            _percentLabel.ForeColor = color;
            _targetLabel.ForeColor = _rule.Enabled ? FlatTheme.TextSecondary : FlatTheme.TextMuted;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_enabledCheck == null) return;

            int height = Height;
            int checkSize = FlatTheme.Scale(this, 22);
            _enabledCheck.SetBounds(FlatTheme.Scale(this, 8), (height - FlatTheme.Scale(this, 24)) / 2,
                checkSize, FlatTheme.Scale(this, 24));

            _timeLabel.Location = new Point(_enabledCheck.Right + FlatTheme.Scale(this, 12),
                (height - _timeLabel.Height) / 2);

            int deleteWidth = FlatTheme.Scale(this, 46);
            int editWidth = FlatTheme.Scale(this, 46);
            _deleteButton.SetBounds(Width - deleteWidth - FlatTheme.Scale(this, 8),
                (height - FlatTheme.Scale(this, 26)) / 2, deleteWidth, FlatTheme.Scale(this, 26));
            _editButton.SetBounds(_deleteButton.Left - FlatTheme.Scale(this, 4) - editWidth,
                (height - FlatTheme.Scale(this, 26)) / 2, editWidth, FlatTheme.Scale(this, 26));

            int percentWidth = FlatTheme.Scale(this, 52);
            _percentLabel.Location = new Point(_editButton.Left - FlatTheme.Scale(this, 8) - percentWidth,
                (height - _percentLabel.Height) / 2);

            int targetLeft = _timeLabel.Right + FlatTheme.Scale(this, 12);
            int targetWidth = Math.Max(FlatTheme.Scale(this, 60), _percentLabel.Left - targetLeft - FlatTheme.Scale(this, 8));
            _targetLabel.Location = new Point(targetLeft, (height - _targetLabel.Height) / 2);
            _targetLabel.Text = CardLayout.Ellipsize(_targetFullText, FlatTheme.Small, targetWidth);
        }
    }

    /// <summary>新建 / 编辑定时规则。</summary>
    internal sealed class ScheduleEditForm : Form
    {
        private readonly BrightnessService _service;
        private readonly ScheduleRule _rule;
        private readonly NumericUpDown _hourBox;
        private readonly NumericUpDown _minuteBox;
        private readonly FlatDropdown _daysDropdown;
        private readonly FlatDropdown _targetDropdown;
        private readonly FlatSlider _slider;
        private readonly Label _percentLabel;
        private readonly FlatSegmented _segmented;

        public ScheduleEditForm(BrightnessService service, ScheduleRule rule, bool isNew)
        {
            _service = service;
            _rule = rule;

            Text = isNew ? "添加定时规则" : "修改定时规则";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = FlatTheme.Body;
            BackColor = FlatTheme.Surface;
            ClientSize = new Size(500, 424);

            int pad = 28;
            int y = 24;

            var title = new Label
            {
                Text = isNew ? "添加定时规则" : "修改定时规则",
                Font = FlatTheme.Title,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(pad, y),
            };
            Controls.Add(title);
            y += 52;

            // 时间
            Controls.Add(FieldLabel("时间", pad, y));
            var hourHost = RoundedField(pad + 96, y - 4, 78, 34);
            _hourBox = NumberBox(0, 23, rule.Hour);
            hourHost.Controls.Add(_hourBox);
            Controls.Add(hourHost);

            var colon = new Label
            {
                Text = "：",
                Font = FlatTheme.BodyBold,
                ForeColor = FlatTheme.TextSecondary,
                AutoSize = true,
                Location = new Point(hourHost.Right + 4, y + 6),
            };
            Controls.Add(colon);

            var minuteHost = RoundedField(colon.Right + 6, y - 4, 78, 34);
            _minuteBox = NumberBox(0, 59, rule.Minute);
            minuteHost.Controls.Add(_minuteBox);
            Controls.Add(minuteHost);
            y += 52;

            // 重复
            Controls.Add(FieldLabel("重复", pad, y));
            _daysDropdown = new FlatDropdown { Font = FlatTheme.Body };
            var dayItems = new List<GroupChoice>();
            for (int i = 0; i < ScheduleDays.OptionLabels.Length; i++)
            {
                dayItems.Add(new GroupChoice(ScheduleDays.OptionValues[i], ScheduleDays.OptionLabels[i]));
            }
            _daysDropdown.SetItems(dayItems, rule.Days);
            _daysDropdown.SetBounds(pad + 96, y - 4, 180, 34);
            Controls.Add(_daysDropdown);
            y += 52;

            // 目标
            Controls.Add(FieldLabel("目标", pad, y));
            _targetDropdown = new FlatDropdown { Font = FlatTheme.Body };
            _targetDropdown.SetItems(BuildTargetItems(service), rule.TargetKind + "|" + rule.TargetId);
            _targetDropdown.SetBounds(pad + 96, y - 4, 300, 34);
            Controls.Add(_targetDropdown);
            y += 56;

            // 亮度
            Controls.Add(FieldLabel("亮度", pad, y + 2));
            _percentLabel = new Label
            {
                Text = rule.Percent + "%",
                Font = FlatTheme.Value,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(ClientSize.Width - pad - 56, y - 6),
            };
            Controls.Add(_percentLabel);

            _slider = new FlatSlider
            {
                BackColor = FlatTheme.Surface,
                Height = 28,
                ValueChanged = delegate (int value)
                {
                    _percentLabel.Text = value + "%";
                    if (_segmented != null) _segmented.SetSelected(value);
                },
            };
            _slider.SetBounds(pad + 96, y, ClientSize.Width - pad * 2 - 96 - 64, 28);
            _slider.Value = Math.Max(0, Math.Min(100, rule.Percent));
            Controls.Add(_slider);
            y += 40;

            _segmented = new FlatSegmented(delegate (int percent)
            {
                _slider.Value = percent;
                _percentLabel.Text = percent + "%";
            })
            {
                Font = FlatTheme.Small,
                BackColor = FlatTheme.Surface,
            };
            _segmented.SetBounds(pad + 96, y, 230, 30);
            _segmented.SetSelected(rule.Percent);
            Controls.Add(_segmented);
            y += 54;

            var separator = new Panel
            {
                Location = new Point(pad, ClientSize.Height - 68),
                Size = new Size(ClientSize.Width - pad * 2, 1),
                BackColor = FlatTheme.Border,
            };
            Controls.Add(separator);

            var applyNow = new FlatButton
            {
                Text = "立即应用一次",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Size = new Size(132, 36),
                Location = new Point(pad, ClientSize.Height - 52),
                Clicked = delegate
                {
                    CaptureInputs();
                    string text = _service.ApplySchedule(_rule);
                    MessageBox.Show(this, text, "定时规则", MessageBoxButtons.OK, MessageBoxIcon.Information);
                },
            };
            Controls.Add(applyNow);

            var okButton = new FlatButton
            {
                Text = "保存",
                Kind = FlatButtonKind.Primary,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Size = new Size(92, 36),
            };
            okButton.Location = new Point(ClientSize.Width - pad - okButton.Width, ClientSize.Height - 52);
            okButton.Clicked = delegate
            {
                CaptureInputs();
                DialogResult = DialogResult.OK;
            };

            var cancelButton = new FlatButton
            {
                Text = "取消",
                Kind = FlatButtonKind.Subtle,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Size = new Size(92, 36),
                Clicked = delegate { DialogResult = DialogResult.Cancel; },
            };
            cancelButton.Location = new Point(okButton.Left - 12 - cancelButton.Width, ClientSize.Height - 52);

            Controls.AddRange(new Control[] { okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        /// <summary>对话框结果（保存时用克隆，取消不影响原对象）。</summary>
        public ScheduleRule Result { get; private set; }

        private static Label FieldLabel(string text, int left, int top)
        {
            return new Label
            {
                Text = text,
                Font = FlatTheme.Body,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(left, top + 6),
            };
        }

        private static Panel RoundedField(int left, int top, int width, int height)
        {
            var host = new Panel
            {
                Location = new Point(left, top),
                Size = new Size(width, height),
                BackColor = FlatTheme.Surface,
                Padding = new Padding(6, 6, 6, 4),
            };
            host.Paint += delegate (object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                FlatTheme.DrawRounded(e.Graphics, new Rectangle(0, 0, host.Width - 1, host.Height - 1),
                    8, FlatTheme.BorderStrong, 1f);
            };
            return host;
        }

        private static NumericUpDown NumberBox(int min, int max, int value)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                Font = FlatTheme.Body,
                BorderStyle = BorderStyle.None,
                BackColor = FlatTheme.Surface,
                TextAlign = HorizontalAlignment.Center,
                Dock = DockStyle.Fill,
            };
        }

        private static List<GroupChoice> BuildTargetItems(BrightnessService service)
        {
            var items = new List<GroupChoice>();
            items.Add(new GroupChoice(ScheduleTarget.All + "|", "全部显示器"));
            foreach (var group in service.Settings.Groups)
            {
                items.Add(new GroupChoice(ScheduleTarget.Group + "|" + group.Id, "分组：" + group.Name));
            }
            foreach (var target in service.Targets)
            {
                items.Add(new GroupChoice(ScheduleTarget.Monitor + "|" + target.Key, service.Settings.GetDisplayName(target)));
            }
            return items;
        }

        private void CaptureInputs()
        {
            var result = new ScheduleRule();
            result.Id = _rule.Id;
            result.Hour = (int)_hourBox.Value;
            result.Minute = (int)_minuteBox.Value;
            result.Enabled = _rule.Enabled;
            result.Percent = _slider.Value;

            var days = _daysDropdown.Selected;
            result.Days = days == null ? ScheduleDays.EveryDay : days.Id;

            var target = _targetDropdown.Selected;
            if (target == null)
            {
                result.TargetKind = ScheduleTarget.All;
                result.TargetId = string.Empty;
            }
            else
            {
                int split = target.Id.IndexOf('|');
                result.TargetKind = split < 0 ? ScheduleTarget.All : target.Id.Substring(0, split);
                result.TargetId = split < 0 ? string.Empty : target.Id.Substring(split + 1);
            }

            Result = result;
        }
    }
}
