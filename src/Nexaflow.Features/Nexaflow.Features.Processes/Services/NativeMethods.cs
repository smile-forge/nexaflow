using System.Runtime.InteropServices;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// The handful of Win32 calls the live sampler needs that have no managed equivalent: a Toolhelp
/// process snapshot (the only cheap source of parent-PID), system-wide CPU times, the memory gauge,
/// GDI/USER object counts, and the 32/64-bit check. Every helper degrades to empty/zero/"" on failure.
/// </summary>
internal static class NativeMethods
{
    // ── Toolhelp process snapshot ──────────────────────────────────────────
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>(pid, parentPid, exeName) for every process via one Toolhelp snapshot. Empty on failure.</summary>
    public static List<(int Pid, int ParentPid, string Name)> SnapshotProcesses()
    {
        var list = new List<(int, int, string)>(400);
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return list;
            do
            {
                list.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile ?? ""));
            } while (Process32NextW(snap, ref entry));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    // ── System CPU times ────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    private static ulong Combine(FILETIME ft) => ((ulong)ft.High << 32) | ft.Low;

    /// <summary>Raw system idle/kernel/user tick totals (kernel already includes idle). (0,0,0) on failure.</summary>
    public static (ulong Idle, ulong Kernel, ulong User) ReadSystemTimes()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return (0, 0, 0);
        return (Combine(i), Combine(k), Combine(u));
    }

    // ── Memory gauge ────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>(loadPercent, totalBytes, availBytes). Zeros on failure.</summary>
    public static (double Load, long Total, long Avail) ReadMemoryStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
        return (m.dwMemoryLoad, (long)m.ullTotalPhys, (long)m.ullAvailPhys);
    }

    // ── GDI / USER object counts ──────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    public static (int Gdi, int User) ReadGuiResources(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return (0, 0);
        return ((int)GetGuiResources(handle, 0 /*GR_GDIOBJECTS*/),
                (int)GetGuiResources(handle, 1 /*GR_USEROBJECTS*/));
    }

    // ── 32/64-bit ─────────────────────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    /// <summary>"32-bit" if the process runs under WOW64, else "64-bit"; "" if it can't be determined.</summary>
    public static string GetArchitecture(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "";
        if (!Environment.Is64BitOperatingSystem) return "32-bit";
        return IsWow64Process(handle, out var wow) ? (wow ? "32-bit" : "64-bit") : "";
    }
}
