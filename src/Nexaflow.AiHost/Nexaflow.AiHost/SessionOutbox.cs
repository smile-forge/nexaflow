using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// What a session sends its client, written to the pipe by one writer, in order, so nothing that reads the pipe ever waits on a
/// send. What keeps the connection itself going — a heartbeat's answer, a section given or closed, a word on the store — goes
/// ahead of replies and is never held back. Requests' replies queue behind it, at most <see cref="MostReplies"/> waiting: a reply
/// sent while the queue is full waits for room, so a client slow to read holds back its own requests and nobody else's — a reply
/// being written, told it waits, stands aside from the rounds it shares with other clients' replies.
/// </summary>
internal sealed class SessionOutbox : IAsyncDisposable
{
    /// <summary>How many replies may wait to be written before the next one waits for room: a second of a fast model's tokens, and
    /// what the pipe's own buffer holds besides.</summary>
    public const int MostReplies = 256;

    /// <summary>A message, and what is done once it is in the pipe — or once the session ends without it.</summary>
    private readonly record struct Outgoing(HostMessage Message, Action? Written);

    private readonly HostChannel _channel;
    private readonly HostLog _log;
    private readonly Channel<Outgoing> _control = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<Outgoing> _replies = Channel.CreateBounded<Outgoing>(
        new BoundedChannelOptions(MostReplies) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _writing;

    public SessionOutbox(HostChannel channel, HostLog log)
    {
        _channel = channel;
        _log     = log;
        _writing = Task.Run(WriteAsync);
    }

    /// <summary>Sends <paramref name="message"/> ahead of every reply waiting; never waits. A session that has ended sends nothing.</summary>
    public void Tell(HostMessage message) => _control.Writer.TryWrite(new Outgoing(message, null));

    /// <summary>Queues <paramref name="reply"/> behind those waiting: complete at once where there is room, once there is where
    /// not. <paramref name="written"/> runs once it is in the pipe, or once the session ends without it.</summary>
    public Task SendAsync(HostReply reply, Action? written = null)
    {
        var item = new Outgoing(reply, written);
        if (_replies.Writer.TryWrite(item)) return Task.CompletedTask;
        return WaitForRoomAsync(item);
    }

    /// <summary>Queues <paramref name="reply"/> where there is room now; false where the queue is full — for what is worth saying only
    /// while the client keeps up, as a word for a quiet request is.</summary>
    public bool TrySend(HostReply reply) => _replies.Writer.TryWrite(new Outgoing(reply, null));

    private async Task WaitForRoomAsync(Outgoing item)
    {
        try { await _replies.Writer.WriteAsync(item).ConfigureAwait(false); }
        catch (ChannelClosedException) { item.Written?.Invoke(); }   // the session has ended: nothing more is sent
    }

    private async Task WriteAsync()
    {
        try
        {
            while (true)
            {
                if (_control.Reader.TryRead(out var told)) await WriteAsync(told).ConfigureAwait(false);
                else if (_replies.Reader.TryRead(out var reply)) await WriteAsync(reply).ConfigureAwait(false);
                else
                {
                    var control = _control.Reader.WaitToReadAsync().AsTask();
                    var replies = _replies.Reader.WaitToReadAsync().AsTask();
                    await Task.WhenAny(control, replies).ConfigureAwait(false);
                    // Both closed and drained: the session has ended.
                    if (control.IsCompleted && replies.IsCompleted && !control.Result && !replies.Result) return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _log.Write($"a client's pipe broke while it was written to: {ex.Message}");
        }
        finally
        {
            // Whatever is left was never sent: what waits on it is let go.
            _control.Writer.TryComplete();
            _replies.Writer.TryComplete();
            while (_control.Reader.TryRead(out var left) || _replies.Reader.TryRead(out left)) left.Written?.Invoke();
        }
    }

    private async Task WriteAsync(Outgoing item)
    {
        try { await _channel.SendAsync(item.Message).ConfigureAwait(false); }
        finally { item.Written?.Invoke(); }
    }

    /// <summary>Writes what is queued, then stops; what cannot be written — the pipe gone — is let go.</summary>
    public async ValueTask DisposeAsync()
    {
        _control.Writer.TryComplete();
        _replies.Writer.TryComplete();
        await _writing.ConfigureAwait(false);
    }
}
