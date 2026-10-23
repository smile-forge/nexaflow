using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The app's one connection to the AI host. Nothing is started until the first request; the host then stays up for
/// the models it holds, and is replaced by a fresh one on the next request after it dies, stops answering its
/// heartbeat, or leaves a request silent past that request's stall limit. A host that keeps dying is not restarted
/// until it has stayed down a while, so a broken install fails fast instead of looping. Nothing here resumes on the
/// caller's context: the host is started, and its pipe read and pinged, on the thread pool, so a request made from the
/// UI thread never puts the host's traffic on it.
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
    private readonly CancellationTokenSource _life = new();
    private Task _retiring = Task.CompletedTask;

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
        var stream = await OpenStreamAsync(create, stallLimit, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            await foreach (var reply in stream.ReadAsync(ct).ConfigureAwait(false)) yield return reply;
    }

    /// <summary>
    /// Sends the request <paramref name="create"/> makes from a fresh id and keeps it open for input — see
    /// <see cref="IHostStream"/>. <paramref name="stallLimit"/> is as for <see cref="SendAsync"/>: a stream's handler
    /// replies while it waits for input, so a quiet stream is not taken for a hung one.
    /// </summary>
    /// <exception cref="AiHostException">No host could be started.</exception>
    public async Task<IHostStream> OpenStreamAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default)
    {
        while (true)
        {
            var connection = await ConnectAsync(ct).ConfigureAwait(false);
            // A host retiring between the two goes on closing; the request starts the next one.
            if (await connection.BeginAsync(create, ct).ConfigureAwait(false) is { } request) return new HostStream(request, stallLimit);
        }
    }

    /// <summary>The live connection, starting a host first when there is none — or when the one running was started
    /// with another environment, once it has finished what it was doing.</summary>
    private async Task<HostConnection> ConnectAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var environment = Environment;
        if (_connection is { IsServing: true } live && live.Environment == environment) return live;

        await _starting.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is { IsServing: true } started && started.Environment == environment) return started;
            if (_connection is { } previous)
            {
                await previous.WhenIdle.WaitAsync(ct).ConfigureAwait(false);
                await previous.DisposeAsync().ConfigureAwait(false);
            }
            _connection = null;

            int recent = RecentCrashes();
            if (recent >= _timings.CrashLimit)
                throw new AiHostException(AiHostFailure.Unavailable,
                    $"The AI host stopped unexpectedly {recent} times in {_timings.CrashWindow.TotalMinutes:0.#} minutes; it is not restarted until that eases.");

            // Started off the caller's thread: launching the process and waiting on its handshake is no work for a UI thread.
            _connection = await Task.Run(() => HostConnection.StartAsync(_launcher, environment, _timings, _time, OnEnded, ct), ct).ConfigureAwait(false);
            return _connection;
        }
        finally { _starting.Release(); }
    }

    /// <summary>
    /// Closes the host once it has served no request for <paramref name="idleFor"/> while <paramref name="holding"/> says
    /// nothing is held in it. A host with no model to keep still holds its runtimes — the CUDA context, cuBLAS, ONNX
    /// Runtime — and any model a tool loaded with its last use; the next request starts a fresh one.
    /// </summary>
    public void RetireWhenIdle(TimeSpan idleFor, Func<bool> holding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _retiring = Task.Run(() => RetireAsync(idleFor, holding));
    }

    private async Task RetireAsync(TimeSpan idleFor, Func<bool> holding)
    {
        using var timer = new PeriodicTimer(idleFor < RetireCheck ? idleFor : RetireCheck, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_life.Token).ConfigureAwait(false))
            {
                if (_connection is not { IsServing: true } live || holding() || !live.TryRetire(idleFor)) continue;
                await _starting.WaitAsync(_life.Token).ConfigureAwait(false);
                try
                {
                    if (_connection == live) _connection = null;
                }
                finally { _starting.Release(); }
                await live.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
    }

    /// <summary>How often an idle host is looked at for closing.</summary>
    private static readonly TimeSpan RetireCheck = TimeSpan.FromMinutes(1);

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
        _life.Cancel();
        await _retiring.ConfigureAwait(false);
        if (_connection is { } connection) await connection.DisposeAsync().ConfigureAwait(false);
        _starting.Dispose();
        _life.Dispose();
    }
}

public sealed class AiHostExitedEventArgs(int? exitCode, string reason) : EventArgs
{
    /// <summary>The process's exit code, when it exited by itself.</summary>
    public int? ExitCode { get; } = exitCode;

    public string Reason { get; } = reason;
}
