# Dev helper: minimal DisplayConfig probe. ASCII-only.
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'probe-result.txt'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Probe
{
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint f, out uint p, out uint m);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint f, ref uint p, IntPtr paths, ref uint m, IntPtr modes, IntPtr t);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetDeviceInfo(IntPtr packet);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

    static IntPtr Make(uint type, int low, int high, uint id, uint size)
    {
        IntPtr buf = Marshal.AllocHGlobal((int)size + 256);
        for (int i = 0; i < (int)size + 256; i++) Marshal.WriteByte(buf, i, 0);
        Marshal.WriteInt32(buf, 0, (int)type);
        Marshal.WriteInt32(buf, 4, low);
        Marshal.WriteInt32(buf, 8, high);
        Marshal.WriteInt32(buf, 12, (int)id);
        Marshal.WriteInt32(buf, 16, (int)size);
        return buf;
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("bitness=" + (IntPtr.Size * 8));
        IntPtr user32 = GetModuleHandle("user32.dll");
        sb.AppendLine("user32=0x" + user32.ToInt64().ToString("X") + " GetProcAddress(DisplayConfigGetDeviceInfo)=0x" + GetProcAddress(user32, "DisplayConfigGetDeviceInfo").ToInt64().ToString("X"));

        sb.AppendLine("GetDeviceInfo(NULL) -> " + GetDeviceInfo(IntPtr.Zero));

        IntPtr zero = Make(0, 0, 0, 0, 0);
        sb.AppendLine("GetDeviceInfo(zeroed header) -> " + GetDeviceInfo(zero));
        Marshal.FreeHGlobal(zero);

        uint pc = 0, mc = 0;
        int e = GetDisplayConfigBufferSizes(2, out pc, out mc);
        sb.AppendLine("bufSizes err=" + e + " paths=" + pc);
        IntPtr paths = Marshal.AllocHGlobal((int)pc * 72);
        IntPtr modes = Marshal.AllocHGlobal((int)mc * 64);
        QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);

        int low = Marshal.ReadInt32(paths, 20);
        int high = Marshal.ReadInt32(paths, 24);
        uint tid = (uint)Marshal.ReadInt32(paths, 28);
        sb.AppendLine("active target luid=" + low.ToString("X") + ":" + high + " id=" + tid);

        uint[] sizes = new uint[] { 24u, 416u };
        uint[] types = new uint[] { 2u, 11u };
        foreach (uint type in types)
        {
            foreach (uint size in sizes)
            {
                IntPtr buf = Make(type, low, high, tid, size);
                int err = GetDeviceInfo(buf);
                sb.AppendLine("type=" + type + " size=" + size + " -> " + err);
                Marshal.FreeHGlobal(buf);
            }
        }

        // same request, but with the adapter id zeroed: is the id even validated?
        IntPtr idZero = Make(11, 0, 0, 0, 24);
        sb.AppendLine("type=11 size=24 luid=0:0 id=0 -> " + GetDeviceInfo(idZero));
        Marshal.FreeHGlobal(idZero);

        Marshal.FreeHGlobal(paths);
        Marshal.FreeHGlobal(modes);
        return sb.ToString();
    }
}
'@

$text = [Probe]::Run()
[System.IO.File]::WriteAllText($out, $text, [System.Text.Encoding]::UTF8)
Write-Host $text
