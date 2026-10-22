using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// One running host and its pipe: the handshake, the loop that routes replies to their requests, and the heartbeat.
/// When anything ends it — the process exiting, the pipe breaking, a watchdog — every open request fails as
/// <see cref="AiHostFailure.Lost"/> and <see cref="Ended"/> is raised once, unless the app ended it itself.
/// </summary>
internal sealed class HostConnection : IAsyncDisposable
{
    private readonly IAiHostProcess _process;
    private readonly HostChannel _channel;
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

    private HostConnection(IAiHostProcess process, AiHostEnvironment environment, HostChannel channel, AiHostTimings timings,
                           TimeProvider time)
    {
        _process     = process;
        Environment  = environment;
        _channel     = channel;
        _timings     = timings;
        _time        = time;
        _lastPong    = time.GetTimestamp();
    }

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

    private void Opened()
    {
        lock (_idleGate)
            if (_inFlight++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void Closed()
    {
        lock (_idleGate)
            if (--_inFlight == 0) _idle.TrySetResult();
    }

    public event EventHandler<AiHostExitedEventArgs>? Ended;

    public bool IsAlive => Volatile.Read(ref _ended) == 0;

    /// <summary>Opens a pipe only this user can reach, starts a host on it and waits for its token.</summary>
    /// <param name="onEnded">Attached before anything can end the connection: a host that dies straight after the
    /// handshake must still be counted.</param>
    public static async Task<HostConnection> StartAsync(IAiHostLauncher launcher, AiHostEnvironment environment, AiHostTimings timings,
                                                        TimeProvider time, EventHandler<AiHostExitedEventArgs> onEnded, CancellationToken ct)
    {
        string pipeName = "nexaflow-aihost-" + Guid.NewGuid().ToString("N");
        string token    = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pipe = CreatePipe(pipeName);

        IAiHostProcess? process = null;
        try
        {
            process = launcher.Start(pipeName, token, environment);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AiHostProtocol.ConnectTimeout);

            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            if (await Task.WhenAny(connected, process.Exited) != connected)
            {
                timeout.Cancel();
                try { await connected; } catch (OperationCanceledException) { }
                throw new AiHostException(AiHostFailure.Unavailable, $"The AI host exited before connecting (exit code {await process.Exited}).");
            }
            await connected;

            var channel = new HostChannel(pipe);
            if (await channel.ReceiveAsync(timeout.Token) is not Hello hello
                || hello.ProtocolVersion != AiHostProtocol.Version
                || !TokensMatch(hello.Token, token))
            {
                await channel.DisposeAsync();
                throw new AiHostException(AiHostFailure.Unavailable, "The AI host did not identify itself.");
            }
            await channel.SendAsync(new Welcome(), timeout.Token);

            var connection = new HostConnection(process, environment, channel, timings, time);
            connection.Ended += onEnded;
            connection._reading = connection.ReadAsync();
            connection._beating = connection.BeatAsync();
            _ = connection.WatchExitAsync();
            return connection;
        }
        catch (Exception ex)
        {
            process?.Kill();
            process?.Dispose();
            await pipe.DisposeAsync();
            if (ex is AiHostException || ct.IsCancellationRequested) throw;
            throw new AiHostException(AiHostFailure.Unavailable, $"The AI host could not be reached: {ex.Message}");
        }
    }

    private static bool TokensMatch(string offered, string expected)
        => offered.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(offered), System.Text.Encoding.ASCII.GetBytes(expected));

