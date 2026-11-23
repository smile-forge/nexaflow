using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.AiHost.Contracts;

/// <summary>
/// A block of bytes a message carries — a picture's pixels, a mask, a clip's frames, a recording. A small one crosses in its
/// frame beside the message's JSON. A large one never crosses the pipe at all: it lives in a shared memory section the host
/// made, which both ends keep mapped and reuse block after block, and only the section's id and name cross. Read and write it
/// through <see cref="Memory"/>, in place, until it is sent or disposed — a section sent is the other end's, and one disposed
/// goes back to be reused — and dispose it once done with.
/// </summary>
public sealed class SharedBytes : IMemoryOwner<byte>
{
    /// <summary>Blocks up to this many bytes cross in their frame: below it a section costs more to make than the copy.</summary>
    public const int InlineLimit = 1 << 20;

    private readonly byte[]? _array;
    private readonly MemoryManager<byte>? _section;
    private readonly Action<SharedBytes>? _released;
    private readonly int _length;
    private int _disposed;

    private SharedBytes(byte[] array) => _array = array;

    /// <param name="holder">The connection's end it was made or opened by: it crosses only that connection.</param>
    /// <param name="section">The end's view of the whole section, which outlives this use of it.</param>
    /// <param name="length">How much of the section this block is.</param>
    /// <param name="released">Told when it is disposed, for the end that must say it is done with the section.</param>
    internal SharedBytes(ISectionHolder holder, long id, string name, MemoryManager<byte> section, int length, Action<SharedBytes>? released)
    {
        Holder = holder;
        SectionId = id;
        SectionName = name;
        _section = section;
        _length = length;
        _released = released;
    }

    /// <summary>Bytes in managed memory, which cross in their frame.</summary>
    public static SharedBytes Of(byte[] bytes) => new(bytes);

    public int Length => _array?.Length ?? _length;

    /// <exception cref="ObjectDisposedException">The bytes are in a section, and have been sent or disposed.</exception>
    public Memory<byte> Memory
        => _array ?? (Usable ? _section!.Memory[.._length]
                              : throw new ObjectDisposedException(nameof(SharedBytes), "Bytes in a shared section are the other end's once sent, and go back to be reused once disposed."));

    public Span<byte> Span => Memory.Span;

    public byte[] ToArray() => Memory.ToArray();

    /// <summary>The section it lives in, by the host's id for it; null for bytes that cross in their frame.</summary>
    public long? SectionId { get; }

    /// <summary>The section's name, which the end that did not make it opens it by.</summary>
    public string? SectionName { get; }

    internal byte[]? Array => _array;

    /// <summary>How many bytes the section holds, of which this block is the first <see cref="Length"/>.</summary>
    internal int Capacity => _section?.Memory.Length ?? Length;

    /// <summary>The end of the connection its section belongs to; null for bytes that cross in their frame.</summary>
    internal ISectionHolder? Holder { get; }

    /// <summary>Whether it has been written into a message: a section sent is the other end's.</summary>
    internal bool Sent { get; set; }

    /// <summary>Whether its section may still be read and written through it.</summary>
    internal bool Usable => Volatile.Read(ref _disposed) == 0 && !Sent;

    /// <summary>Its section has gone back to be reused by the end that holds it: nothing more is read or written through it.</summary>
    internal void Retire() => Interlocked.Exchange(ref _disposed, 1);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _released?.Invoke(this);
    }
}

/// <summary>
/// What one end of a channel does with the sections <see cref="SharedBytes"/> live in. The host keeps a table of the sections it
/// made and finds each by its id, never opening a name it is sent; the app opens a section by the name the host gave it.
/// </summary>
public interface ISectionHolder
{
    /// <summary>The bytes of section <paramref name="id"/>, named <paramref name="name"/> and holding <paramref name="capacity"/>
    /// bytes, of which the block is the first <paramref name="length"/>, as a message read from the channel names them.</summary>
    /// <exception cref="InvalidDataException">There is no such section for this end to read.</exception>
    SharedBytes Read(long id, string name, int length, int capacity);
}

/// <summary>Writes <see cref="SharedBytes"/> as the channel moves them: in the frame beside the JSON, or by the section they
/// live in.</summary>
internal sealed class SharedBytesConverter : JsonConverter<SharedBytes>
{
    public override SharedBytes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Shared bytes are an object.");
        int? array = null;
        long? id = null;
        string? name = null;
        int length = 0, capacity = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string property = reader.GetString()!;
            reader.Read();
            switch (property)
            {
                case "a": array = reader.GetInt32(); break;
                case "s": id = reader.GetInt64(); break;
                case "name": name = reader.GetString(); break;
                case "n": length = reader.GetInt32(); break;
                case "c": capacity = reader.GetInt32(); break;
                default: reader.Skip(); break;
            }
        }
        if (array is { } place) return SharedBytes.Of(ChannelFrame.ArrayAt(place));
        if (id is not { } section || name is null || length < 0 || capacity < length)
            throw new JsonException("Shared bytes name neither their frame array nor their section.");
        var holder = ChannelFrame.Sections ?? throw new JsonException("Shared bytes in a section arrived on a channel that holds none.");
        var bytes = holder.Read(section, name, length, capacity);
        ChannelFrame.Received(bytes);
        return bytes;
    }

    public override void Write(Utf8JsonWriter writer, SharedBytes value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.SectionId is { } id)
        {
        if (value.Holder != ChannelFrame.Sections)
            throw new InvalidOperationException("Shared bytes were made on a connection to the AI host that has since been lost; ask for them again.");
        if (!value.Usable)
            throw new ObjectDisposedException(nameof(SharedBytes), "Shared bytes already sent or disposed are not sent again.");
        writer.WriteNumber("s", id);
        writer.WriteString("name", value.SectionName);
        writer.WriteNumber("n", value.Length);
        writer.WriteNumber("c", value.Capacity);
            value.Sent = true;
        }
        else writer.WriteNumber("a", ChannelFrame.Add(value.Array!));
        writer.WriteEndObject();
    }
}
