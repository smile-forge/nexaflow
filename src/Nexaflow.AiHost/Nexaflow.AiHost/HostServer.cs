using System.Collections.Concurrent;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// Reads the pipe and dispatches. Heartbeats and cancellations are answered here, on the reading loop, so neither
/// waits behind a running request; each request runs on its own task and ends in exactly one Completed or Failed.
/// </summary>
internal sealed class HostServer
{
    private readonly HostChannel _channel;
    private readonly HostLog _log;
    private readonly Dictionary<Type, IHostRequestHandler> _handlers;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _running = new();

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
                    if (_running.TryGetValue(cancel.RequestId, out var cts)) cts.Cancel();
                    break;
                case HostRequest request:
                    serving.RemoveAll(t => t.IsCompleted);
                    serving.Add(Task.Run(() => ServeAsync(request)));
                    break;
                default:
                    _log.Write($"ignored an unexpected {message.GetType().Name}");
                    break;
            }
        }

        foreach (var cts in _running.Values) cts.Cancel();
        await Task.WhenAll(serving);
    }

    private async Task ServeAsync(HostRequest request)
    {
        using var cts = new CancellationTokenSource();
        _running[request.RequestId] = cts;
        HostReply end;
        try
        {
            if (!_handlers.TryGetValue(request.GetType(), out var handler))
                throw new HostRequestException(HostErrorKind.Invalid, $"this host does not handle {request.GetType().Name}");

            await handler.HandleAsync(request, reply => _channel.SendAsync(reply, CancellationToken.None), cts.Token);
            end = new Completed(request.RequestId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
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
}

/// <summary>Serves one kind of <see cref="HostRequest"/>.</summary>
internal interface IHostRequestHandler
{
    Type RequestType { get; }

    /// <summary>Runs the request, sending any replies before its end through <paramref name="reply"/>; returning
    /// completes it, throwing <see cref="HostRequestException"/> fails it with that kind.</summary>
    Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, CancellationToken ct);
}

internal abstract class HostRequestHandler<TRequest> : IHostRequestHandler where TRequest : HostRequest
{
    public Type RequestType => typeof(TRequest);

    public Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, CancellationToken ct)
        => HandleAsync((TRequest)request, reply, ct);

    protected abstract Task HandleAsync(TRequest request, Func<HostReply, Task> reply, CancellationToken ct);
}

/// <summary>A request that cannot be served, with the kind the app reads to decide what to do next.</summary>
internal sealed class HostRequestException(HostErrorKind kind, string message) : Exception(message)
{
    public HostErrorKind Kind { get; } = kind;
}
