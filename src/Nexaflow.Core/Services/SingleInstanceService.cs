using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;


namespace Nexaflow.Core.Services;

/// <summary>
/// Enforces single-instance behaviour via a named mutex + named pipe.
/// The second instance forwards its launch to the first (<see cref="ForwardLaunch"/>), then exits.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Nexaflow_SingleInstance_v1";
    private const string PipeName  = "nexaflow_ipc_v1";

    /// <summary>The one request the pipe carries: a launch, then its workspace name and its path, each a line, empty when absent.</summary>
    private const string LaunchCommand = "launch";

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private Mutex? _mutex;
    private bool   _isOwner;

    /// <summary>Ends the listening: the session is ending.</summary>
    private readonly CancellationTokenSource _listening = new();

    /// <summary>
    /// Attempts to become the single owner instance.
    /// Returns <c>true</c> if this process is the first instance; <c>false</c> otherwise.
    /// When <c>false</c>, the caller should call <see cref="ForwardLaunch"/> then exit.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex   = new Mutex(initiallyOwned: true, MutexName, out _isOwner);
        return _isOwner;
    }

    /// <summary>
    /// Hands <paramref name="launch"/> to the running first instance. Called from the second-instance process before it
    /// exits. It waits for the listener rather than dropping the request when the first instance is still starting: opening
    /// several items from Explorer at once starts one process per item, and all but the first arrive while it initialises.
    /// It also lets the first instance bring its window forward, which Windows otherwise refuses a background process.
    /// </summary>
    public static void ForwardLaunch(LaunchRequest launch)
    {
        AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(timeout: 15_000);
            using var w = new StreamWriter(pipe) { AutoFlush = true };
            w.WriteLine(LaunchCommand);
            w.WriteLine(launch.WorkspaceName ?? "");   // optional Workspace name to open into
            w.WriteLine(launch.OpenPath ?? "");        // optional file or folder to open
        }
        catch { /* first instance never started listening — swallow */ }
    }

    /// <summary>Listens for later launches, handing each to <paramref name="onLaunch"/> on <paramref name="ui"/> until
    /// <see cref="Dispose"/>.</summary>
    public void StartListening(IUiThread ui, Action<LaunchRequest> onLaunch)
    {
        var stopping = _listening.Token;
        Task.Run(ListenLoop);

        async Task ListenLoop()
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(stopping);

                    using var reader = new StreamReader(server);
                    var cmd = await reader.ReadLineAsync(stopping);
                    if (cmd == LaunchCommand)
                    {
                        var workspaceName = await reader.ReadLineAsync(stopping);
                        var openPath      = await reader.ReadLineAsync(stopping);
                        var launch = new LaunchRequest(
                            string.IsNullOrEmpty(workspaceName) ? null : workspaceName,
                            string.IsNullOrEmpty(openPath) ? null : openPath);
                        ui.Run(() => onLaunch(launch));
                    }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
                catch { /* swallow pipe errors, keep listening */ }
            }
        }
    }

    /// <summary>Stops listening and lets the instance go, so a launch after this one owns it. Doing so again does nothing.</summary>
    public void Dispose()
    {
        if (!_listening.IsCancellationRequested) _listening.Cancel();
        if (_isOwner)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            _isOwner = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
