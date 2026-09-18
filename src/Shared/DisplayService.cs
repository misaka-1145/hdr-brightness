using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HdrBrightness
{
    /// <summary>一个显示目标（显示器 + 输出接口的组合）。</summary>
    internal sealed class DisplayTarget
    {
        public DisplayConfigNative.Luid AdapterId;
        public uint TargetId;
        public string FriendlyName = string.Empty;
        public string DevicePath = string.Empty;
        public string GdiName = string.Empty;
        public bool HdrSupported;
        public bool HdrEnabled;
        /// <summary>HDR 状态是否读取成功（false 表示查询失败，而不是不支持）。</summary>
        public bool AdvancedColorKnown;
        public bool WhiteLevelKnown;
        public uint WhiteLevel;
        public int WhiteLevelError;
        public int AdvancedColorError;
        public string NameErrorText;
        public int OutputTechnology;

        /// <summary>稳定的硬件标识，用来在重启/换接口后仍然认得出同一台显示器。</summary>
        public string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(DevicePath)) return DevicePath;
                return FriendlyName + "|" + GdiName;
            }
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(FriendlyName) ? GdiName : FriendlyName; }
        }

        public int Percent
        {
            get { return DisplayService.WhiteLevelToPercent(WhiteLevel); }
        }
    }

    internal static class DisplayService
    {
        /// <summary>
        /// 这台 Windows 上实际可调的原始值范围（实测：低于下限或高于上限的写入都会被拒绝），
        /// 也正好对应 Windows 设置里那个滑块的 0% 和 100%。
        /// </summary>
        public const uint MinWhiteLevel = 1000;
        public const uint MaxWhiteLevel = 6000;

        /// <summary>1% 对应的原始值。</summary>
        public const double WhiteLevelPerPercent = (MaxWhiteLevel - MinWhiteLevel) / 100.0;

        public const uint DefaultWhiteLevel = MinWhiteLevel;

        public static uint PercentToWhiteLevel(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return MinWhiteLevel + (uint)Math.Round(percent * WhiteLevelPerPercent, MidpointRounding.AwayFromZero);
        }

        public static int WhiteLevelToPercent(uint whiteLevel)
        {
            if (whiteLevel <= MinWhiteLevel) return 0;
            if (whiteLevel >= MaxWhiteLevel) return 100;
            return (int)Math.Round((whiteLevel - MinWhiteLevel) / WhiteLevelPerPercent, MidpointRounding.AwayFromZero);
        }

        public static string FormatPercent(uint whiteLevel)
        {
            return WhiteLevelToPercent(whiteLevel).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>枚举当前处于活动状态的显示目标，并读取 HDR 状态与当前白点值。</summary>
        public static List<DisplayTarget> Enumerate(out string error)
        {
            error = null;
            var result = new List<DisplayTarget>();

            uint pathCount = 0;
            uint modeCount = 0;
            int err = 0;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                err = DisplayConfigNative.GetDisplayConfigBufferSizes(
                    DisplayConfigNative.QdcOnlyActivePaths, out pathCount, out modeCount);
                if (err != DisplayConfigNative.ErrorSuccess)
                {
                    error = "GetDisplayConfigBufferSizes 失败，错误码 " + err;
                    return result;
                }

                var paths = new DisplayConfigNative.PathInfo[pathCount];
                var modes = new DisplayConfigNative.ModeInfo[modeCount];
                err = DisplayConfigNative.QueryDisplayConfig(
                    DisplayConfigNative.QdcOnlyActivePaths,
                    ref pathCount, paths,
                    ref modeCount, modes,
                    IntPtr.Zero);

                if (err == DisplayConfigNative.ErrorInsufficientBuffer)
                {
                    continue;   // 显示器数量变了，重新取一次
                }
                if (err != DisplayConfigNative.ErrorSuccess)
                {
                    error = "QueryDisplayConfig 失败，错误码 " + err;
                    return result;
                }

                for (uint i = 0; i < pathCount; i++)
                {
                    result.Add(ReadTarget(paths[i], i + 1));
                }
                break;
            }

            if (result.Count == 0 && error == null)
            {
                error = "没有找到活动的显示目标。";
            }

            result.Sort(delegate (DisplayTarget a, DisplayTarget b)
            {
                int c = string.CompareOrdinal(a.GdiName, b.GdiName);
                if (c != 0) return c;
                return string.CompareOrdinal(a.FriendlyName, b.FriendlyName);
            });

            return result;
        }

        private static DisplayTarget ReadTarget(DisplayConfigNative.PathInfo path, uint index)
        {
            var target = new DisplayTarget();
            target.AdapterId = path.TargetInfo.AdapterId;
            target.TargetId = path.TargetInfo.Id;
            target.OutputTechnology = path.TargetInfo.OutputTechnology;

            var namePacket = new DisplayConfigNative.TargetDeviceName();
            namePacket.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.DeviceInfoType.GetTargetName,
                target.AdapterId, target.TargetId,
                Marshal.SizeOf(typeof(DisplayConfigNative.TargetDeviceName)));

            int nameError = DisplayConfigNative.GetDeviceInfoTargetName(ref namePacket);
            if (nameError == DisplayConfigNative.ErrorSuccess)
            {
                target.FriendlyName = (namePacket.MonitorFriendlyDeviceName ?? string.Empty).Trim();
                target.DevicePath = (namePacket.MonitorDevicePath ?? string.Empty).Trim();
                target.OutputTechnology = namePacket.OutputTechnology;
            }
            else
            {
                // 老版本 Windows 的结构体是 416 字节，换一个再试
                var legacyPacket = new DisplayConfigNative.TargetDeviceNameLegacy();
                legacyPacket.Header = DisplayConfigNative.CreateHeader(
                    DisplayConfigNative.DeviceInfoType.GetTargetName,
                    target.AdapterId, target.TargetId,
                    Marshal.SizeOf(typeof(DisplayConfigNative.TargetDeviceNameLegacy)));

                int legacyError = DisplayConfigNative.GetDeviceInfoTargetNameLegacy(ref legacyPacket);
                if (legacyError == DisplayConfigNative.ErrorSuccess)
                {
                    target.FriendlyName = (legacyPacket.MonitorFriendlyDeviceName ?? string.Empty).Trim();
                    target.DevicePath = (legacyPacket.MonitorDevicePath ?? string.Empty).Trim();
                    target.OutputTechnology = legacyPacket.OutputTechnology;
                }
                else
                {
                    // 名字读不到：仍然显示这个输出，方便用户至少能调它
                    target.NameErrorText = "读取显示器名称失败：" + DescribeError(legacyError);
                    target.FriendlyName = "显示输出 " + index
                        + "（" + OutputTechnologyName(path.TargetInfo.OutputTechnology) + "）";
                }
            }

            var sourcePacket = new DisplayConfigNative.SourceDeviceName();
            sourcePacket.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.DeviceInfoType.GetSourceName,
                path.SourceInfo.AdapterId, path.SourceInfo.Id,
                Marshal.SizeOf(typeof(DisplayConfigNative.SourceDeviceName)));

            if (DisplayConfigNative.GetDeviceInfoSourceName(ref sourcePacket) == DisplayConfigNative.ErrorSuccess)
            {
                target.GdiName = (sourcePacket.ViewGdiDeviceName ?? string.Empty).Trim();
            }

            uint bitsPerChannel;
            int colorError;
            target.AdvancedColorKnown = ReadAdvancedColor(target, out colorError, out bitsPerChannel);
            target.AdvancedColorError = colorError;

            uint whiteLevel;
            int whiteErr;
            if (TryGetWhiteLevel(target, out whiteLevel, out whiteErr))
            {
                target.WhiteLevel = whiteLevel;
                target.WhiteLevelKnown = true;
            }
            target.WhiteLevelError = whiteErr;

            return target;
        }

        public static string OutputTechnologyName(int outputTechnology)
        {
            switch (outputTechnology)
            {
                case 1: return "VGA";
                case 2: return "S-Video";
                case 3: return "复合视频";
                case 4: return "分量视频";
                case 5: return "DVI";
                case 6: return "HDMI";
                case 7: return "LVDS";
                case 8: return "D-Terminal";
                case 9: return "SDI";
                case 10: return "DisplayPort";
                case 11: return "内置 eDP";
                case 12: return "UDI 外接";
                case 13: return "UDI 内置";
                case 14: return "SDTV 转接";
                case 15: return "Miracast 无线";
                case 16: return "USB-C/间接显示";
                case 17: return "虚拟显示";
                case unchecked((int)0x80000000): return "内置屏";
                default: return "接口 " + outputTechnology;
            }
        }

        private static bool ReadAdvancedColor(DisplayTarget target, out int error, out uint bitsPerChannel)
        {
            var packet = new DisplayConfigNative.AdvancedColorInfo();
            packet.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.DeviceInfoType.GetAdvancedColorInfo,
                target.AdapterId, target.TargetId,
                Marshal.SizeOf(typeof(DisplayConfigNative.AdvancedColorInfo)));

            error = DisplayConfigNative.GetDeviceInfoAdvancedColor(ref packet);
            if (error != DisplayConfigNative.ErrorSuccess)
            {
                bitsPerChannel = 0;
                return false;
            }

            target.HdrSupported = packet.AdvancedColorSupported;
            target.HdrEnabled = packet.AdvancedColorEnabled;
            bitsPerChannel = packet.BitsPerColorChannel;
            return true;
        }

        /// <summary>读取某个目标当前的白点值。</summary>
        public static bool TryGetWhiteLevel(DisplayTarget target, out uint whiteLevel, out int error)
        {
            var packet = new DisplayConfigNative.SdrWhiteLevel();
            packet.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.DeviceInfoType.GetSdrWhiteLevel,
                target.AdapterId, target.TargetId,
                Marshal.SizeOf(typeof(DisplayConfigNative.SdrWhiteLevel)));

            error = DisplayConfigNative.GetDeviceInfoSdrWhiteLevel(ref packet);
            if (error != DisplayConfigNative.ErrorSuccess)
            {
                whiteLevel = 0;
                return false;
            }

            whiteLevel = packet.WhiteLevel;
            return true;
        }

        /// <summary>
        /// 设置白点值，返回 Win32 错误码（0 表示成功）。
        /// 先用当前系统实际支持的私有类型 0xFFFFFFEE（结构体多一个 finalValue 字节，size=28），
        /// 失败再回退到老系统上的 12 号类型。
        /// </summary>
        public static int SetWhiteLevel(DisplayTarget target, uint whiteLevel)
        {
            var packet = new DisplayConfigNative.SetSdrWhiteLevel();
            packet.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.SetSdrWhiteLevelType,
                target.AdapterId, target.TargetId,
                Marshal.SizeOf(typeof(DisplayConfigNative.SetSdrWhiteLevel)));
            packet.WhiteLevel = whiteLevel;
            packet.FinalValue = 1;

            int error = DisplayConfigNative.SetDeviceInfoSdrWhiteLevelEx(ref packet);
            if (error == DisplayConfigNative.ErrorSuccess)
            {
                return error;
            }

            var legacy = new DisplayConfigNative.SetSdrWhiteLevelLegacy();
            legacy.Header = DisplayConfigNative.CreateHeader(
                DisplayConfigNative.LegacySetSdrWhiteLevelType,
                target.AdapterId, target.TargetId,
                Marshal.SizeOf(typeof(DisplayConfigNative.SetSdrWhiteLevelLegacy)));
            legacy.WhiteLevel = whiteLevel;

            int legacyError = DisplayConfigNative.SetDeviceInfoSdrWhiteLevelLegacy(ref legacy);
            return legacyError == DisplayConfigNative.ErrorSuccess ? legacyError : error;
        }

        public static string DescribeError(int error)
        {
            if (error == DisplayConfigNative.ErrorSuccess) return "成功";
            try
            {
                return new System.ComponentModel.Win32Exception(error).Message + "（错误码 " + error + "）";
            }
            catch
            {
                return "错误码 " + error;
            }
        }
    }
}
