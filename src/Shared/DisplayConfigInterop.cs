using System;
using System.Runtime.InteropServices;

namespace HdrBrightness
{
    /// <summary>
    /// Win32 DISPLAYCONFIG_* 结构体与入口点。
    /// 用来读写 HDR 模式下“SDR 内容亮度”（SDR white level），也就是
    /// Windows 设置 → 系统 → 屏幕 → HDR 里的那个滑块。
    /// </summary>
    internal static class DisplayConfigNative
    {
        internal const uint QdcAllPaths = 0x00000001;
        internal const uint QdcOnlyActivePaths = 0x00000002;

        internal const int ErrorSuccess = 0;
        internal const int ErrorInsufficientBuffer = 122;

        /// <summary>
        /// 设置 SDR 白点用的设备信息类型。微软没有公开这个值（官方枚举里只有 GET 的 11），
        /// 但 Windows 设置里的“SDR 内容亮度”滑块用的就是它：0xFFFFFFEE。
        /// </summary>
        internal const int SetSdrWhiteLevelType = unchecked((int)0xFFFFFFEE);

        /// <summary>老版本上社区一直在用的写入类型，作为回退。</summary>
        internal const int LegacySetSdrWhiteLevelType = 12;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Luid
        {
            public uint LowPart;
            public int HighPart;

            public bool Equals(Luid other)
            {
                return LowPart == other.LowPart && HighPart == other.HighPart;
            }

            public override string ToString()
            {
                return LowPart.ToString("X8") + ":" + HighPart.ToString("X8");
            }
        }

