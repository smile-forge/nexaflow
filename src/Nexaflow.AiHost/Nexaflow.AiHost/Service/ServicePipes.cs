using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// The service's pipe, as its supervisor opens it (<see cref="For"/>): any signed-in account may connect to read and write — not
/// to open an instance of its own, so nothing else can answer on it — no client across the network may, and the first instance is
/// refused where the name is taken already. Each client is taken on an instance the supervisor makes and connects, so the process a
/// client finds answering is the service's own (<c>GetNamedPipeServerProcessId</c>); the connected instance is then handed to the
/// worker (<see cref="PipeHandover"/>), which reads and writes it. Who the client is, the supervisor reads as it takes it on: only
/// the process that connected an instance can act as its client. An instance is never bound to the supervisor's own completion
/// port, which would leave the worker unable to bind it to its.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ServicePipes(string pipeName, RawSecurityDescriptor security)
{
    /// <summary>What a client may do on the pipe: read and write, and read its attributes and security — never open an instance.</summary>
    public const PipeAccessRights ClientRights = PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize;

    private const int Buffer = SharedBytes.InlineLimit;
    private const uint AccessDuplex = 0x3, FlagOverlapped = 0x40000000, FlagFirstPipeInstance = 0x00080000;
    private const uint RejectRemoteClients = 0x8, UnlimitedInstances = 255;
    private const int ErrorPipeConnected = 535, ErrorIoPending = 997;

    private bool _first = true;

    /// <summary>Who may open the service's pipe: SYSTEM and <paramref name="service"/> in full, any signed-in account as a client
    /// (<see cref="ClientRights"/>), and no one across the network.</summary>
    public static RawSecurityDescriptor For(SecurityIdentifier service)
        => new($"D:P(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;{service.Value})(A;;0x{(int)ClientRights:x};;;AU)");

    /// <summary>The next client, connected, and who it is.</summary>
    /// <exception cref="Win32Exception">The pipe could not be opened — on the first instance, its name is taken.</exception>
    /// <exception cref="IOException">A client went before it was taken, or did not let its account be read.</exception>
    public async Task<TakenClient> AcceptAsync(CancellationToken ct)
    {
        var pipe = Create(_first);
        _first = false;
        try
        {
            await ConnectAsync(pipe, ct).ConfigureAwait(false);
            return new TakenClient(pipe, AccountOf(pipe));
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// <summary>The account that opened <paramref name="pipe"/>, as the OS says: read acting as the pipe's client, which a client lets
    /// the service do by connecting at identification level — enough to read who it is, never to act as it.</summary>
    /// <exception cref="IOException">The client did not let its account be read.</exception>
    private static SecurityIdentifier AccountOf(SafePipeHandle pipe)
    {
        if (!ImpersonateNamedPipeClient(pipe))
            throw new IOException($"A client did not let its account be read (error {Marshal.GetLastPInvokeError()}); it was let go.");
        try
        {
            return WindowsIdentity.GetCurrent(TokenAccessLevels.Query).User!;
        }
        finally
        {
            // Never go on as the client: a thread that cannot stop acting as it ends the service.
            if (!RevertToSelf()) Environment.FailFast("The AI host service could not stop acting as a client.");
        }
    }

    private SafePipeHandle Create(bool first)
    {
        byte[] descriptor = new byte[security.BinaryLength];
        security.GetBinaryForm(descriptor, 0);
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pinned.AddrOfPinnedObject() };
            var pipe = CreateNamedPipe($@"\\.\pipe\{pipeName}", AccessDuplex | FlagOverlapped | (first ? FlagFirstPipeInstance : 0),
                                       RejectRemoteClients, UnlimitedInstances, Buffer, Buffer, 0, ref attributes);
            if (pipe.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"The pipe {pipeName} could not be opened");
            return pipe;
        }
        finally { pinned.Free(); }
    }

    /// <summary>Waits for a client on <paramref name="pipe"/>, signalled by an event rather than a completion port.</summary>
    private static async Task ConnectAsync(SafePipeHandle pipe, CancellationToken ct)
    {
        using var connected = new ManualResetEvent(false);
        IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        try
        {
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = connected.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            if (ConnectNamedPipe(pipe, overlapped)) return;
            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorPipeConnected) return;   // the client came between the instance being made and waited on
            if (error != ErrorIoPending) throw new IOException($"A client could not be taken on the service's pipe (error {error}).");
            try
            {
                await SignalledAsync(connected, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancelIoEx(pipe, overlapped);
                GetOverlappedResult(pipe, overlapped, out _, wait: true);   // the overlapped is the system's until the connect has ended
                throw;
            }
            if (!GetOverlappedResult(pipe, overlapped, out _, wait: false))
                throw new IOException($"A client went before it was taken on the service's pipe (error {Marshal.GetLastPInvokeError()}).");
        }
        finally { Marshal.FreeHGlobal(overlapped); }
    }

    private static async Task SignalledAsync(WaitHandle handle, CancellationToken ct)
    {
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => signalled.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            using (ct.Register(() => signalled.TrySetCanceled(ct))) await signalled.Task.ConfigureAwait(false);
        }
        finally { wait.Unregister(null); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int Inherit;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize,
                                                         uint inBufferSize, uint defaultTimeout, ref SecurityAttributes attributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConnectNamedPipe(SafePipeHandle pipe, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafePipeHandle file, IntPtr overlapped, out uint transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafePipeHandle file, IntPtr overlapped);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImpersonateNamedPipeClient(SafePipeHandle pipe);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RevertToSelf();
}

/// <summary>A client the service's supervisor took on its pipe: the connected instance, and who the client is.</summary>
internal sealed record TakenClient(SafePipeHandle Pipe, SecurityIdentifier Account) : IDisposable
{
    public void Dispose() => Pipe.Dispose();
}
