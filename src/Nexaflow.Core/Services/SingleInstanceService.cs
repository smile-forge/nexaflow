using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace Nexaflow.Core.Services;

/// <summary>
/// Enforces single-instance behaviour via a named mutex + named pipe.
/// The second instance signals the first to open a new window, then exits.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Nexaflow_SingleInstance_v1";
    private const string PipeName  = "nexaflow_ipc_v1";

    private Mutex? _mutex;
    private bool   _isOwner;

    /// <summary>
    /// Attempts to become the single owner instance.
    /// Returns <c>true</c> if this process is the first instance; <c>false</c> otherwise.
    /// When <c>false</c>, the caller should call <see cref="SignalNewWindow"/> then exit.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex   = new Mutex(initiallyOwned: true, MutexName, out _isOwner);
        return _isOwner;
    }

    /// <summary>
    /// Sends a "new-window" command to the running first instance.
    /// Called from the second-instance process before it exits.
    /// </summary>
    public static void SignalNewWindow()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(timeout: 2_000);
            using var w = new StreamWriter(pipe) { AutoFlush = true };
            w.WriteLine("new-window");
        }
        catch { /* first instance may not be listening yet — swallow */ }
    }

    /// <summary>
    /// Starts a background pipe server. Each connection that sends "new-window"
    /// invokes <paramref name="onNewWindow"/> on the UI dispatcher.
    /// Must be called after the Application is running.
    /// </summary>
    public void StartListening(Action onNewWindow)
    {
        Task.Run(ListenLoop);

        async Task ListenLoop()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server);
                    var cmd = await reader.ReadLineAsync();
                    if (cmd == "new-window")
                        Application.Current.Dispatcher.Invoke(onNewWindow);
                }
                catch { /* swallow pipe errors, keep listening */ }
            }
        }
    }

    public void Dispose()
    {
        if (_isOwner)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
    }
}
