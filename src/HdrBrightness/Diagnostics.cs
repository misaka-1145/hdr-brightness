using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace HdrBrightness
{
    /// <summary>把底层调用过程收集成文本，用于排查“读不到 SDR 白点值”之类的问题。</summary>
    internal static class Diagnostics
    {
        public static string Collect()
        {
            var sb = new StringBuilder();
            sb.AppendLine("desktop = " + DisplayConfigNative.GetDesktopName());
            sb.AppendLine("config = " + AppSettings.ConfigPath);

            var loaded = AppSettings.Load();
            sb.AppendLine("groups = " + loaded.Groups.Count + " desired = " + loaded.Desired.Count
                + (AppSettings.LastLoadError == null ? "" : "  loadError=" + AppSettings.LastLoadError));
            foreach (var group in loaded.Groups)
            {
                sb.AppendLine("  group '" + group.Name + "' mode=" + group.Mode
                    + " members=" + group.Monitors.Count);
                foreach (var key in group.Monitors)
                {
                    sb.AppendLine("    member " + key);
                }
            }

            uint pathCount = 0;
            uint modeCount = 0;
            int err = DisplayConfigNative.GetDisplayConfigBufferSizes(
                DisplayConfigNative.QdcOnlyActivePaths, out pathCount, out modeCount);
            sb.AppendLine("GetDisplayConfigBufferSizes -> " + err + " paths=" + pathCount + " modes=" + modeCount);

            if (err == 0)
            {
                var paths = new DisplayConfigNative.PathInfo[pathCount];
                var modes = new DisplayConfigNative.ModeInfo[modeCount];
                int queryErr = DisplayConfigNative.QueryDisplayConfig(
                    DisplayConfigNative.QdcOnlyActivePaths,
                    ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                sb.AppendLine("QueryDisplayConfig -> " + queryErr + " paths=" + pathCount);

                for (uint i = 0; err == 0 && i < pathCount; i++)
                {
                    var path = paths[i];
                    sb.AppendLine("path " + i
                        + " targetAdapter=" + path.TargetInfo.AdapterId
                        + " targetId=" + path.TargetInfo.Id
                        + " sourceId=" + path.SourceInfo.Id);
                    sb.AppendLine("  raw whiteLevel -> " + RawWhiteLevelProbe(path));
                }
            }

            string enumerateError;
            List<DisplayTarget> targets = DisplayService.Enumerate(out enumerateError);
            sb.AppendLine("DisplayService.Enumerate -> " + (enumerateError ?? "ok") + " count=" + targets.Count);

            foreach (var target in targets)
            {
                sb.AppendLine("  " + target.DisplayName + " gdi=" + target.GdiName
                    + " hdrSupported=" + target.HdrSupported + " hdrEnabled=" + target.HdrEnabled
                    + " whiteLevelKnown=" + target.WhiteLevelKnown + " whiteLevel=" + target.WhiteLevel);
                sb.AppendLine("    key=" + target.Key);
            }

            return sb.ToString();
        }

        /// <summary>用原始缓冲区直接调一次 GET_SDR_WHITE_LEVEL，绕过结构体封送。</summary>
        private static string RawWhiteLevelProbe(DisplayConfigNative.PathInfo path)
        {
            IntPtr buffer = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.Copy(new byte[24], 0, buffer, 24);
                // 真实 ABI：type, size, adapterId, id
                Marshal.WriteInt32(buffer, 0, (int)DisplayConfigNative.DeviceInfoType.GetSdrWhiteLevel);
                Marshal.WriteInt32(buffer, 4, 24);
                Marshal.WriteInt32(buffer, 8, (int)path.TargetInfo.AdapterId.LowPart);
                Marshal.WriteInt32(buffer, 12, path.TargetInfo.AdapterId.HighPart);
                Marshal.WriteInt32(buffer, 16, (int)path.TargetInfo.Id);
                int err = DisplayConfigNative.GetDeviceInfoRaw(buffer);
                uint value = err == 0 ? (uint)Marshal.ReadInt32(buffer, 20) : 0;
                return "err=" + err + " value=" + value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
