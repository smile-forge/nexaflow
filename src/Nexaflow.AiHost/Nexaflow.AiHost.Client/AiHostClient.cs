using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The app's one connection to the AI host. Nothing is started until the first request; the host then stays up for
/// the models it holds, and is replaced by a fresh one on the next request after it dies, stops answering its
/// heartbeat, or leaves a request silent past that request's stall limit. A host that keeps dying is not restarted
/// until it has stayed down a while, so a broken install fails fast instead of looping.
/// </summary>
public sealed class AiHostClient : IAiHost, IAsyncDisposable
{
    private readonly IAiHostLauncher _launcher;
    private readonly AiHostTimings _timings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly Queue<DateTimeOffset> _crashes = new();
    private HostConnection? _connection;
    private AiHostEnvironment _environment = AiHostEnvironment.Cpu;
    private bool _disposed;

    public AiHostClient(IAiHostLauncher launcher, AiHostTimings? timings = null, TimeProvider? time = null)
    {
        _launcher = launcher;
        _timings  = timings ?? AiHostTimings.Default;
        _time     = time ?? TimeProvider.System;
    }

    /// <summary>A host exited without being asked to — crashed, killed by a watchdog, or ended by its own error.
    /// Raised on a pool thread.</summary>
    public event EventHandler<AiHostExitedEventArgs>? HostExited;

    public bool IsRunning => _connection is { IsAlive: true };

    /// <summary>What the next host is started with. A host already running with something else serves out the
    /// requests it has, and the next request starts its replacement.</summary>
    public AiHostEnvironment Environment
    {
        get => Volatile.Read(ref _environment);
        set => Volatile.Write(ref _environment, value);
    }

    /// <summary>
    /// Sends the request <paramref name="create"/> makes from a fresh id and yields its replies until it ends.
    /// <paramref name="stallLimit"/> is the longest the request may go without a reply; past it the host is taken for
    /// hung and replaced. Cancelling <paramref name="ct"/>, or leaving the enumeration early, stops the request in the
    /// host too.
    /// </summary>
    /// <exception cref="AiHostException">The request failed, the host was lost, or none could be started.</exception>
    public async IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit,
                                                       [EnumeratorCancellation] CancellationToken ct = default)
    {
        var connection = await ConnectAsync(ct);
        await using var request = await connection.BeginAsync(create, ct);

        while (true)
        {
            var reply = await request.NextAsync(stallLimit, ct);
            switch (reply)
            {
                case Completed:
                    yield break;
                case Failed failed:
                    throw new AiHostException(AiHostFailure.Request, failed.Message, failed.Kind);
                default:
                    yield return reply;
                    break;
            }
        }
    }

    /// <summary>The live connection, starting a host first when there is none — or when the one running was started
    /// with another environment, once it has finished what it was doing.</summary>
    private async Task<HostConnection> ConnectAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var environment = Environment;
        if (_connection is { IsAlive: true } live && live.Environment == environment) return live;

        await _starting.WaitAsync(ct);
        try
        {
            if (_connection is { IsAlive: true } started && started.Environment == environment) return started;
            if (_connection is { } previous)
            {
                await previous.WhenIdle.WaitAsync(ct);
                await previous.DisposeAsync();
            }
            _connection = null;

            int recent = RecentCrashes();
            if (recent >= _timings.CrashLimit)
                throw new AiHostException(AiHostFailure.Unavailable,
                    $"The AI host stopped unexpectedly {recent} times in {_timings.CrashWindow.TotalMinutes:0.#} minutes; it is not restarted until that eases.");

            _connection = await HostConnection.StartAsync(_launcher, environment, _timings, _time, OnEnded, ct);
            return _connection;
        }
        finally { _starting.Release(); }
    }

    private void OnEnded(object? sender, AiHostExitedEventArgs e)
    {
        if (_disposed) return;
        lock (_crashes) _crashes.Enqueue(_time.GetUtcNow());
        HostExited?.Invoke(this, e);
    }

    private int RecentCrashes()
    {
        lock (_crashes)
        {
            while (_crashes.TryPeek(out var at) && _time.GetUtcNow() - at > _timings.CrashWindow) _crashes.Dequeue();
            return _crashes.Count;
        }
    }

    /// <summary>Closes the pipe, which ends the host; it is killed if it has not gone a few seconds later.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection is { } connection) await connection.DisposeAsync();
        _starting.Dispose();
    }
}

public sealed class AiHostExitedEventArgs(int? exitCode, string reason) : EventArgs
{
    /// <summary>The process's exit code, when it exited by itself.</summary>
    public int? ExitCode { get; } = exitCode;

    public string Reason { get; } = reason;
}
