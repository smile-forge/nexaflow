using System.Collections.Concurrent;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// One client's session: reads its pipe and dispatches. Heartbeats, cancellations and input are handled here, on the reading loop,
/// and what they answer goes out through the session's outbox (<see cref="SessionOutbox"/>), so none waits behind a running request
/// or a send; each request runs on its own task, as its client (<see cref="ClientIdentity"/>), and ends in exactly one Completed or
/// Failed. A client that sends what cannot be read ends its own session, never the host.
/// </summary>
internal sealed class HostServer
{
    private readonly HostChannel _channel;
    private readonly SessionOutbox _outbox;
    private readonly HostLog _log;
    private readonly Dictionary<Type, IHostRequestHandler> _handlers;
    private readonly ConcurrentDictionary<long, RunningRequest> _running = new();

    private readonly TimeProvider _clock;
    private readonly HostSections? _sections;
    private readonly ClientIdentity _client;

    /// <param name="sections">The shared sections the host makes for this client; null where it makes none and every block
    /// crosses in its frame.</param>
    /// <param name="client">Who the session serves; the host itself where none is named — a test's server.</param>
    public HostServer(HostChannel channel, HostLog log, IEnumerable<IHostRequestHandler> handlers, TimeProvider? clock = null, HostSections? sections = null,
                      ClientIdentity? client = null)
    {
        _channel  = channel;
        _outbox   = new SessionOutbox(channel, log);
        _log      = log;
        _handlers = handlers.ToDictionary(h => h.RequestType);
        _clock    = clock ?? TimeProvider.System;
        _sections = sections;
        _client   = client ?? ClientIdentity.Host;
    }

    /// <summary>Who the session serves.</summary>
    /// <summary>Whether a request of this session's is running.</summary>
    public bool Busy => !_running.IsEmpty;

    public ClientIdentity Client => _client;

    /// <summary>How long a request may say nothing before the server speaks for it (<see cref="RequestActivity"/>): well inside any
    /// stall limit the client sets.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(15);

    /// <summary>How often the requests running are looked at for one that has been quiet that long.</summary>
    private static readonly TimeSpan QuietCheck = TimeSpan.FromSeconds(5);

    /// <summary>Tells the client something nothing it asked is waiting on — a file of the store arriving, a section closed —
    /// ahead of any reply; never waits.</summary>
    public void Tell(HostMessage message) => _outbox.Tell(message);

    /// <summary>Serves until the client closes the pipe, or sends what cannot be read; every request still running is then cancelled
    /// and awaited, and what is queued for the client written.</summary>
    public async Task RunAsync()
    {
        var serving = new List<Task>();
        using var stop = new CancellationTokenSource();
        var speaking = SpeakForQuietAsync(stop.Token);
        while (await ReceiveAsync() is { } message)
        {
            switch (message)
            {
                case Ping ping:
                    _outbox.Tell(new Pong(ping.Sequence));
                    break;
                case CancelRequest cancel:
                    if (_running.TryGetValue(cancel.RequestId, out var cancelled)) cancelled.Cancel();
                    break;
                case AllocateSection allocate:
                    _outbox.Tell(_sections?.Allocate(allocate.Length) is { } section
                        ? new SectionAllocated(allocate.Ticket, section.SectionId!.Value, section.SectionName!, section.Capacity)
                        : new SectionRefused(allocate.Ticket, _sections is null ? "This host makes no shared sections." : $"The app's shared sections already hold all one app may ({HostSections.MostBytes >> 30} GiB)."));
                    break;
                case ReleaseSection release:
                    _sections?.Release(release.Id);
                    break;
                case RequestInput input:
                    if (_running.TryGetValue(input.RequestId, out var receiving))
                    {
                        receiving.Sections.Take(_channel.Sections);
                        receiving.Receive(input);
                    }
                    else foreach (var bytes in _channel.Sections) _sections?.Return(bytes);
                    break;
                case HostRequest request:
                    // Registered before it runs, so input or a cancellation sent straight after it is never early.
                    var running = new RunningRequest(_clock, new RequestSections(_sections));
                    running.Sections.Take(_channel.Sections);
                    _running[request.RequestId] = running;
                    serving.RemoveAll(t => t.IsCompleted);
                    serving.Add(Task.Run(() => ServeAsync(request, running)));
                    break;
                default:
                    _log.Write($"{_client} sent an unexpected {message.GetType().Name}; it was ignored");
                    break;
            }
        }

        foreach (var running in _running.Values) running.Cancel();
        await Task.WhenAll(serving);
        stop.Cancel();
        await speaking;
        await _outbox.DisposeAsync();
    }

    /// <summary>The client's next message; null once it has closed the pipe, or sent what cannot be read — which ends its session.</summary>
    private async Task<HostMessage?> ReceiveAsync()
    {
        try
        {
            return await _channel.ReceiveAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            _log.Write($"{_client}'s session ends: {ex.Message}");
            return null;
        }
    }

