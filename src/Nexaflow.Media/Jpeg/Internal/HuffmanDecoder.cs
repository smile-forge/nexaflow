/*
 * Ported from libjpeg-turbo 3.1.1, src/jdhuff.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1997, Thomas G. Lane.
 * Lossless JPEG Modifications:
 * Copyright (C) 1999, Ken Murchison.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2009-2011, 2016, 2018-2019, 2022, D. R. Commander.
 * Copyright (C) 2018, Matthias Räncker.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Sequential (baseline and extended) Huffman decoding: libjpeg's jdhuff.c. An MCU decodes by the fast path while
/// plenty of data lies ahead and no restart interval is set; at a marker the fast path gives the MCU up and the
/// careful one decodes it again from where it began.
/// </summary>
internal sealed class HuffmanDecoder(Decompressor decompressor) : HuffmanBitDecoder(decompressor)
{
    /// <summary>BUFSIZE: the bytes an MCU block can take at most, stuffing included.</summary>
    private const int BytesPerBlock = 64 * 8;

    private readonly DerivedHuffmanTable?[] _dcDerived = new DerivedHuffmanTable?[4];
    private readonly DerivedHuffmanTable?[] _acDerived = new DerivedHuffmanTable?[4];
    private readonly DerivedHuffmanTable[] _dcCur = new DerivedHuffmanTable[Decompressor.MaxBlocksInMcu];
    private readonly DerivedHuffmanTable[] _acCur = new DerivedHuffmanTable[Decompressor.MaxBlocksInMcu];
    private readonly int[] _lastDcVal = new int[Decompressor.MaxCompsInScan];
    private int _restartsToGo;

    /// <summary>3.1.1 - start_pass_huff_decoder</summary>
    public override void StartPass()
    {
        // Some baseline files carry zeros here, so this is only a warning.
        if (D.Ss != 0 || D.Se != 63 || D.Ah != 0 || D.Al != 0) D.Warnings.Add(JpegErrors.NotSequential);

        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var component = D.CurComp[ci]!;
            DerivedHuffmanTable.Make(D.DcHuffTables, true, component.DcTableNo, false, _dcDerived);
            DerivedHuffmanTable.Make(D.AcHuffTables, false, component.AcTableNo, false, _acDerived);
            _lastDcVal[ci] = 0;
        }

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var component = D.CurComp[D.McuMembership[blkn]]!;
            _dcCur[blkn] = _dcDerived[component.DcTableNo]!;
            _acCur[blkn] = _acDerived[component.AcTableNo]!;
        }

        BitsLeft = 0;
        BitBuffer = 0;
        InsufficientData = false;
        _restartsToGo = D.RestartInterval;
    }

    /// <summary>3.1.1 - process_restart</summary>
    private void ProcessRestart()
    {
        ReadRestart();
        Array.Clear(_lastDcVal);
        _restartsToGo = D.RestartInterval;
        // Smack up against a marker, the next segment will decode as empty: leave the flag set.
        if (D.UnreadMarker == 0) InsufficientData = false;
    }

    /// <summary>
    /// 3.1.1 - decode_mcu. libjpeg keeps to the careful path whenever a restart interval is set; here the fast path
    /// runs then too. It keeps an MCU only when it met no marker, and short of a marker both paths read the same bits
    /// to the same coefficients — the marker that ends an interval sends that MCU down the careful path, as any other
    /// marker does.
    /// </summary>
    public override void DecodeMcu(McuBlocks mcu)
    {
        if (D.RestartInterval != 0 && _restartsToGo == 0) ProcessRestart();

        var usefast = D.Source.BytesInBuffer >= BytesPerBlock * D.BlocksInMcu && D.UnreadMarker == 0;

        // Out of data: the MCU stays zero, uniform grey for the rest of the segment.
        if (!InsufficientData)
        {
            if (!usefast || !DecodeMcuFast(mcu)) DecodeMcuSlow(mcu);
        }

        if (D.RestartInterval != 0) _restartsToGo--;
    }

    /// <summary>3.1.1 - decode_mcu_slow</summary>
    private void DecodeMcuSlow(McuBlocks mcu)
    {
        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var order = JpegTables.NaturalOrder;

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var block = mcu.Arrays[blkn].AsSpan(mcu.Offsets[blkn], 64);
            var dctbl = _dcCur[blkn];
            var actbl = _acCur[blkn];

            // Section F.2.2.1: the DC coefficient difference
            var s = HuffDecode(ref getBuffer, ref bitsLeft, dctbl);
            if (s != 0)
            {
                var r = GetBits(ref getBuffer, ref bitsLeft, s);
                s = HuffExtend(r, s);
            }

            var ci = D.McuMembership[blkn];
            // Malformed images can grow this past an int's range; libjpeg lets it wrap.
            s = unchecked(s + _lastDcVal[ci]);
            _lastDcVal[ci] = s;
            block[0] = (short)s;

            // Section F.2.2.2: the AC coefficients
            for (var k = 1; k < 64; k++)
            {
                s = HuffDecode(ref getBuffer, ref bitsLeft, actbl);
                var r = s >> 4;
                s &= 15;

                if (s != 0)
                {
                    k += r;
                    r = GetBits(ref getBuffer, ref bitsLeft, s);
                    s = HuffExtend(r, s);
                    // The extra entries of the natural order catch a run past the block in corrupt data.
                    block[order[k]] = (short)s;
                }
                else
                {
                    if (r != 15) break;
                    k += 15;
                }
            }
        }

        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
    }

    /// <summary>
    /// 3.1.1 - decode_mcu_fast: reads the data directly, assuming no marker. Meeting one, it stuffs zeros, and at
    /// the end gives the MCU up so the careful path decodes it again from the saved state.
    /// </summary>
    private bool DecodeMcuFast(McuBlocks mcu)
    {
        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var source = D.Source;
        var buffer = source.Buffer;
        var pos = source.Position;
        var order = JpegTables.NaturalOrder;

        Span<int> lastDcVal = stackalloc int[Decompressor.MaxCompsInScan];
        _lastDcVal.CopyTo(lastDcVal);

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var block = mcu.Arrays[blkn].AsSpan(mcu.Offsets[blkn], 64);
            var dctbl = _dcCur[blkn];
            var actbl = _acCur[blkn];

            var s = HuffDecodeFast(ref getBuffer, ref bitsLeft, buffer, ref pos, dctbl);
            if (s != 0)
            {
                FillFast(ref getBuffer, ref bitsLeft, buffer, ref pos);
                var r = (int)(getBuffer >> (bitsLeft -= s)) & ((1 << s) - 1);
                s = HuffExtend(r, s);
            }

            var ci = D.McuMembership[blkn];
            s = unchecked(s + lastDcVal[ci]);
            lastDcVal[ci] = s;
            block[0] = (short)s;

            for (var k = 1; k < 64; k++)
            {
                s = HuffDecodeFast(ref getBuffer, ref bitsLeft, buffer, ref pos, actbl);
                var r = s >> 4;
                s &= 15;

                if (s != 0)
                {
                    k += r;
                    FillFast(ref getBuffer, ref bitsLeft, buffer, ref pos);
                    r = (int)(getBuffer >> (bitsLeft -= s)) & ((1 << s) - 1);
                    s = HuffExtend(r, s);
                    block[order[k]] = (short)s;
                }
                else
                {
                    if (r != 15) break;
                    k += 15;
                }
            }
        }

        if (D.UnreadMarker != 0)
        {
            D.UnreadMarker = 0;
            return false;
        }

        source.Position = pos;
        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
        lastDcVal.CopyTo(_lastDcVal);
        return true;
    }

    /// <summary>
    /// FILL_BIT_BUFFER_FAST: six bytes when sixteen bits or fewer are left. A marker is backed out of and a zero byte
    /// loaded in its place, the marker noted so the MCU is decoded again.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillFast(ref ulong getBuffer, ref int bitsLeft, byte[] buffer, ref int pos)
    {
        if (bitsLeft > 16) return;

        // Most often none of the six bytes is FF — no stuffing, no marker — and they load at once, as the six
        // byte-at-a-time loads would. FF bytes are the zero bytes of the complement; the two bytes past the six are
        // set so they never count.
        if (pos + 8 <= buffer.Length)
        {
            var next = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(pos, 8));
            var complement = ~next | 0xFFFF;
            if (((complement - 0x0101010101010101UL) & ~complement & 0x8080808080808080UL) == 0)
            {
                getBuffer = (getBuffer << 48) | (next >> 16);
                bitsLeft += 48;
                pos += 6;
                return;
            }
        }

        FillFastBytes(ref getBuffer, ref bitsLeft, buffer, ref pos);
    }

    /// <summary>FILL_BIT_BUFFER_FAST a byte at a time: GET_BYTE six times.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FillFastBytes(ref ulong getBuffer, ref int bitsLeft, byte[] buffer, ref int pos)
    {
        for (var i = 0; i < 6; i++)
        {
            int c0 = buffer[pos++];
            int c1 = buffer[pos];
            getBuffer = (getBuffer << 8) | (uint)c0;
            bitsLeft += 8;
            if (c0 == 0xFF)
            {
                pos++;
                if (c1 != 0)
                {
                    D.UnreadMarker = c1;
                    pos -= 2;
                    getBuffer &= ~0xFFUL;
                }
            }
        }
    }

    /// <summary>HUFF_DECODE_FAST</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HuffDecodeFast(ref ulong getBuffer, ref int bitsLeft, byte[] buffer, ref int pos, DerivedHuffmanTable table)
    {
        FillFast(ref getBuffer, ref bitsLeft, buffer, ref pos);
        var s = table.Lookup[(int)(getBuffer >> (bitsLeft - DerivedHuffmanTable.Lookahead)) & 0xFF];
        var nb = s >> DerivedHuffmanTable.Lookahead;
        bitsLeft -= nb;
        s &= 0xFF;
        if (nb > DerivedHuffmanTable.Lookahead)
        {
            // jpeg_huff_decode inline, from the bits just dropped.
            s = (int)(getBuffer >> bitsLeft) & ((1 << nb) - 1);
            while (s > table.MaxCode[nb])
            {
                s <<= 1;
                s |= (int)(getBuffer >> --bitsLeft) & 1;
                nb++;
            }
            s = nb > 16 ? 0 : table.Values[(s + table.ValOffset[nb]) & 0xFF];
        }
        return s;
    }
}
