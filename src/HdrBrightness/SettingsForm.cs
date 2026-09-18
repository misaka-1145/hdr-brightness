using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HdrBrightness
{
    internal sealed class SettingsForm : Form
    {
        private readonly BrightnessService _service;
        private readonly FlatCheckBox _autoStartCheck;
        private readonly FlatCheckBox _startMinimizedCheck;
        private readonly FlatCheckBox _restoreCheck;
        private readonly FlatCheckBox _lockCheck;
        private readonly NumericUpDown _intervalBox;

        public SettingsForm(BrightnessService service)
        {
            _service = service;
            var settings = service.Settings;

            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = FlatTheme.Body;
            BackColor = FlatTheme.Surface;
            ClientSize = new Size(540, 448);

            int pad = 28;
            int y = 24;

            var title = new Label
            {
                Text = "设置",
                Font = FlatTheme.Title,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(pad, y),
            };
            Controls.Add(title);
            y += 46;

            _autoStartCheck = AddCheckBox("开机自启（登录后自动在托盘运行）", pad, ref y, AutoStart.IsEnabled());
            _startMinimizedCheck = AddCheckBox("手动启动时也直接进托盘（不弹主窗口）", pad, ref y, settings.StartMinimizedToTray);
            _restoreCheck = AddCheckBox("启动时恢复上次的亮度", pad, ref y, settings.RestoreOnStartup);
            _lockCheck = AddCheckBox("锁定亮度（切换 HDR、唤醒、重新插显示器后自动恢复）", pad, ref y, settings.LockBrightness);

            y += 10;
            var intervalLabel = new Label
            {
                Text = "检查间隔（秒）",
                Font = FlatTheme.Body,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(pad, y + 8),
            };
            Controls.Add(intervalLabel);

            var intervalBox = new Panel
            {
                Location = new Point(pad + 170, y),
                Size = new Size(96, 32),
                BackColor = FlatTheme.Surface,
            };
            intervalBox.Paint += delegate (object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                FlatTheme.DrawRounded(e.Graphics,
                    new Rectangle(0, 0, intervalBox.Width - 1, intervalBox.Height - 1), 8, FlatTheme.BorderStrong, 1f);
            };
            _intervalBox = new NumericUpDown
            {
                Minimum = 5,
                Maximum = 600,
                Value = Math.Min(600, Math.Max(5, settings.CheckIntervalSeconds)),
                Font = FlatTheme.Body,
                BorderStyle = BorderStyle.None,
                BackColor = FlatTheme.Surface,
                Increment = 5,
                TextAlign = HorizontalAlignment.Center,
                Dock = DockStyle.Fill,
            };
            intervalBox.Controls.Add(_intervalBox);
            intervalBox.Padding = new Padding(6, 6, 6, 4);
            Controls.Add(intervalBox);

            y += 48;
            var hint = new Label
            {
                Text = "百分比与 Windows 设置的滑块一致（0% 最暗，100% 最亮）",
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.TextMuted,
                AutoSize = false,
                Size = new Size(ClientSize.Width - pad * 2, 20),
                Location = new Point(pad, y),
            };
            Controls.Add(hint);

            y += 30;
            var configLabel = new Label
            {
                Text = "配置文件：" + AppSettings.ConfigPath,
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.TextMuted,
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(ClientSize.Width - pad * 2, 20),
                Location = new Point(pad, y),
            };
            Controls.Add(configLabel);

            y += 34;
            var openConfigButton = new FlatButton
            {
                Text = "打开配置文件夹",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Location = new Point(pad, y),
                Size = new Size(150, 34),
                Clicked = OpenConfigFolder,
            };
            Controls.Add(openConfigButton);

            var resetButton = new FlatButton
            {
                Text = "全部设为 0%",
                Kind = FlatButtonKind.Plain,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Location = new Point(pad + 158, y),
                Size = new Size(130, 34),
                Clicked = delegate { _service.ApplyGlobalValue(DisplayService.PercentToWhiteLevel(0)); },
            };
            Controls.Add(resetButton);

            var separator = new Panel
            {
                Location = new Point(pad, ClientSize.Height - 74),
                Size = new Size(ClientSize.Width - pad * 2, 1),
                BackColor = FlatTheme.Border,
            };
            Controls.Add(separator);

            var okButton = new FlatButton
            {
                Text = "保存",
                Kind = FlatButtonKind.Primary,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Size = new Size(96, 36),
                Clicked = delegate
                {
                    Apply();
                    DialogResult = DialogResult.OK;
                },
            };
            okButton.Location = new Point(ClientSize.Width - pad - okButton.Width, ClientSize.Height - 56);

            var cancelButton = new FlatButton
            {
                Text = "取消",
                Kind = FlatButtonKind.Subtle,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Size = new Size(96, 36),
                Clicked = delegate { DialogResult = DialogResult.Cancel; },
            };
            cancelButton.Location = new Point(okButton.Left - 12 - cancelButton.Width, ClientSize.Height - 56);

            Controls.AddRange(new Control[] { okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private FlatCheckBox AddCheckBox(string text, int left, ref int y, bool isChecked)
        {
            var check = new FlatCheckBox
            {
                Text = text,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Checked = isChecked,
                Location = new Point(left, y),
                Size = new Size(ClientSize.Width - left * 2, 28),
            };
            Controls.Add(check);
            y += 38;
            return check;
        }

        private void OpenConfigFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppSettings.ConfigDirectory);
                Process.Start("explorer.exe", "\"" + AppSettings.ConfigDirectory + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Apply()
        {
            var settings = _service.Settings;

            settings.StartMinimizedToTray = _startMinimizedCheck.Checked;
            settings.RestoreOnStartup = _restoreCheck.Checked;
            settings.LockBrightness = _lockCheck.Checked;
            settings.CheckIntervalSeconds = (int)_intervalBox.Value;

            string error;
            if (!AutoStart.SetEnabled(_autoStartCheck.Checked, out error) && error != null)
            {
                MessageBox.Show(this, "写入开机自启失败：" + error, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            settings.AutoStart = _autoStartCheck.Checked;

            _service.UpdateWatchdog();
            _service.SaveNow();
            _service.ReloadCurrentValues();
        }
    }
}
