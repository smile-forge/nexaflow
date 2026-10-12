using System.IO.Pipes;
using System.Text.Json;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// One <see cref="HostMessage"/> per pipe message over a message-mode named pipe. Any number of writers may send at
/// once — replies to concurrent requests interleave whole — while one loop reads.
/// </summary>
public sealed class HostChannel(PipeStream pipe) : IAsyncDisposable
{
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly byte[] _buffer = new byte[16 * 1024];

    public async Task SendAsync(HostMessage message, CancellationToken ct = default)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, AiHostJson.Default.HostMessage);
        await _write.WaitAsync(ct);
        try
        {
            // No flush: a pipe write is unbuffered, and FlushFileBuffers would wait for the other end to read it all.
            await pipe.WriteAsync(bytes, ct);
        }
        finally { _write.Release(); }
    }

    /// <summary>The next message, or null when the other end has closed the pipe. One reader at a time.</summary>
    public async Task<HostMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        using var message = new MemoryStream();
        do
        {
            int read = await pipe.ReadAsync(_buffer, ct);
            if (read == 0) return null;
            message.Write(_buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);

        return JsonSerializer.Deserialize(message.GetBuffer().AsSpan(0, (int)message.Length), AiHostJson.Default.HostMessage)
            ?? throw new InvalidDataException("An empty message arrived on the AI host pipe.");
    }

    public async ValueTask DisposeAsync()
    {
        await pipe.DisposeAsync();
        _write.Dispose();
    }
}
