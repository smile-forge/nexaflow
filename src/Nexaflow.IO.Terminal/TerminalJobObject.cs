using System.Runtime.InteropServices;

namespace Nexaflow.IO.Terminal;

/// <summary>
/// A process-wide Win32 job object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>. Every ConPTY child
/// shell is assigned to it the instant it is created, so when this process dies — for ANY reason,
/// including a crash, a debugger Stop, or a taskkill where no managed cleanup runs — the OS terminates
/// every assigned child and its descendant tree as the last handle to the job closes.
///
/// This is the definitive backstop against orphaned <c>cmd</c>/<c>conhost</c> processes accumulating
/// across debug/build/test runs. The graceful per-host terminate in <see cref="PseudoConsoleHostService"/>
/// handles normal teardown promptly; this handles abnormal exit. The job handle is deliberately never
/// closed during the process lifetime — closing it (i.e. process exit) is precisely what triggers the kill.
/// </summary>
internal static class TerminalJobObject
{
    private static readonly IntPtr _handle = Create();

    private static IntPtr Create()
    {
        var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            NativeMethods.SetInformationJobObject(
                job, NativeMethods.JobObjectExtendedLimitInformation, ptr, (uint)len);
        }
        finally { Marshal.FreeHGlobal(ptr); }

        return job;
    }

    /// <summary>Assigns a freshly created (ideally still-suspended) process to the kill-on-close job.</summary>
    public static void Assign(IntPtr processHandle)
    {
        if (_handle != IntPtr.Zero && processHandle != IntPtr.Zero)
            NativeMethods.AssignProcessToJobObject(_handle, processHandle);
    }
}