        internal enum DeviceInfoType
        {
            GetSourceName = 1,
            GetTargetName = 2,
            GetTargetPreferredMode = 3,
            GetAdapterName = 4,
            GetAdvancedColorInfo = 9,
            SetAdvancedColorState = 10,
            GetSdrWhiteLevel = 11,
            SetSdrWhiteLevel = 12,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceInfoHeader
        {
            // 注意字段顺序：真实 ABI 是 type, size, adapterId, id（不是文档里常被抄错的
            // type, adapterId, id, size）。顺序错了会拿到没有意义的垃圾值，而且因为两者都是
            // 20 字节，光看结构体尺寸检查是发现不了的。
            public int Type;      // DeviceInfoType
            public uint Size;
            public Luid AdapterId;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rational
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Region2D
        {
            public uint Cx;
            public uint Cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PathSourceInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PathTargetInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIdx;
            public int OutputTechnology;
            public int Rotation;
            public int Scaling;
            public Rational RefreshRate;
            public int ScanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PathInfo
        {
            public PathSourceInfo SourceInfo;
            public PathTargetInfo TargetInfo;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VideoSignalInfo
        {
            public ulong PixelRate;
            public Rational HSyncFreq;
            public Rational VSyncFreq;
            public Region2D ActiveSize;
            public Region2D TotalSize;
            public uint VideoStandard;
            public uint ScanLineOrdering;   // 1 bit 位域，按 32 位字段存放
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TargetMode
        {
            public VideoSignalInfo TargetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SourceMode
        {
            public uint Width;
            public uint Height;
            public int PositionX;
            public int PositionY;
            public int PixelFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DesktopImageInfo
        {
            public Region2D PathSourceSize;
            public Rect32 DesktopImageRegion;
            public Rect32 DesktopImageClip;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ModeInfo
        {
            public int InfoType;
            public uint Id;
            public Luid AdapterId;
            public TargetMode TargetMode;   // union，取最大成员
        }

        /// <summary>
        /// DISPLAYCONFIG_TARGET_DEVICE_NAME。新版 Windows 在 connectorInstance 之后有 4 字节，
        /// 整个结构是 420 字节（老版本是 416），详情见 DisplayService 里的兼容处理。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TargetDeviceName
        {
            public DeviceInfoHeader Header;
            public int OutputTechnology;
            public ushort EdidManufactureId;
            public ushort EdidProductCodeId;
            public uint ConnectorInstance;
            public uint Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
        }

        /// <summary>老版本 Windows 上的 416 字节版本（没有那 4 字节）。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TargetDeviceNameLegacy
        {
            public DeviceInfoHeader Header;
            public int OutputTechnology;
            public ushort EdidManufactureId;
            public ushort EdidProductCodeId;
            public uint ConnectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SourceDeviceName
        {
            public DeviceInfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AdvancedColorInfo
        {
            public DeviceInfoHeader Header;
            public uint Value;              // 位域：advancedColorSupported / Enabled / wideColorEnforced ...
            public int ColorEncoding;
            public uint BitsPerColorChannel;

            public bool AdvancedColorSupported { get { return (Value & 0x1) != 0; } }
            public bool AdvancedColorEnabled { get { return (Value & 0x2) != 0; } }
            public bool WideColorEnforced { get { return (Value & 0x4) != 0; } }
            public bool AdvancedColorForceDisabled { get { return (Value & 0x8) != 0; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SdrWhiteLevel
        {
            public DeviceInfoHeader Header;
            public uint WhiteLevel;
        }

        /// <summary>
        /// SET_SDR_WHITE_LEVEL 的请求结构：比读取版多一个 finalValue 字节（1 = 这是最终值）。
        /// 因为按 4 字节对齐补齐，整个结构是 28 字节，size 必须填 28 才会被接受。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SetSdrWhiteLevel
        {
            public DeviceInfoHeader Header;
            public uint WhiteLevel;
            public byte FinalValue;
        }

        /// <summary>老版本的回退结构（type = 12，24 字节）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SetSdrWhiteLevelLegacy
        {
            public DeviceInfoHeader Header;
            public uint WhiteLevel;
        }

        [DllImport("user32.dll")]
        internal static extern int GetDisplayConfigBufferSizes(
            uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        internal static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] PathInfo[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] ModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoTargetName(ref TargetDeviceName packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoTargetNameLegacy(ref TargetDeviceNameLegacy packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoSourceName(ref SourceDeviceName packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoAdvancedColor(ref AdvancedColorInfo packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoSdrWhiteLevel(ref SdrWhiteLevel packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetDeviceInfoRaw(IntPtr packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        internal static extern int SetDeviceInfoSdrWhiteLevel(ref SdrWhiteLevel packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        internal static extern int SetDeviceInfoSdrWhiteLevelEx(ref SetSdrWhiteLevel packet);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        internal static extern int SetDeviceInfoSdrWhiteLevelLegacy(ref SetSdrWhiteLevelLegacy packet);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] internal static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")] internal static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetUserObjectInformation(
            IntPtr handle, int index, System.Text.StringBuilder buffer, int length, out int needed);

        /// <summary>返回 "窗口站\桌面"，用来判断进程是否运行在真实桌面上。</summary>
        internal static string GetDesktopName()
        {
            return ObjectName(GetProcessWindowStation()) + "\\" + ObjectName(GetThreadDesktop(GetCurrentThreadId()));
        }

        private static string ObjectName(IntPtr handle)
        {
            try
            {
                var buffer = new System.Text.StringBuilder(256);
                int needed;
                if (GetUserObjectInformation(handle, 2, buffer, 512, out needed)) return buffer.ToString();
            }
            catch (Exception)
            {
            }
            return "<unknown>";
        }

        internal static DeviceInfoHeader CreateHeader(DeviceInfoType type, Luid adapterId, uint id, int structSize)
        {
            return CreateHeader((int)type, adapterId, id, structSize);
        }

        internal static DeviceInfoHeader CreateHeader(int type, Luid adapterId, uint id, int structSize)
        {
            DeviceInfoHeader header;
            header.Type = type;
            header.Size = (uint)structSize;
            header.AdapterId = adapterId;
            header.Id = id;
            return header;
        }

        /// <summary>
        /// 校验结构体尺寸是否与 Win32 定义一致，尺寸不符会导致调用直接被拒。
        /// </summary>
        internal static void AssertLayout()
        {
            Check(typeof(Luid), 8);
            Check(typeof(DeviceInfoHeader), 20);
            Check(typeof(PathInfo), 72);
            Check(typeof(ModeInfo), 64);
            Check(typeof(TargetDeviceName), 420);
            Check(typeof(TargetDeviceNameLegacy), 416);
            Check(typeof(SourceDeviceName), 84);
            Check(typeof(AdvancedColorInfo), 32);
            Check(typeof(SdrWhiteLevel), 24);
            Check(typeof(SetSdrWhiteLevel), 28);
            Check(typeof(SetSdrWhiteLevelLegacy), 24);
        }

        private static void Check(Type type, int expected)
        {
            int actual = Marshal.SizeOf(type);
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    "结构体 " + type.Name + " 尺寸错误：期望 " + expected + "，实际 " + actual);
            }
        }
    }
}
