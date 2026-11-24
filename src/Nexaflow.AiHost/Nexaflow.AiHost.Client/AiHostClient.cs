using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// The app's one connection to the AI host. Nothing is started until the first request. Where the machine's AI host service is
/// installed — the host every app on the machine shares — the request joins it and nothing else: a service stopped, unreachable, or of
/// another protocol is no host, saying why. Where it is not installed, the request starts a host of the app's own. That host then stays up for the models it holds, and is replaced by a fresh one on the next request
/// after it dies, stops answering its heartbeat, or leaves a request silent past that request's stall limit. A host that keeps
/// dying is not restarted until it has stayed down a while, so a broken install fails fast instead of looping. The service is
/// never killed: a request it leaves silent fails alone, and a missed heartbeat lets its connection go, for the next request to
/// join again. Nothing here resumes on the
/// caller's context: the host is started, and its pipe read and pinged, on the thread pool, so a request made from the
/// UI thread never puts the host's traffic on it.
/// </summary>
public sealed class AiHostClient : IAiHost, IAsyncDisposable
{
    private readonly IAiHostLauncher _launcher;
    private readonly IAiHostService? _service;
    private readonly string _app;
    private readonly AiHostTimings _timings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly Queue<DateTimeOffset> _crashes = new();
    private HostConnection? _connection;
    private AiHostEnvironment _environment = AiHostEnvironment.Cpu;
    private bool _disposed;
    private readonly CancellationTokenSource _life = new();
    private Task _retiring = Task.CompletedTask;

    /// <param name="app">The app's name for itself, which the host keeps this app's conversations apart by — its process's name
    /// where it gives none.</param>
    /// <param name="service">The machine's AI host service, joined rather than starting a host where it runs; none to always start one.</param>
    public AiHostClient(IAiHostLauncher launcher, AiHostTimings? timings = null, TimeProvider? time = null, string? app = null,
                        IAiHostService? service = null)
    {
        _launcher = launcher;
        _service  = service;
        _app      = app ?? System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        _timings  = timings ?? AiHostTimings.Default;
        _time     = time ?? TimeProvider.System;
        if (service is not null) _watching = Task.Run(() => WatchServiceAsync(service));
    }

    /// <summary>A host this app started exited without being asked to — crashed, killed by a watchdog, or ended by its own error.
    /// Raised on a pool thread; the service, whose life is not this app's, raises none.</summary>
    public event EventHandler<AiHostExitedEventArgs>? HostExited;

    /// <summary>Files arrived in the host's model store, verified, or left it, whichever app asked: their paths in the store. Raised on
    /// a pool thread.</summary>
    public event EventHandler<IReadOnlyList<string>>? ModelFilesChanged;

    /// <summary>Whether a host is running for this app now: what one that is not keeps — held models, kept conversations — is gone with it.
    /// The service this app last joined is, while it runs, though its connection has gone: it keeps this app's conversations by the
    /// app's name, not by its connection.</summary>
    public bool IsRunning => _connection is { IsAlive: true } || _joinedLast && _service?.RunningProcessId() is not null;

    /// <summary>Whether the last host this app had was the service.</summary>
    private bool _joinedLast;

    /// <summary>What the next host this app starts is started with. A host already running with something else serves out the
    /// requests it has, and the next request starts its replacement; the service runs as it was set up, whatever this says.</summary>
    public AiHostEnvironment Environment
    {
        get => Volatile.Read(ref _environment);
        set => Volatile.Write(ref _environment, value);
    }

    /// <summary>The share of this machine's memory the models on its host may hold: the service's, where this app last joined it, as its
    /// administrator set it; else what a host this app starts will find, before it has started.</summary>
    public HostMemory Memory => ServiceMemory ?? _launcher.Memory;

    /// <summary>The share of this machine's memory the machine's service holds its models on the CPU to, where this app last joined it and
    /// it said; null where this app runs a host of its own. Set by the service's administrator, not by this app.</summary>
    public HostMemory? ServiceMemory => _joinedLast ? Volatile.Read(ref _serviceMemory) : null;

    private HostMemory? _serviceMemory;

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

    /// <summary>
    /// Bytes to fill and send in the next request: an array when small; else a shared section the host made for them, so a large
    /// picture is written once, in place, and never copied through the pipe. Dispose them once the request has been sent — or if they
    /// never are, which frees the section.
    /// </summary>
    /// <exception cref="AiHostException">No host could be started, or it refused the section.</exception>
    public async Task<SharedBytes> AllocateAsync(int length, CancellationToken ct = default)
    {
        if (length <= SharedBytes.InlineLimit) return SharedBytes.Of(new byte[length]);
        var connection = await ConnectAsync(ct).ConfigureAwait(false);
        return await connection.AllocateAsync(length, ct).ConfigureAwait(false);
    }

    /// <summary>The live connection; else the service, joined, where it is installed; else a host started first — and in place of one this
    /// app started with another environment, once it has finished what it was doing.</summary>
    private async Task<HostConnection> ConnectAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var environment = Environment;
        if (_connection is { IsServing: true } live && Serves(live, environment)) return live;

