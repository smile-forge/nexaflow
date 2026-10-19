/*
 * Ported from libjpeg-turbo 3.1.1, src/jdhuff.c (jpeg_make_d_derived_tbl) and src/jdhuff.h:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1997, Thomas G. Lane.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2009-2011, 2016, 2018-2019, 2022, D. R. Commander.
 * Copyright (C) 2018, Matthias Räncker.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>A Huffman table as a DHT marker defines it — libjpeg's <c>JHUFF_TBL</c>.</summary>
internal sealed class HuffmanTable
{
    /// <summary>The number of codes of each length, 1 to 16; element 0 is unused.</summary>
    public readonly byte[] Bits = new byte[17];

    /// <summary>The symbols, in order of code length.</summary>
    public readonly byte[] Values = new byte[256];

    public static HuffmanTable Of(ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values)
    {
        var table = new HuffmanTable();
        bits.CopyTo(table.Bits);
        values.CopyTo(table.Values);
        return table;
    }
}

/// <summary>
/// A Huffman table expanded for decoding — libjpeg's <c>d_derived_tbl</c>: the largest code of each length, the
/// offset from a code to its symbol, and a lookahead table that decodes any code of eight bits or fewer in one step.
/// </summary>
internal sealed class DerivedHuffmanTable
{
    /// <summary>HUFF_LOOKAHEAD: the bits looked up at once.</summary>
    public const int Lookahead = 8;

    /// <summary>The largest code of each length, -1 when there is none; [17] is a sentinel that ends every search.</summary>
    public readonly int[] MaxCode = new int[18];

    /// <summary>For a code of length k, its symbol is <c>Values[code + ValOffset[k]]</c>.</summary>
    public readonly int[] ValOffset = new int[18];

    /// <summary>
    /// Indexed by the next eight bits: the code's length in bits 8 and up (9 when longer than eight) and its symbol in
    /// the low eight.
    /// </summary>
    public readonly int[] Lookup = new int[1 << Lookahead];

    public byte[] Values = [];

    /// <summary>
    /// 3.1.1 - jpeg_make_d_derived_tbl: expands table <paramref name="tableNumber"/> into <paramref name="derived"/>,
    /// reusing the entry already there.
    /// </summary>
    public static DerivedHuffmanTable Make(HuffmanTable?[] tables, bool isDc, int tableNumber, bool lossless, DerivedHuffmanTable?[] derived)
    {
        if (tableNumber is < 0 or >= 4) throw JpegErrors.Fatal(JpegErrors.NoHuffTable(tableNumber));
        var htbl = tables[tableNumber] ?? throw JpegErrors.Fatal(JpegErrors.NoHuffTable(tableNumber));

        var dtbl = derived[tableNumber] ??= new DerivedHuffmanTable();
        dtbl.Values = htbl.Values;

        // Figure C.1: make table of Huffman code length for each symbol
        Span<byte> huffsize = stackalloc byte[257];
        Span<uint> huffcode = stackalloc uint[257];
        var p = 0;
        for (var l = 1; l <= 16; l++)
        {
            int i = htbl.Bits[l];
            if (p + i > 256) throw JpegErrors.Fatal(JpegErrors.BadHuffTable);
            while (i-- > 0) huffsize[p++] = (byte)l;
        }
        huffsize[p] = 0;
        var numsymbols = p;

        // Figure C.2: generate the codes themselves, checking they form a legal Huffman code tree
        uint code = 0;
        int si = huffsize[0];
        p = 0;
        while (huffsize[p] != 0)
        {
            while (huffsize[p] == si)
            {
                huffcode[p++] = code;
                code++;
            }
            if (code >= 1u << si) throw JpegErrors.Fatal(JpegErrors.BadHuffTable);
            code <<= 1;
            si++;
        }

        // Figure F.15: generate decoding tables for bit-sequential decoding
        p = 0;
        for (var l = 1; l <= 16; l++)
        {
            if (htbl.Bits[l] != 0)
            {
                dtbl.ValOffset[l] = p - (int)huffcode[p];
                p += htbl.Bits[l];
                dtbl.MaxCode[l] = (int)huffcode[p - 1];
            }
            else
            {
                dtbl.MaxCode[l] = -1;
            }
        }
        dtbl.ValOffset[17] = 0;
        dtbl.MaxCode[17] = 0xFFFFF;

        // Lookahead tables: every entry "too long", then each short code fills the entries it begins.
        dtbl.Lookup.AsSpan().Fill((Lookahead + 1) << Lookahead);
        p = 0;
        for (var l = 1; l <= Lookahead; l++)
        {
            for (var i = 1; i <= htbl.Bits[l]; i++, p++)
            {
                var lookbits = (int)(huffcode[p] << (Lookahead - l));
                for (var ctr = 1 << (Lookahead - l); ctr > 0; ctr--)
                    dtbl.Lookup[lookbits++] = (l << Lookahead) | htbl.Values[p];
            }
        }

        // DC symbols must lie in 0..15 for lossy coding and 0..16 for lossless.
        if (isDc)
        {
            for (var i = 0; i < numsymbols; i++)
                if (htbl.Values[i] > (lossless ? 16 : 15)) throw JpegErrors.Fatal(JpegErrors.BadHuffTable);
        }

        return dtbl;
    }
}
