using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.IO.Location.Wsl.Protocol;

/// <summary>
/// The wire between the WSL backend and the helper it runs inside a distro, over the helper's stdin and stdout. Every
/// message is one frame: its length (int32, little-endian, counting the bytes after it), a code (<see cref="WslOp"/> from
/// the backend, <see cref="WslReply"/> from the helper), the id of the request it belongs to (int32), and a payload.
/// Requests run side by side, so replies to different ids interleave: a listing streams as
/// <see cref="WslReply.Entries"/> pages before its <see cref="WslReply.Done"/>, and a watch pushes
/// <see cref="WslReply.Changed"/> under its own id until it is ended. Compiled into both sides, so they never disagree.
/// </summary>
internal static class WslWire
{
    /// <summary>Raised whenever what a frame means changes; the backend refuses a helper that says another.</summary>
    public const int ProtocolVersion = 3;

    /// <summary>The largest frame either side sends or accepts.</summary>
    public const int MaxFrame = 4 * 1024 * 1024;

    /// <summary>How many entries a listing sends in one page, so a large folder's first rows paint before it is read through.</summary>
    public const int PageSize = 256;

    /// <summary>Reads one frame, or null where the stream ends between frames. A stream ending inside one, or a length no
    /// frame has, is <see cref="InvalidDataException"/>.</summary>
    public static async ValueTask<WslFrame?> ReadAsync(Stream input, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await FillAsync(input, header, ct).ConfigureAwait(false)) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 5 || length > MaxFrame)
            throw new InvalidDataException($"A frame of {length} bytes is not one this wire sends.");

        var body = new byte[length];
        if (!await FillAsync(input, body, ct).ConfigureAwait(false))
            throw new InvalidDataException("The stream ended inside a frame.");
        return new WslFrame(body[0], BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1, 4)), body.AsMemory(5));
    }

    // True when the buffer was filled; false when the stream had already ended before its first byte.
    private static async ValueTask<bool> FillAsync(Stream input, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int got = await input.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (got == 0)
                return read == 0 ? false : throw new InvalidDataException("The stream ended inside a frame.");
            read += got;
        }
        return true;
    }
}

/// <summary>One frame as read: its code, the request it belongs to, and its payload.</summary>
internal readonly record struct WslFrame(byte Code, int Id, ReadOnlyMemory<byte> Payload)
{
    /// <summary>A reader over the payload, from its start.</summary>
    public WslReader Read() => new(Payload);
}

/// <summary>Builds one frame: its code and id first, then what <see cref="WslOp"/> or <see cref="WslReply"/> says its
/// payload holds, in that order.</summary>
internal sealed class WslWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new(256);

    public WslWriter(byte code, int id)
    {
        var head = _buffer.GetSpan(9);
        head[4] = code;
        BinaryPrimitives.WriteInt32LittleEndian(head[5..], id);
        _buffer.Advance(9);
    }

    public WslWriter(WslOp op, int id) : this((byte)op, id) { }

    public WslWriter(WslReply reply, int id) : this((byte)reply, id) { }

    /// <summary>The bytes written so far, past the frame's own header — what a reader of the payload would see.</summary>
    public int PayloadLength => _buffer.WrittenCount - 9;

    public WslWriter Byte(byte value)
    {
        _buffer.GetSpan(1)[0] = value;
        _buffer.Advance(1);
        return this;
    }

    public WslWriter Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public WslWriter Int32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.GetSpan(4), value);
        _buffer.Advance(4);
        return this;
    }

    public WslWriter UInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.GetSpan(4), value);
        _buffer.Advance(4);
        return this;
    }

    public WslWriter Int64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.GetSpan(8), value);
        _buffer.Advance(8);
        return this;
    }

    /// <summary>A string as its UTF-8 length (int32; -1 for null) and bytes.</summary>
    public WslWriter String(string? value)
    {
        if (value is null) return Int32(-1);
        int count = Encoding.UTF8.GetByteCount(value);
        Int32(count);
        Encoding.UTF8.GetBytes(value, _buffer.GetSpan(count));
        _buffer.Advance(count);
        return this;
    }

    /// <summary>Bytes as their length (int32) and themselves.</summary>
    public WslWriter Bytes(ReadOnlySpan<byte> value)
    {
        Int32(value.Length);
        value.CopyTo(_buffer.GetSpan(value.Length));
        _buffer.Advance(value.Length);
        return this;
    }

    public WslWriter Entry(WireEntry entry)
        => String(entry.Name).Byte((byte)entry.Kind).Int64(entry.Size).Int64(entry.ModifiedTicks).Int64(entry.CreatedTicks)
           .UInt32(entry.Mode).Bool(entry.HasOwner).UInt32(entry.Uid).UInt32(entry.Gid).String(entry.Owner).String(entry.Group)
           .String(entry.LinkTarget).Bool(entry.LinkToDirectory);

    public WslWriter Failure(WslFailure failure, string message) => Byte((byte)failure).String(message);

    /// <summary>The frame, its length filled in. The writer may not be written to after.</summary>
    public byte[] ToFrame()
    {
        var frame = _buffer.WrittenSpan.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length - 4);
        return frame;
    }
}

/// <summary>Reads a frame's payload in the order <see cref="WslWriter"/> wrote it. A payload shorter than what is read is
/// <see cref="InvalidDataException"/>.</summary>
internal sealed class WslReader(ReadOnlyMemory<byte> payload)
{
    private int _at;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _at + count > payload.Length)
            throw new InvalidDataException("A frame's payload ended before what it says it holds.");
        var span = payload.Span.Slice(_at, count);
        _at += count;
        return span;
    }

    public byte Byte() => Take(1)[0];

    public bool Bool() => Byte() != 0;

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public string? String()
    {
        int count = Int32();
        return count < 0 ? null : Encoding.UTF8.GetString(Take(count));
    }

    /// <summary>A string that may not be null.</summary>
    public string Text() => String() ?? throw new InvalidDataException("A frame held no text where it must.");

    /// <summary>Bytes as <see cref="WslWriter.Bytes"/> wrote them.</summary>
    public ReadOnlySpan<byte> Bytes() => Take(Int32());

    public WireEntry Entry()
        => new(Text(), (WireKind)Byte(), Int64(), Int64())
        {
            CreatedTicks    = Int64(),
            Mode            = UInt32(),
            HasOwner        = Bool(),
            Uid             = UInt32(),
            Gid             = UInt32(),
            Owner           = String(),
            Group           = String(),
            LinkTarget      = String(),
            LinkToDirectory = Bool(),
        };

    public (WslFailure Failure, string Message) Failure() => ((WslFailure)Byte(), Text());
}
