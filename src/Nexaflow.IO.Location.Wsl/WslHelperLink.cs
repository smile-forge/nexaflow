using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// The helper running inside one distro, and the requests in flight to it. Requests are numbered and run side by side;
/// one reader routes each reply to its request by id. When the helper ends — the distro shut down, the process killed —
/// every request still waiting fails with <see cref="LocationError.Disconnected"/>, so the session pool lets the session
/// go and the next use reconnects.
/// </summary>
internal sealed class WslHelperLink : IAsyncDisposable
{
    private readonly string _distro;
    private readonly Process _process;
    private readonly Stream _toHelper;
    private readonly Stream _fromHelper;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly ConcurrentDictionary<int, Channel<WslFrame>> _waiting = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _reading;
    private int _lastId;
    private int _gone;

    public WslHelperLink(string distro, Process process)
    {
        _distro     = distro;
        _process    = process;
        _toHelper   = process.StandardInput.BaseStream;
        _fromHelper = process.StandardOutput.BaseStream;
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) Trace.TraceWarning($"WSL helper in {distro}: {e.Data}");
        };
        process.BeginErrorReadLine();
        _reading = Task.Run(ReadAsync);
    }

    /// <summary>True once the helper has ended; every request fails from then on.</summary>
    public bool IsGone => Volatile.Read(ref _gone) != 0;

    /// <summary>Ends the helper's process as a crash would — what a test does to drop the link.</summary>
    internal void Kill()
    {
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Sends a request and waits for the reply that ends it — <see cref="WslReply.Done"/>,
    /// <see cref="WslReply.Entry"/>, <see cref="WslReply.HelloReply"/> — failing as the helper says, against
    /// <paramref name="path"/>.</summary>
    public async ValueTask<WslFrame> CallAsync(WslOp op, Action<WslWriter> payload, string path, CancellationToken ct)
    {
        int id = Interlocked.Increment(ref _lastId);
        var replies = Open(id);
        try
        {
            await SendAsync(Build(op, id, payload), path, ct).ConfigureAwait(false);
            var frame = await NextAsync(replies, path, ct).ConfigureAwait(false);
            return frame.Code == (byte)WslReply.Failed ? throw Failure(frame, path) : frame;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CancelRemote(id);
            throw;
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a request whose answer streams — a listing — yielding every frame before its
    /// <see cref="WslReply.Done"/>. Stopping early tells the helper to stop too.</summary>
    public async IAsyncEnumerable<WslFrame> StreamAsync(WslOp op, Action<WslWriter> payload, string path,
                                                        [EnumeratorCancellation] CancellationToken ct)
    {
        int id = Interlocked.Increment(ref _lastId);
        var replies = Open(id);
        bool ended = false;
        try
        {
            await SendAsync(Build(op, id, payload), path, ct).ConfigureAwait(false);
            while (true)
            {
                var frame = await NextAsync(replies, path, ct).ConfigureAwait(false);
                if (frame.Code == (byte)WslReply.Done)
                {
                    ended = true;
                    yield break;
                }
                if (frame.Code == (byte)WslReply.Failed)
                {
                    ended = true;
                    throw Failure(frame, path);
                }
                yield return frame;
            }
        }
        finally
        {
            _waiting.TryRemove(id, out _);
            if (!ended) CancelRemote(id);
        }
    }

    /// <summary>Watches a path, calling <paramref name="changed"/> with each path the helper reports, until the handle is
    /// disposed or the helper ends. A path that cannot be watched reports nothing.</summary>
    public IDisposable Watch(string path, bool recursive, Action<string> changed)
    {
        int id = Interlocked.Increment(ref _lastId);
        var replies = Open(id);
        _ = PumpAsync();
        return new WatchHandle(this, id);

        async Task PumpAsync()
        {
            try
            {
                await SendAsync(Build(WslOp.Watch, id, w => w.String(path).Bool(recursive)), path, CancellationToken.None)
                    .ConfigureAwait(false);
                await foreach (var frame in replies.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    if (frame.Code == (byte)WslReply.Changed) changed(frame.Read().Text());
                    else if (frame.Code == (byte)WslReply.Failed) break;
                }
            }
            catch (Exception ended) when (ended is LocationException or ChannelClosedException or IOException
                                                or InvalidDataException or ObjectDisposedException)
            {
                // The watch ends with the helper; the next listing reconnects.
            }
            finally
            {
                _waiting.TryRemove(id, out _);
            }
        }
    }

    private sealed class WatchHandle(WslHelperLink link, int id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (link._waiting.TryRemove(id, out var replies)) replies.Writer.TryComplete();
            link.SendQuietly(Build(WslOp.Unwatch, Interlocked.Increment(ref link._lastId), w => w.Int32(id)));
        }
    }

    private Channel<WslFrame> Open(int id)
    {
        var replies = Channel.CreateUnbounded<WslFrame>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _waiting[id] = replies;
        if (IsGone) replies.Writer.TryComplete(Disconnected("/"));
        return replies;
    }

    private static async ValueTask<WslFrame> NextAsync(Channel<WslFrame> replies, string path, CancellationToken ct)
    {
        try { return await replies.Reader.ReadAsync(ct).ConfigureAwait(false); }
        catch (ChannelClosedException closed) { throw closed.InnerException as LocationException ?? Gone(path); }

        static LocationException Gone(string path) => new(LocationError.Disconnected, path, "The WSL helper stopped.");
    }

    private static byte[] Build(WslOp op, int id, Action<WslWriter> payload)
    {
        var writer = new WslWriter(op, id);
        payload(writer);
        return writer.ToFrame();
    }

    // A frame goes out whole or not at all: cancelling waits for the turn, never cuts a frame short.
    private async ValueTask SendAsync(byte[] frame, string path, CancellationToken ct)
    {
        if (IsGone) throw Disconnected(path);
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _toHelper.WriteAsync(frame, CancellationToken.None).ConfigureAwait(false);
            await _toHelper.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception broken) when (broken is IOException or ObjectDisposedException)
        {
            End(broken);
            throw Disconnected(path);
        }
        finally
        {
            _sending.Release();
        }
    }

    private void SendQuietly(byte[] frame)
        => _ = Task.Run(async () =>
        {
            try { await SendAsync(frame, "/", CancellationToken.None).ConfigureAwait(false); }
            catch (LocationException) { /* the helper is gone, and what it was asked to stop has stopped with it */ }
        });

    private void CancelRemote(int id) => SendQuietly(Build(WslOp.Cancel, Interlocked.Increment(ref _lastId), w => w.Int32(id)));

    private async Task ReadAsync()
    {
        Exception? why = null;
        try
        {
            while (await WslWire.ReadAsync(_fromHelper, _closing.Token).ConfigureAwait(false) is { } frame)
                if (_waiting.TryGetValue(frame.Id, out var replies)) replies.Writer.TryWrite(frame);
        }
        catch (Exception broken) when (broken is IOException or InvalidDataException or OperationCanceledException
                                             or ObjectDisposedException)
        {
            why = broken;
        }
        End(why);
    }

    private void End(Exception? why)
    {
        if (Interlocked.Exchange(ref _gone, 1) != 0) return;
        var failure = Disconnected("/", why);
        foreach (var replies in _waiting.Values) replies.Writer.TryComplete(failure);
    }

    private LocationException Disconnected(string path, Exception? why = null)
        => new(LocationError.Disconnected, path, $"The WSL helper in '{_distro}' stopped.", why);

    /// <summary>The <see cref="LocationException"/> a <see cref="WslReply.Failed"/> frame stands for; a failure with no
    /// common meaning is a plain <see cref="IOException"/>.</summary>
    internal static Exception Failure(WslFrame frame, string path)
    {
        var (failure, message) = frame.Read().Failure();
        return failure switch
        {
            WslFailure.NotFound     => new LocationException(LocationError.NotFound, path, message),
            WslFailure.AccessDenied => new LocationException(LocationError.AccessDenied, path, message),
            WslFailure.Conflict or WslFailure.NotEmpty => new LocationException(LocationError.Conflict, path, message),
            _                       => new IOException(message),
        };
    }

    /// <summary>Closes the helper's input, which ends it, and kills it if it has not ended within two seconds.</summary>
    public async ValueTask DisposeAsync()
    {
        End(null);
        try { _toHelper.Dispose(); }
        catch (IOException) { }

        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await _process.WaitForExitAsync(patience.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { Kill(); }

        await _closing.CancelAsync().ConfigureAwait(false);
        try { await _reading.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _process.Dispose();
        _closing.Dispose();
    }
}
