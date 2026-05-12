using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.Console;

internal static class NativeMethods
{
    internal const uint EXTENDED_STARTUPINFO_PRESENT       = 0x00080000;
    internal const uint INFINITE                           = 0xFFFFFFFF;
    internal const int  PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

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
