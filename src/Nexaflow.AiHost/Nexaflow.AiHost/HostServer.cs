using System.Collections.Concurrent;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// Reads the pipe and dispatches. Heartbeats, cancellations and input are handled here, on the reading loop, so none
/// waits behind a running request; each request runs on its own task and ends in exactly one Completed or Failed.
/// </summary>
internal sealed class HostServer
{
    private readonly HostChannel _channel;
    private readonly HostLog _log;
    private readonly Dictionary<Type, IHostRequestHandler> _handlers;
    private readonly ConcurrentDictionary<long, RunningRequest> _running = new();

    public HostServer(HostChannel channel, HostLog log, IEnumerable<IHostRequestHandler> handlers)
    {
        _channel  = channel;
        _log      = log;
        _handlers = handlers.ToDictionary(h => h.RequestType);
    }

    /// <summary>Serves until the app closes the pipe; every request still running is then cancelled and awaited.</summary>
    public async Task RunAsync()
    {
        var serving = new List<Task>();
        while (await _channel.ReceiveAsync() is { } message)
        {
            switch (message)
            {
                case Ping ping:
                    await _channel.SendAsync(new Pong(ping.Sequence));
                    break;
                case CancelRequest cancel:
                    if (_running.TryGetValue(cancel.RequestId, out var cancelled)) cancelled.Cancel();
                    break;
                case RequestInput input:
                    if (_running.TryGetValue(input.RequestId, out var receiving)) receiving.Receive(input);
                    break;
                case HostRequest request:
                    // Registered before it runs, so input or a cancellation sent straight after it is never early.
                    var running = new RunningRequest();
                    _running[request.RequestId] = running;
                    serving.RemoveAll(t => t.IsCompleted);
                    serving.Add(Task.Run(() => ServeAsync(request, running)));
                    break;
                default:
                    _log.Write($"ignored an unexpected {message.GetType().Name}");
                    break;
            }
        }

        foreach (var running in _running.Values) running.Cancel();
        await Task.WhenAll(serving);
    }

    private async Task ServeAsync(HostRequest request, RunningRequest running)
    {
        HostReply end;
        try
        {
            if (!_handlers.TryGetValue(request.GetType(), out var handler))
                throw new HostRequestException(HostErrorKind.Invalid, $"this host does not handle {request.GetType().Name}");

            await handler.HandleAsync(request, reply => _channel.SendAsync(reply, CancellationToken.None), running.Input, running.Token);
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
            _log.Write($"{request.GetType().Name} #{request.RequestId} failed: {ex}");
            end = new Failed(request.RequestId, HostErrorKind.Runtime, ex.Message);
        }
        finally
        {
            _running.TryRemove(request.RequestId, out _);
        }

        try { await _channel.SendAsync(end); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the app has gone */ }
    }

    /// <summary>A request being served: its cancellation, and the input the app sends it while it runs.</summary>
    private sealed class RunningRequest
    {
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
