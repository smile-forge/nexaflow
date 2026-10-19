/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * io/SubInputStream.java and the javax.imageio stream methods it relies on.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: one reader over a stretch of an array in place of the stream and sub-stream classes.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A segment's bytes and the place reached in them, read a byte or a bit at a time, most significant bit first. A
/// sub-range is another reader over part of the same array: nothing is copied and nothing is shared but the bytes.
/// Reading past the end is a <see cref="Jbig2DataException"/>: the segment ends sooner than its data says it does.
/// </summary>
internal sealed class Jbig2Reader
{
    private long _position;
    private int _bitOffset;

    public Jbig2Reader(byte[] bytes, int start, int length)
    {
        Bytes = bytes;
        Start = start;
        Length = length;
    }

    /// <summary>The array the bytes are in; they run from <see cref="Start"/> for <see cref="Length"/> bytes.</summary>
    public byte[] Bytes { get; }

    public int Start { get; }

    public int Length { get; }

    /// <summary>The next byte to read; a byte only partly read by <see cref="ReadBit"/> is still the next.</summary>
    public long Position => _position;

    /// <summary>Moves to a byte, at the start of it. A place past the end is allowed; reading from there is not.</summary>
    public void Seek(long position)
    {
        _position = Math.Max(0, position);
        _bitOffset = 0;
    }

    /// <summary>A reader over <paramref name="length"/> bytes from <paramref name="offset"/>, or as many of them as there are.</summary>
    public Jbig2Reader Slice(long offset, long length)
    {
        var start = (int)Math.Clamp(offset, 0, Length);
        var count = (int)Math.Clamp(length, 0, Length - start);
        return new Jbig2Reader(Bytes, Start + start, count);
    }

    /// <summary>The next byte, or -1 past the end.</summary>
    public int Read()
    {
        if (_position >= Length) return -1;
        _bitOffset = 0;
        return Bytes[Start + (int)_position++];
    }

    public byte ReadByte()
    {
        var value = Read();
        if (value < 0) throw Jbig2DataException.DataEnd();
        return (byte)value;
    }

    public int ReadBit()
    {
        if (_position >= Length) throw Jbig2DataException.DataEnd();
        var bit = Bytes[Start + (int)_position] >> (7 - _bitOffset) & 1;
        if (++_bitOffset == 8)
        {
            _bitOffset = 0;
            _position++;
        }
        return bit;
    }

    /// <summary>Up to 32 bits as an unsigned number.</summary>
    public long ReadBits(int count)
    {
        if (count is < 0 or > 32) throw new Jbig2DataException($"{count} bits cannot be read as one value");
        long value = 0;
        for (var i = 0; i < count; i++) value = value << 1 | (long)ReadBit();
        return value;
    }

    public uint ReadUnsignedInt() => (uint)ReadBits(32);

    /// <summary>Steps over what is left of a byte partly read, to the start of the next.</summary>
    public void SkipBits()
    {
        if (_bitOffset == 0) return;
        _bitOffset = 0;
        if (_position < Length) _position++;
    }
}
