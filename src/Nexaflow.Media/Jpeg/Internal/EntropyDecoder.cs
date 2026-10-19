/*
 * Ported from libjpeg-turbo 3.1.1, src/jdhuff.c (jpeg_fill_bit_buffer, jpeg_huff_decode) and src/jdhuff.h:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1997, Thomas G. Lane.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2009-2011, 2016, 2018-2019, 2022, D. R. Commander.
 * Copyright (C) 2018, Matthias Räncker.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Runtime.CompilerServices;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>A lossy scan's entropy decoder: libjpeg's <c>jpeg_entropy_decoder</c>.</summary>
internal abstract class EntropyDecoder(Decompressor decompressor)
{
    protected readonly Decompressor D = decompressor;

    /// <summary>
    /// Set once the scan's data has run out mid-segment: the MCUs left in the segment stay zero, a uniform grey,
    /// until a restart marker resets it.
    /// </summary>
    public bool InsufficientData;

    /// <summary>Prepares for the scan the last SOS began.</summary>
    public abstract void StartPass();

    /// <summary>Decodes one MCU into its blocks, which hold what earlier scans left (zero before any).</summary>
    public abstract void DecodeMcu(McuBlocks mcu);
}

/// <summary>
/// The bit reading the Huffman decoders share: jdhuff.h's macros and jdhuff.c's out-of-line helpers. The bit buffer
/// and its count live in the caller's locals while an MCU decodes, passed by reference.
/// </summary>
internal abstract class HuffmanBitDecoder(Decompressor decompressor) : EntropyDecoder(decompressor)
{
    /// <summary>MIN_GET_BITS: the buffer is filled to at least this many bits whenever data allows.</summary>
    protected const int MinGetBits = 64 - 7;

    /// <summary>Bit buffer state saved between MCUs (bitread_perm_state).</summary>
    protected ulong BitBuffer;

    protected int BitsLeft;

    /// <summary>
    /// 3.1.1 - jpeg_fill_bit_buffer: loads at least <paramref name="nbits"/> bits. Past the marker that ends the
    /// segment it can load nothing more, and when the request still cannot be met it warns once and supplies zeros.
    /// </summary>
    protected void FillBitBuffer(ref ulong getBuffer, ref int bitsLeft, int nbits)
    {
        if (D.UnreadMarker == 0)
        {
            var source = D.Source;
            while (bitsLeft < MinGetBits)
            {
                if (source.Position == source.Limit) source.Fill();
                int c = source.Buffer[source.Position++];

                if (c == 0xFF)
                {
                    // Padding FFs before a marker are swallowed; FF 00 is an FF data byte.
                    do
                    {
                        if (source.Position == source.Limit) source.Fill();
                        c = source.Buffer[source.Position++];
                    }
                    while (c == 0xFF);

                    if (c == 0)
                    {
                        c = 0xFF;
                    }
                    else
                    {
                        D.UnreadMarker = c;
                        goto NoMoreBytes;
                    }
                }

                getBuffer = (getBuffer << 8) | (uint)c;
                bitsLeft += 8;
            }
            return;
        }

    NoMoreBytes:
        if (nbits > bitsLeft)
        {
            if (!InsufficientData)
            {
                D.Warnings.Add(JpegErrors.HitMarker);
                InsufficientData = true;
            }
            getBuffer <<= MinGetBits - bitsLeft;
            bitsLeft = MinGetBits;
        }
    }

    /// <summary>CHECK_BIT_BUFFER then GET_BITS</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int GetBits(ref ulong getBuffer, ref int bitsLeft, int nbits)
    {
        if (bitsLeft < nbits) FillBitBuffer(ref getBuffer, ref bitsLeft, nbits);
        return (int)(getBuffer >> (bitsLeft -= nbits)) & ((1 << nbits) - 1);
    }

    /// <summary>HUFF_DECODE: the next symbol, by lookahead when eight bits are at hand.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int HuffDecode(ref ulong getBuffer, ref int bitsLeft, DerivedHuffmanTable table)
    {
        int nb;
        if (bitsLeft < DerivedHuffmanTable.Lookahead)
        {
            FillBitBuffer(ref getBuffer, ref bitsLeft, 0);
            if (bitsLeft < DerivedHuffmanTable.Lookahead) return HuffDecodeSlow(ref getBuffer, ref bitsLeft, table, 1);
        }

        var look = (int)(getBuffer >> (bitsLeft - DerivedHuffmanTable.Lookahead)) & ((1 << DerivedHuffmanTable.Lookahead) - 1);
        var entry = table.Lookup[look];
        nb = entry >> DerivedHuffmanTable.Lookahead;
        if (nb <= DerivedHuffmanTable.Lookahead)
        {
            bitsLeft -= nb;
            return entry & ((1 << DerivedHuffmanTable.Lookahead) - 1);
        }
        return HuffDecodeSlow(ref getBuffer, ref bitsLeft, table, nb);
    }

    /// <summary>
    /// 3.1.1 - jpeg_huff_decode: a code of at least <paramref name="minBits"/> bits, a bit at a time per Figure F.16.
    /// Garbage can run past sixteen bits, to the sentinel; that is warned of and decodes as zero.
    /// </summary>
    private int HuffDecodeSlow(ref ulong getBuffer, ref int bitsLeft, DerivedHuffmanTable table, int minBits)
    {
        var l = minBits;
        var code = GetBits(ref getBuffer, ref bitsLeft, l);
        while (code > table.MaxCode[l])
        {
            code <<= 1;
            code |= GetBits(ref getBuffer, ref bitsLeft, 1);
            l++;
        }

        if (l > 16)
        {
            D.Warnings.Add(JpegErrors.HuffBadCode);
            return 0;
        }
        return table.Values[code + table.ValOffset[l]];
    }

    /// <summary>HUFF_EXTEND (Figure F.12): extends the sign of an <paramref name="s"/>-bit value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static int HuffExtend(int x, int s) => x + (((x - (1 << (s - 1))) >> 31) & ((-1 << s) + 1));

    /// <summary>
    /// The start of process_restart, which every Huffman decoder shares: the unused bits are thrown away, their whole
    /// bytes counted as discarded, and the restart marker read.
    /// </summary>
    protected void ReadRestart()
    {
        D.DiscardedBytes += BitsLeft / 8;
        BitsLeft = 0;
        D.ReadRestartMarker();
    }
}
