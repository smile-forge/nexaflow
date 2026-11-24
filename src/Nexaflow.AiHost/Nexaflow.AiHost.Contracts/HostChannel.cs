using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// One <see cref="HostMessage"/> a frame, over any stream — a pipe in byte mode, a socket: the message's JSON, then the bytes of each
/// of its byte arrays beside it rather than written into it, so a recording or a small picture crosses as it is, not a third larger
/// as base64 and parsed back. A frame is the JSON's length and the JSON, the number of byte arrays, then each one's length and bytes;
/// every length is four bytes, little-endian. A large block of bytes (<see cref="SharedBytes"/>) does not cross at all: it lives in
/// a shared section, which <paramref name="sections"/> keeps for this end, and only its id and name are in the JSON. Any number of
/// writers may send at once — frames interleave whole — while one loop reads.
/// </summary>
/// <param name="sections">What this end does with sections; null for a channel that carries every block in its frames.</param>
public sealed class HostChannel(Stream stream, ISectionHolder? sections = null) : IAsyncDisposable
{
    /// <summary>The longest JSON a frame may carry: far past any message's, and short of what a corrupt length would claim.</summary>
    public const int MostJsonBytes = 16 << 20;

    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly byte[] _length = new byte[4];
    private List<SharedBytes> _received = [];

    public async Task SendAsync(HostMessage message, CancellationToken ct = default)
    {
        var arrays = new List<byte[]>();
        byte[] json;
        using (ChannelFrame.Writing(arrays, sections))
            json = JsonSerializer.SerializeToUtf8Bytes(message, AiHostJson.Default.HostMessage);

        // The JSON and what follows it up to the first byte array go in one write; each array goes as it is.
        var head = new byte[4 + json.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(head, json.Length);
        json.CopyTo(head, 4);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4 + json.Length), arrays.Count);

        await _write.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(head, ct);
            var length = new byte[4];
            foreach (var array in arrays)
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
        var arrays = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            var array = new byte[await ReadLengthAsync(ct)];
            await ReadAsync(array, ct);
            arrays.Add(array);
        }

        var received = new List<SharedBytes>();
        HostMessage message;
        using (ChannelFrame.Reading(arrays, sections, received))
            message = JsonSerializer.Deserialize(json, AiHostJson.Default.HostMessage)
                      ?? throw new InvalidDataException("An empty message arrived on the AI host's stream.");
        _received = received;
        return message;
    }

    /// <summary>The sections the message <see cref="ReceiveAsync"/> returned last names, for the reader to tie to the request they
    /// belong to; none for a message that names none.</summary>
    public IReadOnlyList<SharedBytes> Sections => _received;

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
/// The frame a message is being written into or read from, on the calling thread: its byte arrays, the end's sections, and the
/// sections read. Serializing a message is synchronous, so the frame is the thread's for as long as it lasts; outside a frame
/// there is none, and neither a byte array nor <see cref="SharedBytes"/> can be written.
/// </summary>
internal static class ChannelFrame
{
    [ThreadStatic] private static State? _state;

    private sealed record State(List<byte[]> Arrays, ISectionHolder? Sections, List<SharedBytes>? Received, State? Outer);

    public static IDisposable Writing(List<byte[]> arrays, ISectionHolder? sections) => new Scope(new State(arrays, sections, null, _state));

    public static IDisposable Reading(List<byte[]> arrays, ISectionHolder? sections, List<SharedBytes> received)
        => new Scope(new State(arrays, sections, received, _state));

    private static State Current => _state ?? throw new InvalidOperationException("Bytes are written and read only in a frame of the AI host's channel.");

    public static ISectionHolder? Sections => Current.Sections;

    /// <summary>Adds an array to the frame being written; its place among them.</summary>
    public static int Add(byte[] array)
    {
        var arrays = Current.Arrays;
        arrays.Add(array);
        return arrays.Count - 1;
    }

    /// <summary>The array at <paramref name="place"/> in the frame being read.</summary>
    public static byte[] ArrayAt(int place)
    {
        var arrays = Current.Arrays;
        return place >= 0 && place < arrays.Count ? arrays[place]
               : throw new JsonException($"The frame carries {arrays.Count} byte array(s); its JSON names array {place}.");
    }

    /// <summary>Notes bytes in a section read from the frame.</summary>
    public static void Received(SharedBytes bytes) => Current.Received?.Add(bytes);

    private sealed class Scope : IDisposable
    {
        private readonly State? _outer;

        public Scope(State state)
        {
            _outer = state.Outer;
            _state = state;
        }

        public void Dispose() => _state = _outer;
    }
}

/// <summary>Writes a message's byte arrays beside its JSON rather than into it (<see cref="HostChannel"/>): each array is written as
/// its place among the frame's arrays, and read back from there.</summary>
internal sealed class ChannelBytesConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ChannelFrame.ArrayAt(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) => writer.WriteNumberValue(ChannelFrame.Add(value));
}
