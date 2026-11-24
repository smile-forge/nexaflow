using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The AI host a machine runs as a service, for every app on it: where its pipe is, and which process — as Windows' service manager
/// says — runs it now. An app joins it where it runs, and talks only to that process on that pipe: something else that took the
/// pipe's name is never spoken to.
/// </summary>
public interface IAiHostService
{
    /// <summary>The name of the pipe the service opens for its clients.</summary>
    string PipeName { get; }

    /// <summary>The process running the service now; null where it is not installed, or not running.</summary>
    int? RunningProcessId();
}

/// <summary>The AI host service as Windows installs it: <see cref="AiHostProtocol.ServiceName"/>, on <see cref="AiHostProtocol.ServicePipeName"/>.</summary>
public sealed class AiHostService(string serviceName, string pipeName) : IAiHostService
{
    /// <summary>This machine's own.</summary>
    public static AiHostService Machine { get; } = new(AiHostProtocol.ServiceName, AiHostProtocol.ServicePipeName);

    public string PipeName => pipeName;

    public int? RunningProcessId()
    {
        var manager = OpenSCManager(null, null, ConnectAccess);
        if (manager == IntPtr.Zero) return null;
        try
        {
            var service = OpenService(manager, serviceName, QueryStatusAccess);
            if (service == IntPtr.Zero) return null;   // not installed
            try
            {
                if (!QueryServiceStatusEx(service, ProcessInfo, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _))
                    throw new Win32Exception();
                return status.CurrentState == Running && status.ProcessId != 0 ? (int)status.ProcessId : null;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    /// <summary>The process that opened the pipe <paramref name="pipe"/> is connected to, as Windows says.</summary>
    internal static int ServerProcessIdOf(NamedPipeClientStream pipe)
        => GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint id) ? (int)id : throw new Win32Exception();

    private const uint ConnectAccess = 0x0001;
    private const uint QueryStatusAccess = 0x0004;
    private const int ProcessInfo = 0;
    private const uint Running = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
