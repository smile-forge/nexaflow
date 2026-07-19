using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Terminal;

internal static class NativeMethods
{
    internal const uint EXTENDED_STARTUPINFO_PRESENT        = 0x00080000;
    internal const uint CREATE_SUSPENDED                    = 0x00000004;
    internal const uint CREATE_UNICODE_ENVIRONMENT          = 0x00000400;
    internal const uint INFINITE                            = 0xFFFFFFFF;
    internal const uint WAIT_OBJECT_0                       = 0x00000000;
    internal const int  PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    // Process enumeration / termination — used by the foreground-tree hard-stop
    internal const uint TH32CS_SNAPPROCESS                  = 0x00000002;
    internal const uint PROCESS_TERMINATE                   = 0x00000001;
    internal static readonly IntPtr INVALID_HANDLE_VALUE    = new(-1);

    // Job-object limits (winnt.h)
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE  = 0x00002000;
    internal const int  JobObjectExtendedLimitInformation   = 9;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        COORD  size,
        IntPtr hInput,
        IntPtr hOutput,
        uint   dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcess(
        string?              lpApplicationName,
        string               lpCommandLine,
        IntPtr               lpProcessAttributes,
        IntPtr               lpThreadAttributes,
        bool                 bInheritHandles,
        uint                 dwCreationFlags,
        IntPtr               lpEnvironment,
        string?              lpCurrentDirectory,
        ref STARTUPINFOEX    lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out SafeFileHandle     hReadPipe,
        out SafeFileHandle     hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        uint                   nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr  lpAttributeList,
        int     dwAttributeCount,
        int     dwFlags,
        ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint   dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,  // HPCON handle value passed directly — NOT ref
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(IntPtr hThread);

    // ── Job objects — used to guarantee child shells die with this process ──

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    // ── Process tree enumeration / termination (foreground hard-stop) ───────

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);
}

[StructLayout(LayoutKind.Sequential)]
internal struct COORD
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public uint   dwProcessId;
    public uint   dwThreadId;
}

// PROCESSENTRY32W (CharSet.Unicode → Process32FirstW/NextW). dwSize must be set before the first call.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct PROCESSENTRY32
{
    public uint   dwSize;
    public uint   cntUsage;
    public uint   th32ProcessID;
    public IntPtr th32DefaultHeapID;
    public uint   th32ModuleID;
    public uint   cntThreads;
    public uint   th32ParentProcessID;
    public int    pcPriClassBase;
    public uint   dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szExeFile;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SECURITY_ATTRIBUTES
{
    public int    nLength;
    public IntPtr lpSecurityDescriptor;
    public int    bInheritHandle;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct STARTUPINFO
{
    public int    cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public uint   dwX;
    public uint   dwY;
    public uint   dwXSize;
    public uint   dwYSize;
    public uint   dwXCountChars;
    public uint   dwYCountChars;
    public uint   dwFillAttribute;
    public uint   dwFlags;
    public short  wShowWindow;
    public short  cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct STARTUPINFOEX
{
    public STARTUPINFO StartupInfo;
    public IntPtr      lpAttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long  PerProcessUserTimeLimit;
    public long  PerJobUserTimeLimit;
    public uint  LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint  ActiveProcessLimit;
    public nuint Affinity;
    public uint  PriorityClass;
    public uint  SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS                        IoInfo;
    public nuint                              ProcessMemoryLimit;
    public nuint                              JobMemoryLimit;
    public nuint                              PeakProcessMemoryUsed;
    public nuint                              PeakJobMemoryUsed;
}