    private async Task ServeAsync(HostRequest request, RunningRequest running)
    {
        HostReply end;
        running.Activity.Enter();
        _client.Enter();
        using (running.Sections.Enter())
        {
            try
            {
                if (!_handlers.TryGetValue(request.GetType(), out var handler))
                    throw new HostRequestException(HostErrorKind.Invalid, $"this host does not handle {request.GetType().Name}");

                await handler.HandleAsync(request, reply =>
                {
                    running.Activity.Said();
                    return _outbox.SendAsync(reply);
                }, running.Input, running.Token);
                end = new Completed(request.RequestId);
            }
            catch (OperationCanceledException) when (running.Token.IsCancellationRequested)
            {
                end = new Failed(request.RequestId, HostErrorKind.Cancelled, "cancelled");
            }
            catch (HostRequestException ex)
            {
                end = new Failed(request.RequestId, ex.Kind, ex.Message);
            }
            catch (Exception ex)
            {
                _log.Write($"{request.GetType().Name} #{request.RequestId} for {_client} failed: {ex}");
                end = new Failed(request.RequestId, HostErrorKind.Runtime, ex.Message);
            }
            finally
            {
                _running.TryRemove(request.RequestId, out _);
            }
        }

        // Once its end is in the pipe, its replies are too: what it was sent ends with it, and what it made for a reply it never sent.
        await _outbox.SendAsync(end, running.Sections.End);
    }

    /// <summary>Says for each request quiet for <see cref="Quiet"/> what it is doing — waiting, or going on between waits — until the
    /// session ends. A request stuck in one step is left silent, for the client's watchdog to hear; nothing is said for one whose
    /// client has not yet read what it was sent.</summary>
    private async Task SpeakForQuietAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(QuietCheck, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                foreach (var (id, running) in _running)
                {
                    if (running.Activity.Due(Quiet) is not { } stage) continue;
                    if (_outbox.TrySend(new Progress(id, stage, 0, 0))) running.Activity.Said();
                }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>A request being served: its cancellation, the input the app sends it while it runs, and the shared sections it uses.</summary>
    private sealed class RunningRequest(TimeProvider clock, RequestSections sections)
    {
        /// <summary>What it has done since it last said anything.</summary>
        public RequestActivity Activity { get; } = new(clock);

        public RequestSections Sections { get; } = sections;

        // Never disposed: the reading loop may still cancel a request that has just ended, and a source with no
        // timer holds nothing to free.
        private readonly CancellationTokenSource _cancel = new();
        private readonly Channel<RequestInput> _input = Channel.CreateUnbounded<RequestInput>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        public CancellationToken Token => _cancel.Token;

        public ChannelReader<RequestInput> Input => _input.Reader;

        public void Cancel() => _cancel.Cancel();

        /// <summary>Queues input for the handler without waiting — the reading loop never waits on a request.
        /// <see cref="InputEnd"/> ends it.</summary>
        public void Receive(RequestInput input)
        {
            if (input is InputEnd) _input.Writer.TryComplete();
            else _input.Writer.TryWrite(input);
        }
    }
}

/// <summary>Serves one kind of <see cref="HostRequest"/>.</summary>
internal interface IHostRequestHandler
{
    Type RequestType { get; }

    /// <summary>Runs the request, sending any replies before its end through <paramref name="reply"/>; returning
    /// completes it, throwing <see cref="HostRequestException"/> fails it with that kind. <paramref name="input"/> is
    /// what the app sends into it while it runs, and ends when the app says no more is coming.</summary>
    Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct);
}

/// <summary>A request whose whole content arrives with it.</summary>
internal abstract class HostRequestHandler<TRequest> : IHostRequestHandler where TRequest : HostRequest
{
    public Type RequestType => typeof(TRequest);

    public Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
        => HandleAsync((TRequest)request, reply, ct);

    protected abstract Task HandleAsync(TRequest request, Func<HostReply, Task> reply, CancellationToken ct);
}

/// <summary>
/// A request the app keeps feeding while it runs — audio to hear, text to speak. A stream may go quiet for as long as
/// the app sends nothing worth answering, so its handler replies with <see cref="Progress"/> while it waits: the
/// app's stall watchdog then tells a quiet stream from a hung one.
/// </summary>
internal abstract class HostStreamHandler<TRequest> : IHostRequestHandler where TRequest : HostRequest
{
    public Type RequestType => typeof(TRequest);

    public Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
        => HandleAsync((TRequest)request, reply, input, ct);

    protected abstract Task HandleAsync(TRequest request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct);
}

/// <summary>A request that cannot be served, with the kind the app reads to decide what to do next.</summary>
internal sealed class HostRequestException(HostErrorKind kind, string message) : Exception(message)
{
    public HostErrorKind Kind { get; } = kind;
}
