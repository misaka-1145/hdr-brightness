# Dev helper: launch a program on the interactive "WinSta0\Default" desktop and
# capture its console output into a file.
#
# Why: when the calling process lives on a non-default desktop (sandbox/virtual
# desktop), display configuration queries such as DisplayConfigGetDeviceInfo fail
# with error 31 (ERROR_GEN_FAILURE). Running the tool on the real desktop is the
# only way to verify that behaviour. Keep this file ASCII-only.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$Arguments = '',
    [string]$OutFile = 'desktop-run.log',
    [int]$TimeoutMs = 30000
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath" }
$ExePath = (Resolve-Path $ExePath).Path
$OutFile = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutFile))

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DesktopLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa,
        uint disposition, uint flags, IntPtr template);

    public static int Run(string exe, string args, string outFile, uint timeoutMs)
    {
        IntPtr handle = CreateFile(outFile, 0x40000000, 3, IntPtr.Zero, 2, 0x80, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return -1;
        SetHandleInformation(handle, 1, 1);

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.lpDesktop = "WinSta0\\Default";
        si.dwFlags = 0x100;
        si.hStdOutput = handle;
        si.hStdError = handle;

        var pi = new PROCESS_INFORMATION();
        string cmd = "\"" + exe + "\"" + (string.IsNullOrEmpty(args) ? "" : " " + args);
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, null, ref si, out pi))
            return -Marshal.GetLastWin32Error();

        WaitForSingleObject(pi.hProcess, timeoutMs);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseHandle(handle);
        return 0;
    }
}
'@

$result = [DesktopLauncher]::Run($ExePath, $Arguments, $OutFile, [uint32]$TimeoutMs)
if ($result -ne 0) { throw "CreateProcess failed: $result" }

Write-Host "Output written to $OutFile"
Get-Content $OutFile