        await _starting.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is { IsServing: true } started && Serves(started, environment)) return started;
            if (_connection is { } previous)
            {
                // One still serving finishes what it is doing first; one lost has nothing to finish — a request left open on it, as a hold
                // is until its owner lets it go, would otherwise be waited on for ever.
                if (previous.IsAlive) await previous.WhenIdle.WaitAsync(ct).ConfigureAwait(false);
                await previous.DisposeAsync().ConfigureAwait(false);
            }
            _connection = null;

            // Off the caller's thread: joining, or launching a process and waiting on its handshake, is no work for a UI thread. Where the
            // service is installed it is the only host: one that cannot be joined is no host, saying why, and none is started in its place.
            if (_service is { } service && service.IsInstalled())
            {
                HostConnection joined;
                try
                {
                    joined = await Task.Run(() => HostConnection.JoinAsync(service, _app, _timings, _time, OnEnded, ct), ct).ConfigureAwait(false);
                }
                catch (AiHostException ex)
                {
                    Reach(ex.Message);
                    throw;
                }
                Reach(null);
                _joinedLast = true;
                if (joined.Memory is { } served) Volatile.Write(ref _serviceMemory, served);
                return Keep(joined);
            }

            int recent = RecentCrashes();
            if (recent >= _timings.CrashLimit)
                throw new AiHostException(AiHostFailure.Unavailable,
                    $"The AI host stopped unexpectedly {recent} times in {_timings.CrashWindow.TotalMinutes:0.#} minutes; it is not restarted until that eases.");

            var own = await Task.Run(() => HostConnection.StartAsync(_launcher, environment, _app, _timings, _time, OnEnded, ct), ct).ConfigureAwait(false);
            _joinedLast = false;
            return Keep(own);
        }
        finally { _starting.Release(); }
    }

    /// <summary>
    /// Why this app's models cannot run now: the machine's AI host service, which is installed and so the only host, is stopped, does not
    /// answer, is not what answers, speaks another protocol, or turned this app away. Null where they can — the service joined, or none
    /// installed and this app starting a host of its own. Kept current without a request: an app says its models cannot run before the
    /// user asks one to.
    /// </summary>
    public string? Unreachable => Volatile.Read(ref _unreachable);

    /// <summary>Raised when <see cref="Unreachable"/> changes. Raised on a pool thread.</summary>
    public event EventHandler? ReachabilityChanged;

    private string? _unreachable;
    private Task _watching = Task.CompletedTask;

    private void Reach(string? why)
    {
        if (string.Equals(Interlocked.Exchange(ref _unreachable, why), why, StringComparison.Ordinal)) return;
        ReachabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Looks at the service every <see cref="AiHostTimings.ServiceCheck"/>: stopped, its models cannot run, said at once; running
    /// again while they cannot, it is joined — a connection the app keeps until it retires for standing idle — and they can, or it says why
    /// not; uninstalled, this app starts a host of its own.</summary>
    private async Task WatchServiceAsync(IAiHostService service)
    {
        using var timer = new PeriodicTimer(_timings.ServiceCheck, _time);
        try
        {
            do
            {
                if (!service.IsInstalled()) Reach(null);
                else if (service.RunningProcessId() is null) Reach(HostConnection.ServiceNotRunning);
                else if (Unreachable is not null && _connection is not { IsAlive: true })
                {
                    try { await ConnectAsync(_life.Token).ConfigureAwait(false); }
                    catch (AiHostException) { }   // said, in Unreachable
                }
            }
            while (await timer.WaitForNextTickAsync(_life.Token).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && _life.IsCancellationRequested) { }
    }

    /// <summary>Whether <paramref name="connection"/> serves requests made for <paramref name="environment"/>: the service serves every
    /// one, as it was set up; a host this app started, the one it was started with.</summary>
    private static bool Serves(HostConnection connection, AiHostEnvironment environment)
        => connection.Shared || connection.Environment == environment;

    private HostConnection Keep(HostConnection connection)
    {
        _connection = connection;
        connection.FilesChanged += (_, paths) => ModelFilesChanged?.Invoke(this, paths);
        return connection;
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
        // A connection to the service that ended is no crash of this app's host: the service goes on, and the next request joins it.
        if (_disposed || sender is HostConnection { Shared: true }) return;
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

    /// <summary>Closes the pipe, which ends a host this app started — killed if it has not gone a few seconds later — and lets go of
    /// the service.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _life.Cancel();
    await _retiring.ConfigureAwait(false);
        await _watching.ConfigureAwait(false);
        if (_connection is { } connection) await connection.DisposeAsync().ConfigureAwait(false);
        _starting.Dispose();
        _life.Dispose();
    }
}

/// <summary>How a host ended without being asked to.</summary>
public sealed class AiHostExitedEventArgs(int processId, int? exitCode, string reason, IReadOnlyList<string> lastErrors) : EventArgs
{
    /// <summary>The host's process — the id its own log is named for.</summary>
    public int ProcessId { get; } = processId;

    /// <summary>The process's exit code, when it exited by itself.</summary>
    public int? ExitCode { get; } = exitCode;

    /// <summary>Why it ended, in words: its exit, the code named (<see cref="AiHostExitCode"/>), or the watchdog or the
    /// pipe that ended it.</summary>
    public string Reason { get; } = reason;

    /// <summary>The last lines the host wrote to its error output, oldest first.</summary>
    public IReadOnlyList<string> LastErrors { get; } = lastErrors;
}
