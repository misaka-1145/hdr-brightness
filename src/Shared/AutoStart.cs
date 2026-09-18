using System;
using System.Reflection;
using Microsoft.Win32;

namespace HdrBrightness
{
    /// <summary>开机自启：写当前用户的 Run 键，不需要管理员权限。</summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "HdrBrightness";

        public static string ExecutablePath
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                return assembly.Location;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    var value = key.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string GetRegisteredCommand()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return null;
                    return key.GetValue(ValueName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "无法打开注册表 Run 键。";
                        return false;
                    }

                    if (enabled)
                    {
                        string command = "\"" + ExecutablePath + "\" --tray";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
