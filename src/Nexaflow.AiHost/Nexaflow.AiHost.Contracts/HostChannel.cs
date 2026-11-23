
using System.Text.Json;
using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// One <see cref="HostMessage"/> a frame, over any stream — a pipe in byte mode, a socket: the message's JSON, then the bytes of each
/// of its byte arrays beside it rather than written into it, so a picture, a recording or a clip crosses as it is, not a third
/// larger as base64 and parsed back. A frame is the JSON's length and the JSON, the number of byte arrays, then each one's length
/// and bytes; every length is four bytes, little-endian. Any number of writers may send at once — frames interleave whole — while
/// one loop reads.
/// </summary>
public sealed class HostChannel(Stream stream) : IAsyncDisposable
{
    /// <summary>The longest JSON a frame may carry: far past any message's, and short of what a corrupt length would claim.</summary>
    public const int MostJsonBytes = 16 << 20;

    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly byte[] _length = new byte[4];

    public async Task SendAsync(HostMessage message, CancellationToken ct = default)
    {
        var bytes = new List<byte[]>();
        byte[] json;
        using (ChannelBytesConverter.Into(bytes))
            json = JsonSerializer.SerializeToUtf8Bytes(message, AiHostJson.Default.HostMessage);

        // The JSON and what follows it up to the first byte array go in one write; each array goes as it is.
        var head = new byte[4 + json.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(head, json.Length);
        json.CopyTo(head, 4);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4 + json.Length), bytes.Count);

        await _write.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(head, ct);
            var length = new byte[4];
            foreach (var array in bytes)
            {
                BinaryPrimitives.WriteInt32LittleEndian(length, array.Length);
                await stream.WriteAsync(length, ct);
                await stream.WriteAsync(array, ct);
            }
            // No flush: a pipe's write is unbuffered, and flushing one waits for the other end to read everything — two ends
            // sending at once would each wait for the other.
        }
        finally { _write.Release(); }
    }

    /// <summary>The next message, or null when the other end has closed the stream between messages. One reader at a time.</summary>
    /// <exception cref="InvalidDataException">The stream ended inside a frame, or a frame is not one.</exception>
    public async Task<HostMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        int read = await stream.ReadAtLeastAsync(_length, 4, throwOnEndOfStream: false, ct);
        if (read == 0) return null;
        if (read < 4) throw new InvalidDataException("The AI host's stream ended inside a frame.");
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(_length);
        if (jsonLength is <= 0 or > MostJsonBytes) throw new InvalidDataException($"A frame on the AI host's stream claims {jsonLength} bytes of JSON.");

        var json = new byte[jsonLength];
        await ReadAsync(json, ct);
        int count = await ReadLengthAsync(ct);
        var bytes = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            var array = new byte[await ReadLengthAsync(ct)];
            await ReadAsync(array, ct);
            bytes.Add(array);
        }

        using (ChannelBytesConverter.From(bytes))
            return JsonSerializer.Deserialize(json, AiHostJson.Default.HostMessage)
                   ?? throw new InvalidDataException("An empty message arrived on the AI host's stream.");
    }

    private async Task<int> ReadLengthAsync(CancellationToken ct)
    {
        await ReadAsync(_length, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(_length);
        return length >= 0 ? length : throw new InvalidDataException($"A frame on the AI host's stream claims a length of {length}.");
    }

    private async Task ReadAsync(byte[] into, CancellationToken ct)
    {
        try { await stream.ReadExactlyAsync(into, ct); }
        catch (EndOfStreamException ex) { throw new InvalidDataException("The AI host's stream ended inside a frame.", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync();
        _write.Dispose();
    }
}

/// <summary>
/// Writes a message's byte arrays beside its JSON rather than into it (<see cref="HostChannel"/>): each array is written as its
/// place among the frame's arrays, and read back from there. Serializing a message is synchronous, so the frame's arrays are the
/// calling thread's for as long as it lasts; outside a frame there are none, and a byte array cannot be written.
/// </summary>
internal sealed class ChannelBytesConverter : JsonConverter<byte[]>
{
    [ThreadStatic] private static List<byte[]>? _frame;

    /// <summary>The arrays of a frame being written, collected into <paramref name="arrays"/> until the returned value is disposed.</summary>
    public static IDisposable Into(List<byte[]> arrays) => new Frame(arrays);

    /// <summary>The arrays of a frame being read, for its JSON to take by place until the returned value is disposed.</summary>
    public static IDisposable From(List<byte[]> arrays) => new Frame(arrays);

    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var frame = _frame ?? throw new InvalidOperationException("A byte array is read only from a frame of the AI host's channel.");
        int place = reader.GetInt32();
        return place >= 0 && place < frame.Count ? frame[place]
               : throw new JsonException($"The frame carries {frame.Count} byte array(s); its JSON names array {place}.");
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        var frame = _frame ?? throw new InvalidOperationException("A byte array is written only into a frame of the AI host's channel.");
        writer.WriteNumberValue(frame.Count);
        frame.Add(value);
    }

    private sealed class Frame : IDisposable
    {
        private readonly List<byte[]>? _outer = _frame;

        public Frame(List<byte[]> arrays) => _frame = arrays;

        public void Dispose() => _frame = _outer;
    }
}
