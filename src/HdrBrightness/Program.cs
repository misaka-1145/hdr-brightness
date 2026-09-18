using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HdrBrightness
{
    internal static class Program
    {
        private const int HwndBroadcast = 0xFFFF;

        /// <summary>第二个实例启动时用它通知第一个实例把窗口显示出来。</summary>
        public static readonly int ShowWindowMessage =
            (int)DisplayConfigNative.RegisterWindowMessage("HdrBrightness_ShowWindowMessage");

        private static Mutex _instanceMutex;

        [STAThread]
        internal static void Main(string[] args)
        {
            // 调试用：把主窗口渲染成 PNG（不显示在屏幕上），方便检查界面
            if (args.Length >= 2 && string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                UseTemporaryConfigForScreenshots();
                RenderScreenshot(args[1], args);
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--screenshot-settings", StringComparison.OrdinalIgnoreCase))
            {
                UseTemporaryConfigForScreenshots();
                RenderSettingsScreenshot(args[1], args);
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--screenshot-schedule", StringComparison.OrdinalIgnoreCase))
            {
                UseTemporaryConfigForScreenshots();
                RenderScheduleScreenshot(args[1]);
                return;
            }

            bool startHidden = HasArgument(args, "--tray");

            bool isFirstInstance;
            _instanceMutex = new Mutex(true, @"Local\HdrBrightness_SingleInstance", out isFirstInstance);
            if (!isFirstInstance)
            {
                // 已经有一个在跑了，把它的窗口叫出来
                DisplayConfigNative.PostMessage(
                    new IntPtr(HwndBroadcast), (uint)ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("程序出现异常：" + e.Exception.Message, "HDR 内容亮度",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                DisplayConfigNative.AssertLayout();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "HDR 内容亮度", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppSettings settings = AppSettings.Load();
            SyncFirstRun(settings);

            var service = new BrightnessService(settings);
            service.Start();

            using (var context = new TrayApplicationContext(service, startHidden || settings.StartMinimizedToTray))
            {
                Application.Run(context);
            }

            service.Dispose();
            if (_instanceMutex != null) _instanceMutex.Dispose();
        }

        private static void SyncFirstRun(AppSettings settings)
        {
            try
            {
                bool registered = AutoStart.IsEnabled();

                if (!settings.FirstRunDone)
                {
                    settings.FirstRunDone = true;
                    if (settings.AutoStart != registered)
                    {
                        string error;
                        AutoStart.SetEnabled(settings.AutoStart, out error);
                    }
                    settings.Save();
                    return;
                }

                if (settings.AutoStart != registered)
                {
                    string error;
                    AutoStart.SetEnabled(settings.AutoStart, out error);
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>截图模式默认用临时配置目录，避免碰到真实配置。</summary>
        private static void UseTemporaryConfigForScreenshots()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HDRBRIGHTNESS_CONFIG_DIR"))) return;

            string dir = Path.Combine(Path.GetTempPath(), "HdrBrightness-screenshot");
            Environment.SetEnvironmentVariable("HDRBRIGHTNESS_CONFIG_DIR", dir);
        }

        private static void RenderScreenshot(string path, string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DisplayConfigNative.AssertLayout();

            var settings = AppSettings.Load();
            var service = new BrightnessService(settings);
            service.Start();
            if (HasArgument(args, "--demo")) service.LoadDemoTargets();

            var form = new MainForm(service);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            if (HasArgument(args, "--full"))
            {
                int needed = form.GetPreferredHeight();
                form.ClientSize = new Size(form.ClientSize.Width,
                    Math.Min(1500, Math.Max(form.ClientSize.Height, needed)));
            }
            form.Show();
            Application.DoEvents();
            Thread.Sleep(400);
            Application.DoEvents();

            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(path, ImageFormat.Png);
            }

            try
            {
                System.IO.File.WriteAllText(path + ".txt", Diagnostics.Collect());
            }
            catch (Exception)
            {
            }

            form.AllowClose = true;
            form.Close();
            service.Dispose();
        }

        private static void RenderScheduleScreenshot(string path)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var settings = AppSettings.Load();
            var service = new BrightnessService(settings);
            service.Start();

            var rule = settings.Schedules.Count > 0 ? settings.Schedules[0] : new ScheduleRule();

            using (var form = new ScheduleEditForm(service, rule, settings.Schedules.Count == 0))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                Application.DoEvents();
                Thread.Sleep(300);
                Application.DoEvents();

                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(path, ImageFormat.Png);
                }

                form.Hide();
            }

            service.Dispose();
        }

        private static void RenderSettingsScreenshot(string path, string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var settings = AppSettings.Load();
            var service = new BrightnessService(settings);
            service.Start();

            using (var form = new SettingsForm(service))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                Application.DoEvents();
                Thread.Sleep(300);
                Application.DoEvents();

                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(path, ImageFormat.Png);
                }

                form.Hide();
            }

            service.Dispose();
        }
    }
}
