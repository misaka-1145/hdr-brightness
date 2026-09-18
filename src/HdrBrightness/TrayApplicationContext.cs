using System;
using System.Drawing;
using System.Windows.Forms;

namespace HdrBrightness
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly BrightnessService _service;
        private readonly MainForm _form;
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _presetMenuItem;
        private bool _balloonShown;
        private bool _exiting;

        public TrayApplicationContext(BrightnessService service, bool startHidden)
        {
            _service = service;

            _form = new MainForm(service);
            _form.FormClosing += OnFormClosing;

            _autoStartItem = new ToolStripMenuItem("开机自启");
            _autoStartItem.CheckOnClick = false;
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _autoStartItem.Click += delegate
            {
                bool target = !_autoStartItem.Checked;
                string error;
                if (!AutoStart.SetEnabled(target, out error))
                {
                    MessageBox.Show(_form, "写入开机自启失败：" + error, "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _autoStartItem.Checked = target;
                _service.Settings.AutoStart = target;
                _service.SaveNow();
            };

            var menu = new ContextMenuStrip();
            menu.Renderer = new FlatMenuRenderer();
            menu.ShowImageMargin = false;
            menu.Font = FlatTheme.Body;
            menu.BackColor = FlatTheme.Surface;
            menu.Padding = new Padding(4);
            menu.Items.Add(CreateItem("显示主窗口", delegate { ShowMainWindow(); }));
            menu.Items.Add(CreateItem("刷新显示器", delegate { _service.Refresh(); }));
            menu.Items.Add(CreateItem("立即恢复亮度", delegate { _service.EnforceDesiredValues(false); }));
            menu.Items.Add(new ToolStripSeparator());

            _presetMenuItem = new ToolStripMenuItem("常用亮度");
            menu.Items.Add(_presetMenuItem);

            menu.Items.Add(_autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateItem("退出", delegate { ExitApplication(); }));

            // 每次弹出前重建，这样新建的分组会立刻出现在菜单里
            menu.Opening += delegate { RebuildPresetMenu(); };

            _trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "HDR 内容亮度调节",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _trayIcon.DoubleClick += delegate { ShowMainWindow(); };

            if (startHidden)
            {
                ShowBalloonOnce();
            }
            else
            {
                ShowMainWindow();
            }
        }

        private static ToolStripMenuItem CreateItem(string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void RebuildPresetMenu()
        {
            _presetMenuItem.DropDownItems.Clear();

            foreach (int percent in FlatSegmented.Presets)
            {
                int value = percent;
                var item = new ToolStripMenuItem(value + "%");
                item.Click += delegate
                {
                    _service.ApplyGlobalValue(DisplayService.PercentToWhiteLevel(value));
                };
                _presetMenuItem.DropDownItems.Add(item);
            }

            if (_service.Settings.Groups.Count > 0)
            {
                _presetMenuItem.DropDownItems.Add(new ToolStripSeparator());
                foreach (var group in _service.Settings.Groups)
                {
                    var groupRoot = new ToolStripMenuItem("分组：" + group.Name);
                    foreach (int percent in FlatSegmented.Presets)
                    {
                        int value = percent;
                        var item = new ToolStripMenuItem(value + "%");
                        item.Click += delegate
                        {
                            _service.ApplyGroupValue(group, DisplayService.PercentToWhiteLevel(value));
                        };
                        groupRoot.DropDownItems.Add(item);
                    }
                    _presetMenuItem.DropDownItems.Add(groupRoot);
                }
            }
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch (Exception)
            {
            }
            return SystemIcons.Application;
        }

        public void ShowMainWindow()
        {
            _form.ShowFromTray();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;

            // 点右上角叉：收进托盘，不退出程序
            _form.AllowClose = false;
            e.Cancel = true;
            _form.Hide();
            ShowBalloonOnce();
        }

        private void ShowBalloonOnce()
        {
            if (_balloonShown) return;
            _balloonShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(2500, "HDR 内容亮度调节",
                    "程序仍在后台运行，双击托盘图标可以重新打开窗口。", ToolTipIcon.Info);
            }
            catch (Exception)
            {
            }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;

            try
            {
                _service.SaveNow();
                _service.Dispose();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();

                _form.AllowClose = true;
                _form.Close();
            }
            catch (Exception)
            {
            }

            ExitThread();
        }
    }
}
