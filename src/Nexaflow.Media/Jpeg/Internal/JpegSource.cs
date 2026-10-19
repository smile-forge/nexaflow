/*
 * Ported from libjpeg-turbo 3.1.1, src/jdatasrc-tj.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1994-1996, Thomas G. Lane.
 * Modified 2009-2011 by Guido Vollbeding.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2013, 2016, 2022, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// The compressed data and the place reached in it — libjpeg's source manager as TurboJPEG's memory source makes
/// it. Past the end it supplies a fake EOI marker, warning each time, so a cut-short file decodes as far as it goes.
/// </summary>
internal sealed class JpegSource
{
    private static readonly byte[] FakeEoi = [0xFF, 0xD9];

    private readonly JpegWarnings _warnings;

    /// <summary>The bytes being read: the data, or once it is used up, the two bytes of a fake EOI marker.</summary>
    public byte[] Buffer;

    /// <summary>The next byte to read in <see cref="Buffer"/>.</summary>
    public int Position;

    /// <summary>Where the readable bytes of <see cref="Buffer"/> end.</summary>
    public int Limit;

    public JpegSource(ReadOnlyMemory<byte> data, JpegWarnings warnings)
    {
        // 3.1.1 - jpeg_mem_src_tj
        if (data.IsEmpty) throw JpegErrors.Fatal(JpegErrors.InputEmpty);
        _warnings = warnings;
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment))
        {
            Buffer = segment.Array!;
            Position = segment.Offset;
            Limit = segment.Offset + segment.Count;
        }
        else
        {
            Buffer = data.ToArray();
            Position = 0;
            Limit = Buffer.Length;
        }
    }

    public int BytesInBuffer => Limit - Position;

    /// <summary>3.1.1 - fill_mem_input_buffer: the data is all in memory, so asking for more means it ended.</summary>
    public void Fill()
    {
        _warnings.Add(JpegErrors.JpegEof);
        Buffer = FakeEoi;
        Position = 0;
        Limit = 2;
    }

    /// <summary>INPUT_BYTE: the next byte, refilling first when none is left.</summary>
    public int ReadByte()
    {
        if (Position == Limit) Fill();
        return Buffer[Position++];
    }

    /// <summary>INPUT_2BYTES: the next two bytes, big-endian.</summary>
    public int Read2Bytes()
    {
        var high = ReadByte();
        return (high << 8) + ReadByte();
    }

    /// <summary>3.1.1 - skip_input_data</summary>
    public void Skip(long count)
    {
        if (count <= 0) return;
        while (count > BytesInBuffer)
        {
            count -= BytesInBuffer;
            Fill();
        }
        Position += (int)count;
    }
}
