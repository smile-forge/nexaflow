using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// One running host and its pipe: the handshake, the loop that routes replies to their requests, and the heartbeat. The host is
/// one this app started for itself (<see cref="StartAsync"/>), killed when a watchdog finds it hung, or the machine's service that
/// other apps share too (<see cref="JoinAsync"/>, <see cref="Shared"/>), which this app never kills: a request that stalls there is
/// cancelled and fails alone, and a missed heartbeat lets the connection go, for the next request to join again.
/// When anything ends it — the process exiting, the pipe breaking, a watchdog — every open request fails as
/// <see cref="AiHostFailure.Lost"/> and <see cref="Ended"/> is raised once, unless the app ended it itself.
/// </summary>
internal sealed class HostConnection : IAsyncDisposable
{
    /// <summary>The host's process, where this app started it; null for the service it joined.</summary>
    private readonly IAiHostProcess? _process;
    private readonly int _processId;
    private readonly HostChannel _channel;

    private readonly ClientSections _sections;
    private readonly AiHostTimings _timings;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _life = new();
    private readonly ConcurrentDictionary<long, Channel<HostReply>> _open = new();
    private long _nextId;
    private long _lastPong;
    private int _ended;
    private string? _killReason;
    private bool _closing;
    private Task _reading = Task.CompletedTask;
    private Task _beating = Task.CompletedTask;
    private readonly object _idleGate = new();
    private int _inFlight;
    private TaskCompletionSource _idle = Idle();
    /// <summary>When the last request ended — or the host started, before any.</summary>
    private long _idleSince;
    /// <summary>Being closed for having stood idle: no request may begin on it.</summary>
    private bool _retiring;

    private Task? _closed;

    private HostConnection(IAiHostProcess? process, int processId, AiHostEnvironment environment, HostChannel channel, ClientSections sections,
                           AiHostTimings timings, TimeProvider time)
    {
        _process     = process;
        _processId   = processId;
        Environment  = environment;
        _channel     = channel;
        _sections    = sections;
        _timings     = timings;
        _time        = time;
        _lastPong    = time.GetTimestamp();
        _idleSince   = _lastPong;
    }

    /// <summary>Joined to the machine's service, which other apps share: never killed by this app, only let go.</summary>
    public bool Shared => _process is null;

    public AiHostEnvironment Environment { get; }

    /// <summary>Completes when no request is in flight.</summary>
    public Task WhenIdle
    {
        get { lock (_idleGate) return _idle.Task; }
    }

