using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HdrBrightness
{
    internal sealed class MainForm : Form
    {
        private readonly BrightnessService _service;
        private readonly List<Control> _rows = new List<Control>();
        private readonly Panel _headerPanel;
        private readonly Panel _cardHost;
        private readonly Label _statusLabel;
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;
        private readonly FlatButton _refreshButton;
        private readonly FlatButton _newGroupButton;
        private readonly FlatButton _settingsButton;
        private readonly FlatButton _hideButton;
        private readonly Panel _separator;
        private bool _rebuilding;

        public MainForm(BrightnessService service)
        {
            _service = service;

            Text = "HDR 内容亮度";
            Font = FlatTheme.Body;
            BackColor = FlatTheme.PageBackground;
            ClientSize = new Size(680, 700);
            MinimumSize = new Size(600, 460);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            _titleLabel = new Label
            {
                Text = "HDR 内容亮度",
                Font = FlatTheme.Title,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
            _subtitleLabel = new Label
            {
                Text = "HDR 模式下 SDR 内容的亮度 · 百分比与 Windows 设置一致",
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.TextMuted,
                AutoSize = true,
                BackColor = Color.Transparent,
            };

            _refreshButton = MakeHeaderButton("刷新", FlatButtonKind.Plain);
            _refreshButton.Clicked = delegate
            {
                _service.Refresh();
                _service.EnforceDesiredValues(true);
            };
            _newGroupButton = MakeHeaderButton("新建分组", FlatButtonKind.Primary);
            _newGroupButton.Clicked = CreateGroup;
            _settingsButton = MakeHeaderButton("设置", FlatButtonKind.Plain);
            _settingsButton.Clicked = OpenSettings;
            _hideButton = MakeHeaderButton("收起", FlatButtonKind.Plain);
            _hideButton.Clicked = delegate { Hide(); };

            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                BackColor = FlatTheme.PageBackground,
            };
            _headerPanel.Controls.AddRange(new Control[]
            {
                _titleLabel, _subtitleLabel, _refreshButton, _newGroupButton, _settingsButton, _hideButton
            });

            _separator = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = FlatTheme.Border };

            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.TextMuted,
                BackColor = FlatTheme.PageBackground,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _cardHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FlatTheme.PageBackground,
            };
            _cardHost.Resize += delegate { Relayout(); };

            Controls.Add(_cardHost);
            Controls.Add(_statusLabel);
            Controls.Add(_separator);
            Controls.Add(_headerPanel);

            _service.TargetsChanged += OnTargetsChanged;
            _service.StatusChanged += OnStatusChanged;
            DpiChanged += delegate { RebuildCards(); };

            RebuildCards();
            _statusLabel.Text = "  已识别 " + _service.Targets.Count + " 个显示输出";
        }

        public bool AllowClose { get; set; }

        /// <summary>所有卡片排完需要的高度（调试截图用，把窗口撑到能显示全部内容）。</summary>
        public int GetPreferredHeight()
        {
            int pad = FlatTheme.Scale(this, 22);
            int top = FlatTheme.Scale(this, 6);
            foreach (var row in _rows)
            {
                var card = row as FlatCard;
                top += card != null ? card.Height + FlatTheme.Scale(this, 12) : FlatTheme.Scale(this, 32);
            }
            return top + _headerPanel.Height + _statusLabel.Height + pad;
        }

        private FlatButton MakeHeaderButton(string text, FlatButtonKind kind)
        {
            return new FlatButton
            {
                Text = text,
                Kind = kind,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.PageBackground,
                Size = new Size(FlatTheme.Scale(this, 84), FlatTheme.Scale(this, 32)),
            };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutHeader();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutHeader();
            Relayout();
        }

        private void LayoutHeader()
        {
            if (_headerPanel == null) return;

            int pad = FlatTheme.Scale(this, 22);
            _titleLabel.Location = new Point(pad, FlatTheme.Scale(this, 18));
            _subtitleLabel.Location = new Point(pad + FlatTheme.Scale(this, 2), FlatTheme.Scale(this, 50));

            int y = FlatTheme.Scale(this, 22);
            int gap = FlatTheme.Scale(this, 8);
            int x = _headerPanel.ClientSize.Width - pad - _hideButton.Width;
            _hideButton.Location = new Point(x, y);
            x -= _settingsButton.Width + gap;
            _settingsButton.Location = new Point(x, y);
            x -= _newGroupButton.Width + gap;
            _newGroupButton.Location = new Point(x, y);
            x -= _refreshButton.Width + gap;
            _refreshButton.Location = new Point(x, y);
        }

        private void CreateGroup()
        {
            var group = new GroupConfig();
            group.Name = "分组 " + (_service.Settings.Groups.Count + 1);
            _service.Settings.Groups.Add(group);
            _service.SaveSoon();
            RebuildCards();
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_service))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RebuildCards();
                }
            }
        }

        private void OnTargetsChanged(object sender, EventArgs e)
        {
            RebuildCards();
        }

        private void OnStatusChanged(object sender, string message)
        {
            _statusLabel.Text = "  " + message;
        }

        private void OnCardsChanged()
        {
            RebuildCards();
        }

        private void RebuildCards()
        {
            if (_rebuilding) return;
            _rebuilding = true;
            try
            {
                _cardHost.SuspendLayout();

                foreach (var row in _rows)
                {
                    _cardHost.Controls.Remove(row);
                    row.Dispose();
                }
                _rows.Clear();
                _cardHost.Controls.Clear();

                if (_service.HasProblems)
                {
                    _rows.Add(BuildWarningCard());
                }

                _rows.Add(new SectionHeader("分组"));
                _rows.Add(new AllMonitorsCard(_service));

                foreach (var group in _service.Settings.Groups)
                {
                    _rows.Add(new GroupCard(_service, group, OnCardsChanged));
                }

                _rows.Add(new SectionHeader("显示器"));

                if (_service.Targets.Count == 0)
                {
                    var empty = new Label
                    {
                        Text = string.IsNullOrEmpty(_service.EnumerationError)
                            ? "没有检测到活动的显示器。"
                            : _service.EnumerationError,
                        Font = FlatTheme.Body,
                        ForeColor = FlatTheme.TextSecondary,
                        AutoSize = true,
                        BackColor = FlatTheme.PageBackground,
                    };
                    empty.Tag = "empty";
                    _rows.Add(empty);
                }
                else
                {
                foreach (var target in _service.Targets)
                {
                    _rows.Add(new MonitorCard(_service, target, OnCardsChanged));
                }

                _rows.Add(new SectionHeader("定时"));
                _rows.Add(new ScheduleCard(_service, OnCardsChanged));
                }

                foreach (var row in _rows)
                {
                    _cardHost.Controls.Add(row);
                }

                _cardHost.ResumeLayout();
                Relayout();
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private Control BuildWarningCard()
        {
            string title;
            string body;

            if (_service.Targets.Count == 0)
            {
                title = "没有读到任何显示输出";
                body = (_service.EnumerationError ?? "系统没有返回可用的显示输出。")
                    + " 点“重新检测”可以再试一次，也可以用“复制诊断信息”把细节发出来。";
            }
            else
            {
                int code = 0;
                foreach (var target in _service.Targets)
                {
                    if (!target.WhiteLevelKnown) { code = target.WhiteLevelError; break; }
                }

                title = "系统拒绝了亮度读取请求（错误码 " + code + "）";
                body = "滑块仍然可以试着拖动，但很可能同样失败。可以先重启，或用“复制诊断信息”把细节发出来。";
            }

            return new WarningCard(title, body, delegate { _service.Refresh(); });
        }

        private void Relayout()
        {
            if (_cardHost == null || _rows.Count == 0) return;

            int pad = FlatTheme.Scale(this, 22);
            int top = FlatTheme.Scale(this, 6);
            int width = _cardHost.ClientSize.Width - pad * 2 - SystemInformation.VerticalScrollBarWidth;
            if (width < FlatTheme.Scale(this, 260)) width = FlatTheme.Scale(this, 260);

            foreach (var row in _rows)
            {
                var card = row as FlatCard;
                if (card != null)
                {
                    card.SetBounds(pad, top, width, card.Height);
                    top += card.Height + FlatTheme.Scale(this, 12);
                }
                else
                {
                    int height = row is SectionHeader ? FlatTheme.Scale(this, 22) : FlatTheme.Scale(this, 20);
                    row.SetBounds(pad + FlatTheme.Scale(this, 2), top, width, height);
                    top += height + FlatTheme.Scale(this, 10);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowWindowMessage)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _service.TargetsChanged -= OnTargetsChanged;
                _service.StatusChanged -= OnStatusChanged;
            }
            base.Dispose(disposing);
        }
    }
}
