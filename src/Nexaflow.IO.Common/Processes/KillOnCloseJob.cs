using System.Runtime.InteropServices;

namespace Nexaflow.IO.Common.Processes;

/// <summary>
/// A process-wide Win32 job object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>. A child assigned to it the instant
/// it is created dies with this process — for any reason, including a crash, a debugger Stop, or a taskkill where no
/// managed cleanup runs — because the OS terminates every assigned process and its descendants as the last handle to
/// the job closes. Terminal shells and the AI host are assigned to it.
///
/// The handle is deliberately never closed during the process lifetime: closing it (process exit) is what triggers
/// the kill. Graceful teardown of each child stays its owner's job; this is the backstop for abnormal exit.
/// </summary>
public static class KillOnCloseJob
{
    private const uint LimitKillOnJobClose            = 0x00002000;
    private const int  ExtendedLimitInformationClass  = 9;

    private static readonly IntPtr _handle = Create();

    private static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = LimitKillOnJobClose;

        int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            SetInformationJobObject(job, ExtendedLimitInformationClass, ptr, (uint)len);
        }
        finally { Marshal.FreeHGlobal(ptr); }

        return job;
    }

    /// <summary>Assigns a freshly created (ideally still-suspended) process to the kill-on-close job; false when it
    /// could not be — the child then outlives an abnormal exit of this process.</summary>
    public static bool Assign(IntPtr processHandle)
        => _handle != IntPtr.Zero && processHandle != IntPtr.Zero && AssignProcessToJobObject(_handle, processHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS                        IoInfo;
        public nuint                              ProcessMemoryLimit;
        public nuint                              JobMemoryLimit;
        public nuint                              PeakProcessMemoryUsed;
        public nuint                              PeakJobMemoryUsed;
    }
}