    private static TaskCompletionSource Idle()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        idle.SetResult();
        return idle;
    }

    /// <summary>Counts a request in, unless the connection is retiring.</summary>
    private bool TryOpen()
    {
        lock (_idleGate)
        {
            if (_retiring) return false;
            if (_inFlight++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    private void Closed()
    {
        lock (_idleGate)
            if (--_inFlight == 0)
            {
                _idleSince = _time.GetTimestamp();
                _idle.TrySetResult();
            }
    }

    public event EventHandler<AiHostExitedEventArgs>? Ended;

    public bool IsAlive => Volatile.Read(ref _ended) == 0;

    /// <summary>Alive and not being closed for having stood idle: a request may begin on it.</summary>
    public bool IsServing
    {
        get { lock (_idleGate) return IsAlive && !_retiring; }
    }

    /// <summary>Marks the connection for closing when no request is in flight and none has been for
    /// <paramref name="idleFor"/>; from then on <see cref="BeginAsync"/> refuses, and its caller starts another host.</summary>
    public bool TryRetire(TimeSpan idleFor)
    {
        lock (_idleGate)
        {
            if (_retiring || _inFlight > 0 || _time.GetElapsedTime(_idleSince) < idleFor) return false;
            _retiring = true;
            return true;
        }
    }

    /// <summary>Starts a host on a pipe of its own under a name only this call knows, connects to it once it has opened the pipe, and
    /// says hello with the token it was started with: the host welcomes this app, the process it is the one started.</summary>
    /// <param name="app">The app's name for itself, which the host keeps its conversations apart by.</param>
    /// <param name="onEnded">Attached before anything can end the connection: a host that dies straight after the
    /// handshake must still be counted.</param>
    public static async Task<HostConnection> StartAsync(IAiHostLauncher launcher, AiHostEnvironment environment, string app, AiHostTimings timings,
                                                        TimeProvider time, EventHandler<AiHostExitedEventArgs> onEnded, CancellationToken ct)
    {
        string pipeName = "nexaflow-aihost-" + Guid.NewGuid().ToString("N");
        string token    = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        // The host reads which account this app runs as from the pipe itself: the app lets it identify that account, and no more.
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

        IAiHostProcess? process = null;
        try
        {
            process = launcher.Start(pipeName, token, environment);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AiHostProtocol.ConnectTimeout);

            // The host opens its pipe once its runtime is up: connecting waits for it, or for the host to have exited instead.
            var connected = pipe.ConnectAsync(timeout.Token);
            if (await Task.WhenAny(connected, process.Exited).ConfigureAwait(false) != connected)
            {
                timeout.Cancel();
                try { await connected.ConfigureAwait(false); } catch (OperationCanceledException) { }
                int code = await process.Exited.ConfigureAwait(false);
                throw new AiHostException(AiHostFailure.Unavailable, $"The AI host exited before it took this app, with {DescribeExit(code, process.LastErrors)}.");
            }
            await connected.ConfigureAwait(false);

            HostChannel? opened = null;
            var sections = new ClientSections(message => opened!.SendAsync(message));
            var channel = opened = new HostChannel(pipe, sections);
            await channel.SendAsync(new Hello(token, AiHostProtocol.Version, app), timeout.Token).ConfigureAwait(false);
            switch (await channel.ReceiveAsync(timeout.Token).ConfigureAwait(false))
            {
                case Welcome welcome when welcome.ProcessId == process.Id:
                    break;
                case HelloRefused refused:
                    await channel.DisposeAsync().ConfigureAwait(false);
                    throw new AiHostException(AiHostFailure.Unavailable, $"The AI host turned this app away: {refused.Reason}.");
                case null:
                    await channel.DisposeAsync().ConfigureAwait(false);
                    throw new AiHostException(AiHostFailure.Unavailable, "The AI host closed its pipe without welcoming this app.");
                default:
                    await channel.DisposeAsync().ConfigureAwait(false);
                    throw new AiHostException(AiHostFailure.Unavailable, "What answered on the AI host's pipe is not the host this app started.");
            }

            var connection = new HostConnection(process, process.Id, environment, channel, sections, timings, time);
            connection.Ended += onEnded;
            // The loops live as long as the host: on the thread pool, never on whatever context started it.
            connection._reading = Task.Run(connection.ReadAsync);
            connection._beating = Task.Run(connection.BeatAsync);
            _ = Task.Run(connection.WatchExitAsync);
            return connection;
        }
        catch (Exception ex)
        {
            process?.Kill();
            process?.Dispose();
            await pipe.DisposeAsync().ConfigureAwait(false);
            if (ex is AiHostException || ct.IsCancellationRequested) throw;
            throw new AiHostException(AiHostFailure.Unavailable, $"The AI host could not be reached: {ex.Message}");
        }
    }

    /// <summary>How long joining the service may take to find its pipe answering, once the service manager says it runs.</summary>
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long the service, once connected, may take to welcome this app: a client is served by the service's worker, which
    /// may be starting — after a crash, or to take up a new runtime.</summary>
    private static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Joins the machine's AI host service, where it runs: connects to its pipe, checks the process that answers is the service's own —
    /// so nothing that took the pipe's name is ever spoken to — and says hello as <paramref name="app"/>. Null where the service is not
    /// running, does not answer, is not what answers, or speaks another protocol: the caller starts a host of its own instead.
    /// </summary>
    /// <exception cref="AiHostException">The service turned this app away for something other than its protocol.</exception>
    public static async Task<HostConnection?> JoinAsync(IAiHostService service, string app, AiHostTimings timings, TimeProvider time,
                                                        EventHandler<AiHostExitedEventArgs> onEnded, CancellationToken ct)
    {
        if (service.RunningProcessId() is not { } processId) return null;
        // The host reads which account this app runs as from the pipe itself: the app lets it identify that account, and no more. It asks
        // to read and write, and no more: the service's pipe grants a client nothing else.
        var pipe = new NamedPipeClientStream(".", service.PipeName, PipeAccessRights.ReadWrite, PipeOptions.Asynchronous,
                                             TokenImpersonationLevel.Identification, HandleInheritability.None);
        HostChannel? channel = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(JoinTimeout);
            try { await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }   // running, yet not answering
            timeout.CancelAfter(WelcomeTimeout);
            if (AiHostService.ServerProcessIdOf(pipe) != processId) return null;

            var sections = new ClientSections(message => channel!.SendAsync(message));
            channel = new HostChannel(pipe, sections);
            await channel.SendAsync(new Hello(null, AiHostProtocol.Version, app), timeout.Token).ConfigureAwait(false);
            switch (await channel.ReceiveAsync(timeout.Token).ConfigureAwait(false))
            {
                case Welcome welcome when welcome.ProcessId == processId:
                    var connection = new HostConnection(null, processId, AiHostEnvironment.Cpu, channel, sections, timings, time);
                    connection.Ended += onEnded;
                    connection._reading = Task.Run(connection.ReadAsync);
                    connection._beating = Task.Run(connection.BeatAsync);
                    channel = null;
                    return connection;
                case HelloRefused { Protocol: not null }:
                    return null;   // a service of another build: this app's own host speaks its protocol
                case HelloRefused refused:
                    throw new AiHostException(AiHostFailure.Unavailable, $"The AI host service turned this app away: {refused.Reason}.");
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.ComponentModel.Win32Exception or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (channel is not null) await channel.DisposeAsync().ConfigureAwait(false);
            else if (pipe is { IsConnected: false }) await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Sends the request <paramref name="create"/> makes; null when the connection is retiring and the request
    /// belongs on the next host.</summary>
    public async Task<OpenRequest?> BeginAsync(Func<long, HostRequest> create, CancellationToken ct)
    {
        if (!TryOpen()) return null;
        var request = create(Interlocked.Increment(ref _nextId));
        var replies = Channel.CreateUnbounded<HostReply>(new UnboundedChannelOptions { SingleReader = true });
        _open[request.RequestId] = replies;
        if (!IsAlive) replies.Writer.TryComplete(Lost("The AI host has stopped."));
        try
        {
            await _channel.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            await PipeLostAsync("the pipe broke").ConfigureAwait(false);
        }
        return new OpenRequest(this, request.RequestId, replies.Reader);
    }

    /// <summary>Bytes to fill and send in a request on this connection: an array when small, else a section the host made.</summary>
    public Task<SharedBytes> AllocateAsync(int length, CancellationToken ct) => _sections.AllocateAsync(length, ct);

    /// <summary>Files arrived in the host's model store, verified, or left it, whichever app asked: their paths in the store. Raised
    /// on the thread that reads the pipe.</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesChanged;

    private async Task ReadAsync()
    {
        try
        {
            while (await _channel.ReceiveAsync(_life.Token) is { } message)
            {
                switch (message)
                {
                    case Pong:
                        Interlocked.Exchange(ref _lastPong, _time.GetTimestamp());
                        break;
                    case SectionAllocated allocated:
                        _sections.Answer(allocated.Ticket, allocated);
                        break;
                    case SectionRefused refused:
                        _sections.Answer(refused.Ticket, refused);
                        break;
                    case SectionClosed closed:
                        _sections.Closed(closed.Id);
                        break;
                    case ModelFilesChanged changed:
                        FilesChanged?.Invoke(this, changed.Paths);
                        break;
                    case HostReply reply when _open.TryGetValue(reply.RequestId, out var replies):
                        replies.Writer.TryWrite(reply);
                        if (reply is Completed or Failed)
                        {
                            replies.Writer.TryComplete();
                            _open.TryRemove(reply.RequestId, out _);
                        }
                        break;
                }
            }
            await PipeLostAsync("the AI host closed its pipe").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.Json.JsonException or InvalidDataException)
        {
            await PipeLostAsync($"the pipe failed: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pings the host and kills it once it has gone <see cref="AiHostTimings.HeartbeatTimeout"/> without answering. A tick that
    /// comes late is this side having stalled — its threads busy, collecting garbage — not the host: no ping went out and no answer
    /// could be read meanwhile, so the host is given its whole time again from then, never blamed for the app's own pause.
    /// </summary>
    private async Task BeatAsync()
    {
        using var timer = new PeriodicTimer(_timings.HeartbeatInterval, _time);
        long sequence = 0;
        var ticked = _time.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(_life.Token))
            {
                var late = _time.GetElapsedTime(ticked) > _timings.HeartbeatInterval * 2;
                ticked = _time.GetTimestamp();
                if (late) Interlocked.Exchange(ref _lastPong, ticked);
                else if (_time.GetElapsedTime(Interlocked.Read(ref _lastPong)) > _timings.HeartbeatTimeout)
                {
                    GiveUp($"the AI host stopped answering for {_timings.HeartbeatTimeout.TotalSeconds:0.#} s");
                    return;
                }
                await _channel.SendAsync(new Ping(++sequence), _life.Token);
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            await PipeLostAsync("the pipe broke").ConfigureAwait(false);
        }
    }

    private async Task WatchExitAsync() => EndExited(await _process!.Exited.ConfigureAwait(false));

    /// <summary>
    /// The pipe closed or broke. A host that dies takes its pipe with it a moment before its exit is seen — the exit waits
    /// for its error output to drain, and a crash report being written holds it back further — and only the exit knows why,
    /// so the end waits up to <see cref="AiHostTimings.ExitWait"/> for it. A host still running past that let its pipe go
    /// while alive, and the end is the pipe's.
    /// </summary>
    private async Task PipeLostAsync(string reason)
    {
        _sections.Lose($"The AI host's pipe was lost: {reason}.");
        if (!IsAlive) return;
        if (_process is not { } process)
        {
            End(null, reason);   // the service's exit is not this app's to wait for
            return;
        }
        int code;
        try
        {
            code = await process.Exited.WaitAsync(_timings.ExitWait, _time, _life.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            End(null, reason);
            return;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;   // ended meanwhile: killed by a watchdog, or closed by the app
        }
        EndExited(code);
    }

    /// <summary>Ends the connection for its process having exited: with the reason it was killed for, when it was, else
    /// with the exit itself.</summary>
    private void EndExited(int code)
    {
        if (Volatile.Read(ref _killReason) is { } killed) End(null, killed);
        else End(code, code == 0 ? "the AI host exited" : $"the AI host exited with {DescribeExit(code, _process!.LastErrors)}");
    }

    /// <summary>An exit in words: its code, named where it is a common fault, and the last line the host wrote to its error
    /// output — usually the native runtime's own account of what failed.</summary>
    private static string DescribeExit(int code, IReadOnlyList<string> errors)
        => $"exit code {AiHostExitCode.Describe(code)}" + (errors is [.., var last] ? $": {last}" : "");

    /// <summary>Gives the host up — it is hung — and ends the connection with <paramref name="reason"/>. A host this app started is
    /// killed, its exit then reporting the reason rather than a bare exit code; the service it joined is let go — its connection closed,
    /// for the next request to join again — and goes on serving the apps that share it.</summary>
    public void GiveUp(string reason)
    {
        if (_process is not { } process)
        {
            End(null, reason);
            _ = _channel.DisposeAsync().AsTask();
            return;
        }
        Interlocked.CompareExchange(ref _killReason, reason, null);
        process.Kill();
        End(null, reason);
    }

    private void End(int? exitCode, string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _life.Cancel();
        var lost = Lost(_closing ? "The AI host was shut down." : $"The AI host was lost: {reason}.");
        foreach (var replies in _open.Values) replies.Writer.TryComplete(lost);
        _open.Clear();
        if (!_closing) Ended?.Invoke(this, new AiHostExitedEventArgs(_processId, exitCode, reason, _process?.LastErrors ?? []));
    }

    private static AiHostException Lost(string message) => new(AiHostFailure.Lost, message);

    private void Forget(long requestId) => _open.TryRemove(requestId, out _);

    private Task SendCancelAsync(long requestId) => _channel.SendAsync(new CancelRequest(requestId));

    /// <summary>Closes the pipe, which ends a host this app started and lets go of the service it joined — once, however many ask: a
    /// host retiring for having stood idle can be closed by the request that found it retiring too.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_idleGate) _closed ??= Task.Run(CloseAsync);
        return new ValueTask(_closed);
    }

    private async Task CloseAsync()
    {
        _closing = true;
        End(null, "closed");
        await _channel.DisposeAsync().ConfigureAwait(false);   // a host this app started sees its pipe close and exits
        if (_process is { } process)
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.Exited.WaitAsync(grace.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { process.Kill(); }
        }
        await Task.WhenAll(_reading, _beating).ConfigureAwait(false);
        _process?.Dispose();
        _life.Dispose();
    }

    /// <summary>Cancels a request in the host, saying nothing where the pipe has gone meanwhile.</summary>
    private async Task CancelQuietlyAsync(long requestId)
    {
        try { await SendCancelAsync(requestId).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    /// <summary>One request in flight: its replies, its stall watchdog, and its cancellation.</summary>
    internal sealed class OpenRequest(HostConnection connection, long id, ChannelReader<HostReply> replies) : IAsyncDisposable
    {
    private bool _ended;

        public long Id => id;

        /// <summary>Sends input into the request while it runs.</summary>
        /// <exception cref="AiHostException">The host was lost.</exception>
        public async Task SendInputAsync(RequestInput input, CancellationToken ct)
        {
            if (!connection.IsAlive) throw Lost("The AI host has stopped.");
            try
            {
                await connection._channel.SendAsync(input, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                await connection.PipeLostAsync("the pipe broke").ConfigureAwait(false);
                throw Lost("The AI host was lost: the pipe broke.");
            }
        }

        /// <summary>The next reply. Where none arrives within <paramref name="stallLimit"/>, a host this app started is killed; on the
        /// service other apps share, the request alone is cancelled, and fails (<see cref="AiHostFailure.Stalled"/>).</summary>
        public async Task<HostReply> NextAsync(TimeSpan stallLimit, CancellationToken ct)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(stallLimit);
            try
            {
                var reply = await replies.ReadAsync(stall.Token).ConfigureAwait(false);
                _ended = reply is Completed or Failed;
                return reply;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _ended = true;
                if (connection.Shared)
                {
                    await connection.CancelQuietlyAsync(id).ConfigureAwait(false);
                    throw new AiHostException(AiHostFailure.Stalled,
                        $"The AI host left a request without a reply for {stallLimit.TotalSeconds:0.#} s; the request was cancelled.");
                }
                connection.GiveUp($"a request went {stallLimit.TotalSeconds:0.#} s without a reply");
                throw Lost($"The AI host stopped responding to a request for {stallLimit.TotalSeconds:0.#} s.");
            }
            catch (ChannelClosedException ex) when (ex.InnerException is AiHostException lost)
            {
                _ended = true;
                throw lost;
            }
        }

        /// <summary>A request left before its end — cancelled, or its reader stopped early — is cancelled in the host
        /// too, so it stops holding the GPU. A host this app started that does not end it within the grace is hung, and killed; the
        /// service other apps share is no longer waited for.</summary>
        public async ValueTask DisposeAsync()
        {
            if (!_ended && connection.IsAlive)
            {
                try
                {
                    await connection.SendCancelAsync(id).ConfigureAwait(false);
                    using var grace = new CancellationTokenSource(connection._timings.CancelGrace);
                    while (await replies.ReadAsync(grace.Token).ConfigureAwait(false) is not (Completed or Failed)) { }
                }
                catch (OperationCanceledException) when (!connection.Shared)
                {
                    connection.GiveUp($"a cancelled request did not end within {connection._timings.CancelGrace.TotalSeconds:0.#} s");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is ChannelClosedException or IOException or ObjectDisposedException) { }
            }
            connection.Forget(id);
            connection.Closed();
        }
    }
}
