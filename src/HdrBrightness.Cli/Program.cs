using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HdrBrightness
{
    /// <summary>
    /// 命令行工具，方便脚本化调用和排查问题：
    ///   hdrbright list
    ///   hdrbright set all 120          （单位 nit）
    ///   hdrbright set 1 raw:1500       （直接用 API 原始值）
    ///   hdrbright get 1
    /// </summary>
    internal static class CliProgram
    {
        private static int Main(string[] args)
        {
            try
            {
                DisplayConfigNative.AssertLayout();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 3;
            }

            if (args.Length == 0)
            {
                PrintUsage();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "list":
                case "ls":
                    return List();
                case "get":
                    return Get(args);
                case "set":
                    return Set(args);
                case "debug":
                    return DebugDump();
                case "help":
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine("未知命令：" + args[0]);
                    PrintUsage();
                    return 2;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("HDR 内容亮度命令行工具（对应 Windows 显示设置里的 SDR 内容亮度）");
            Console.WriteLine();
            Console.WriteLine("  hdrbright list                 列出所有活动显示输出及当前数值");
            Console.WriteLine("  hdrbright get <目标>           读取指定显示器的数值");
            Console.WriteLine("  hdrbright set <目标> <百分比>  设置亮度百分比（0-100）");
            Console.WriteLine("  hdrbright debug                打印底层调用过程，用于排查问题");
            Console.WriteLine();
            Console.WriteLine("  <目标>：list 里的序号、\"all\"，或者显示器名称的一部分");
            Console.WriteLine("  <百分比>：0-100，0 最暗、100 最亮（与 Windows 设置里的滑块一致）");
            Console.WriteLine("            用 raw:1500 可直接指定 API 原始值（1000-6000，对应 0%-100%）");
        }

        private static List<DisplayTarget> Load()
        {
            string error;
            var targets = DisplayService.Enumerate(out error);
            if (!string.IsNullOrEmpty(error)) Console.Error.WriteLine(error);
            return targets;
        }

        private static int DebugDump()
        {
            Console.WriteLine("desktop = " + DisplayConfigNative.GetDesktopName());
            uint pathCount = 0;
            uint modeCount = 0;
            int err = DisplayConfigNative.GetDisplayConfigBufferSizes(
                DisplayConfigNative.QdcOnlyActivePaths, out pathCount, out modeCount);
            Console.WriteLine("GetDisplayConfigBufferSizes -> " + err + " paths=" + pathCount + " modes=" + modeCount);
            if (err != 0) return 1;

            var paths = new DisplayConfigNative.PathInfo[pathCount];
            var modes = new DisplayConfigNative.ModeInfo[modeCount];
            err = DisplayConfigNative.QueryDisplayConfig(
                DisplayConfigNative.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            Console.WriteLine("QueryDisplayConfig -> " + err + " paths=" + pathCount);
            if (err != 0) return 1;

            for (uint i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                Console.WriteLine("path " + i
                    + " targetAdapter=" + path.TargetInfo.AdapterId
                    + " targetId=" + path.TargetInfo.Id
                    + " targetAvailable=" + path.TargetInfo.TargetAvailable
                    + " sourceAdapter=" + path.SourceInfo.AdapterId
                    + " sourceId=" + path.SourceInfo.Id);

                var namePacket = new DisplayConfigNative.TargetDeviceName();
                namePacket.Header = DisplayConfigNative.CreateHeader(
                    DisplayConfigNative.DeviceInfoType.GetTargetName,
                    path.TargetInfo.AdapterId, path.TargetInfo.Id,
                    Marshal.SizeOf(typeof(DisplayConfigNative.TargetDeviceName)));
                int targetErr = DisplayConfigNative.GetDeviceInfoTargetName(ref namePacket);
                Console.WriteLine("  GetTargetName -> " + targetErr
                    + " friendly='" + namePacket.MonitorFriendlyDeviceName + "'"
                    + " devicePath='" + namePacket.MonitorDevicePath + "'");

                var sourcePacket = new DisplayConfigNative.SourceDeviceName();
                sourcePacket.Header = DisplayConfigNative.CreateHeader(
                    DisplayConfigNative.DeviceInfoType.GetSourceName,
                    path.SourceInfo.AdapterId, path.SourceInfo.Id,
                    Marshal.SizeOf(typeof(DisplayConfigNative.SourceDeviceName)));
                int sourceErr = DisplayConfigNative.GetDeviceInfoSourceName(ref sourcePacket);
                Console.WriteLine("  GetSourceName -> " + sourceErr + " gdi='" + sourcePacket.ViewGdiDeviceName + "'");

                var colorPacket = new DisplayConfigNative.AdvancedColorInfo();
                colorPacket.Header = DisplayConfigNative.CreateHeader(
                    DisplayConfigNative.DeviceInfoType.GetAdvancedColorInfo,
                    path.TargetInfo.AdapterId, path.TargetInfo.Id,
                    Marshal.SizeOf(typeof(DisplayConfigNative.AdvancedColorInfo)));
                int colorErr = DisplayConfigNative.GetDeviceInfoAdvancedColor(ref colorPacket);
                Console.WriteLine("  GetAdvancedColorInfo -> " + colorErr
                    + " value=0x" + colorPacket.Value.ToString("X8")
                    + " supported=" + colorPacket.AdvancedColorSupported
                    + " enabled=" + colorPacket.AdvancedColorEnabled
                    + " bpc=" + colorPacket.BitsPerColorChannel);

                var whitePacket = new DisplayConfigNative.SdrWhiteLevel();
                whitePacket.Header = DisplayConfigNative.CreateHeader(
                    DisplayConfigNative.DeviceInfoType.GetSdrWhiteLevel,
                    path.TargetInfo.AdapterId, path.TargetInfo.Id,
                    Marshal.SizeOf(typeof(DisplayConfigNative.SdrWhiteLevel)));
                int whiteErr = DisplayConfigNative.GetDeviceInfoSdrWhiteLevel(ref whitePacket);
                Console.WriteLine("  GetSdrWhiteLevel -> " + whiteErr
                    + " whiteLevel=" + whitePacket.WhiteLevel
                    + " (" + DisplayService.FormatPercent(whitePacket.WhiteLevel) + ")");

                DumpRawCalls(path);
            }
            return 0;
        }

        /// <summary>用原始字节缓冲区再调一遍，排除结构体封送带来的差异。</summary>
        private static void DumpRawCalls(DisplayConfigNative.PathInfo path)
        {
            // GET_SOURCE_NAME：84 字节
            var sourceBuffer = new byte[84];
            IntPtr sourcePtr = Marshal.AllocHGlobal(sourceBuffer.Length);
            try
            {
                // 注意：真实 ABI 的字段顺序是 type, size, adapterId, id
                Marshal.WriteInt32(sourcePtr, 0, (int)DisplayConfigNative.DeviceInfoType.GetSourceName);
                Marshal.WriteInt32(sourcePtr, 4, 84);
                Marshal.WriteInt32(sourcePtr, 8, (int)path.SourceInfo.AdapterId.LowPart);
                Marshal.WriteInt32(sourcePtr, 12, path.SourceInfo.AdapterId.HighPart);
                Marshal.WriteInt32(sourcePtr, 16, (int)path.SourceInfo.Id);
                int err = DisplayConfigNative.GetDeviceInfoRaw(sourcePtr);
                Console.WriteLine("  [raw] GetSourceName -> " + err
                    + " gdi='" + Marshal.PtrToStringUni(sourcePtr + 20, 32) + "'");
            }
            finally
            {
                Marshal.FreeHGlobal(sourcePtr);
            }

            // GET_SDR_WHITE_LEVEL：24 字节
            var whiteBuffer = new byte[24];
            IntPtr whitePtr = Marshal.AllocHGlobal(whiteBuffer.Length);
            try
            {
                Marshal.WriteInt32(whitePtr, 0, (int)DisplayConfigNative.DeviceInfoType.GetSdrWhiteLevel);
                Marshal.WriteInt32(whitePtr, 4, 24);
                Marshal.WriteInt32(whitePtr, 8, (int)path.TargetInfo.AdapterId.LowPart);
                Marshal.WriteInt32(whitePtr, 12, path.TargetInfo.AdapterId.HighPart);
                Marshal.WriteInt32(whitePtr, 16, (int)path.TargetInfo.Id);
                int err = DisplayConfigNative.GetDeviceInfoRaw(whitePtr);
                uint value = (uint)Marshal.ReadInt32(whitePtr, 20);
                Console.WriteLine("  [raw] GetSdrWhiteLevel -> " + err + " whiteLevel=" + value);
            }
            finally
            {
                Marshal.FreeHGlobal(whitePtr);
            }
        }

        private static int List()
        {
            var targets = Load();
            if (targets.Count == 0) return 1;

            Console.WriteLine("{0,-3} {1,-28} {2,-14} {3,-10} {4,-8} {5}", "序号", "显示器", "接口", "HDR", "亮度", "原始值");
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                string hdr = !target.HdrSupported ? "不支持" : (target.HdrEnabled ? "已开启" : "未开启");
                string percent = target.WhiteLevelKnown ? DisplayService.FormatPercent(target.WhiteLevel) : "-";
                string raw = target.WhiteLevelKnown
                    ? target.WhiteLevel.ToString(CultureInfo.InvariantCulture)
                    : "-";
                Console.WriteLine("{0,-3} {1,-28} {2,-14} {3,-10} {4,-8} {5}",
                    (i + 1).ToString(CultureInfo.InvariantCulture), Trim(target.DisplayName, 26), target.GdiName, hdr, percent, raw);
            }
            return 0;
        }

        private static int Get(string[] args)
        {
            if (args.Length < 2) { PrintUsage(); return 2; }

            var targets = Load();
            var selection = Select(targets, args[1]);
            if (selection.Count == 0) { Console.Error.WriteLine("没有匹配的显示器：" + args[1]); return 1; }

            foreach (var target in selection)
            {
                uint current;
                int error;
                if (!DisplayService.TryGetWhiteLevel(target, out current, out error))
                {
                    Console.Error.WriteLine(target.DisplayName + "：读取失败，" + DisplayService.DescribeError(error));
                    continue;
                }
                Console.WriteLine("{0}: {1}（原始值 {2}）",
                    target.DisplayName, DisplayService.FormatPercent(current), current);
            }
            return 0;
        }

        private static int Set(string[] args)
        {
            if (args.Length < 3) { PrintUsage(); return 2; }

            var targets = Load();
            var selection = Select(targets, args[1]);
            if (selection.Count == 0) { Console.Error.WriteLine("没有匹配的显示器：" + args[1]); return 1; }

            uint whiteLevel;
            if (!TryParseValue(args[2], out whiteLevel))
            {
                Console.Error.WriteLine("无法解析数值：" + args[2]);
                return 2;
            }

            int failures = 0;
            foreach (var target in selection)
            {
                int code = DisplayService.SetWhiteLevel(target, whiteLevel);
                if (code != DisplayConfigNative.ErrorSuccess)
                {
                    failures++;
                    Console.Error.WriteLine(target.DisplayName + "：设置失败，" + DisplayService.DescribeError(code));
                }
                else
                {
                    Console.WriteLine("{0}: 已设为 {1}（原始值 {2}）",
                        target.DisplayName, DisplayService.FormatPercent(whiteLevel), whiteLevel);
                }
            }
            return failures == 0 ? 0 : 1;
        }

        private static bool TryParseValue(string text, out uint whiteLevel)
        {
            whiteLevel = 0;
            if (text.StartsWith("raw:", StringComparison.OrdinalIgnoreCase))
            {
                uint raw;
                if (!uint.TryParse(text.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw)) return false;
                whiteLevel = raw;
                return true;
            }

            int percent;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent)) return false;
            whiteLevel = DisplayService.PercentToWhiteLevel(percent);
            return true;
        }

        private static List<DisplayTarget> Select(List<DisplayTarget> targets, string selector)
        {
            var result = new List<DisplayTarget>();
            if (selector.Equals("all", StringComparison.OrdinalIgnoreCase) || selector == "*")
            {
                result.AddRange(targets);
                return result;
            }

            int index;
            if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                if (index >= 1 && index <= targets.Count) result.Add(targets[index - 1]);
                return result;
            }

            foreach (var target in targets)
            {
                if (target.DisplayName.IndexOf(selector, StringComparison.OrdinalIgnoreCase) >= 0
                    || target.GdiName.IndexOf(selector, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(target);
                }
            }
            return result;
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }
}