    /// <summary>A pipe only this user can open, buffered both ways: unbuffered, every write would wait for the other
    /// end to read it, and a heartbeat could queue behind a slow read.</summary>
    private static NamedPipeServerStream CreatePipe(string name)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
                                               PipeOptions.Asynchronous, PipeBuffer, PipeBuffer, security);
    }

    private const int PipeBuffer = 64 * 1024;

    public async Task<OpenRequest> BeginAsync(Func<long, HostRequest> create, CancellationToken ct)
    {
        var request = create(Interlocked.Increment(ref _nextId));
        var replies = Channel.CreateUnbounded<HostReply>(new UnboundedChannelOptions { SingleReader = true });
        _open[request.RequestId] = replies;
        Opened();
        if (!IsAlive) replies.Writer.TryComplete(Lost("The AI host has stopped."));
        try
        {
            await _channel.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            End(null, "the pipe broke");
        }
        return new OpenRequest(this, request.RequestId, replies.Reader);
    }

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
            End(null, "the AI host closed its pipe");
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.Json.JsonException or InvalidDataException)
        {
            End(null, $"the pipe failed: {ex.Message}");
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
                    Kill($"the AI host stopped answering for {_timings.HeartbeatTimeout.TotalSeconds:0.#} s");
                    return;
                }
                await _channel.SendAsync(new Ping(++sequence), _life.Token);
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            End(null, "the pipe broke");
        }
    }

    private async Task WatchExitAsync()
    {
        int code = await _process.Exited;
        if (Volatile.Read(ref _killReason) is { } killed) End(null, killed);
        else End(code, code == 0 ? "the AI host exited" : $"the AI host exited (code {code})" + (_process.LastErrors is { Length: > 0 } errors ? $": {errors}" : ""));
    }

    /// <summary>Kills the host — it is hung — and ends the connection with <paramref name="reason"/>, which its exit
    /// then reports rather than a bare exit code.</summary>
    public void Kill(string reason)
    {
        Interlocked.CompareExchange(ref _killReason, reason, null);
        _process.Kill();
        End(null, reason);
    }

    private void End(int? exitCode, string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _life.Cancel();
        var lost = Lost(_closing ? "The AI host was shut down." : $"The AI host was lost: {reason}.");
        foreach (var replies in _open.Values) replies.Writer.TryComplete(lost);
        _open.Clear();
        if (!_closing) Ended?.Invoke(this, new AiHostExitedEventArgs(exitCode, reason));
    }

    private static AiHostException Lost(string message) => new(AiHostFailure.Lost, message);

    private void Forget(long requestId) => _open.TryRemove(requestId, out _);

    private Task SendCancelAsync(long requestId) => _channel.SendAsync(new CancelRequest(requestId));

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        End(null, "closed");
        await _channel.DisposeAsync();   // the host sees its pipe close and exits
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _process.Exited.WaitAsync(grace.Token); }
        catch (OperationCanceledException) { _process.Kill(); }
        await Task.WhenAll(_reading, _beating);
        _process.Dispose();
        _life.Dispose();
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
                await connection._channel.SendAsync(input, ct);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                connection.End(null, "the pipe broke");
                throw Lost("The AI host was lost: the pipe broke.");
            }
        }

        /// <summary>The next reply; the host is killed when none arrives within <paramref name="stallLimit"/>.</summary>
        public async Task<HostReply> NextAsync(TimeSpan stallLimit, CancellationToken ct)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(stallLimit);
            try
            {
                var reply = await replies.ReadAsync(stall.Token);
                _ended = reply is Completed or Failed;
                return reply;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                connection.Kill($"a request went {stallLimit.TotalSeconds:0.#} s without a reply");
                _ended = true;
                throw Lost($"The AI host stopped responding to a request for {stallLimit.TotalSeconds:0.#} s.");
            }
            catch (ChannelClosedException ex) when (ex.InnerException is AiHostException lost)
            {
                _ended = true;
                throw lost;
            }
        }

        /// <summary>A request left before its end — cancelled, or its reader stopped early — is cancelled in the host
        /// too, so it stops holding the GPU. A host that does not end it within the grace is hung, and killed.</summary>
        public async ValueTask DisposeAsync()
        {
            if (!_ended && connection.IsAlive)
            {
                try
                {
                    await connection.SendCancelAsync(id);
                    using var grace = new CancellationTokenSource(connection._timings.CancelGrace);
                    while (await replies.ReadAsync(grace.Token) is not (Completed or Failed)) { }
                }
                catch (OperationCanceledException)
                {
                    connection.Kill($"a cancelled request did not end within {connection._timings.CancelGrace.TotalSeconds:0.#} s");
                }
                catch (Exception ex) when (ex is ChannelClosedException or IOException or ObjectDisposedException) { }
            }
            connection.Forget(id);
            connection.Closed();
        }
    }
}
