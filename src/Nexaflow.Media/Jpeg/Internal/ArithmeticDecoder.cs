/*
 * Ported from libjpeg-turbo 3.1.1, src/jdarith.c:
 * This file was part of the Independent JPEG Group's software:
 * Developed 1997-2015 by Guido Vollbeding.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2015-2020, 2022, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Arithmetic decoding, sequential and progressive: libjpeg's jdarith.c. A marker met mid-segment is legal here; zeros
/// are supplied after it until decoding is done. A code that overflows is warned of and the rest of the segment left
/// as it is.
/// </summary>
internal sealed class ArithmeticDecoder : EntropyDecoder
{
    private const int DcStatBins = 64;
    private const int AcStatBins = 256;

    private enum Pass { Sequential, DcFirst, AcFirst, DcRefine, AcRefine }

    // The C register (the base of the coding interval and the input bit buffer), the A register (the interval's
    // size) and the bit shift counter: -16 to read the two initial bytes, 0..7 running, -1 after an error.
    private int _c, _a, _ct;
    private readonly int[] _lastDcVal = new int[Decompressor.MaxCompsInScan];
    private readonly int[] _dcContext = new int[Decompressor.MaxCompsInScan];
    private int _restartsToGo;
    private readonly byte[]?[] _dcStats = new byte[]?[16];
    private readonly byte[]?[] _acStats = new byte[]?[16];

    /// <summary>The bin for the fixed 0.5 estimate; its state never moves.</summary>
    private readonly byte[] _fixedBin = [JpegTables.FixedProbabilityState, 0, 0, 0];

    private Pass _pass;

    /// <summary>3.1.1 - jinit_arith_decoder</summary>
    public ArithmeticDecoder(Decompressor decompressor) : base(decompressor)
    {
        if (D.Progressive) D.CoefBits = ProgressiveHuffmanDecoder.CreateCoefBits(D.NumComponents);
    }

    /// <summary>3.1.1 - start_pass</summary>
    public override void StartPass()
    {
        if (D.Progressive)
        {
            ProgressiveHuffmanDecoder.UpdateProgression(D);
            _pass = D.Ah == 0 ? D.Ss == 0 ? Pass.DcFirst : Pass.AcFirst : D.Ss == 0 ? Pass.DcRefine : Pass.AcRefine;
        }
        else
        {
            if (D.Ss != 0 || D.Se != 63 || D.Ah != 0 || D.Al != 0) D.Warnings.Add(JpegErrors.NotSequential);
            _pass = Pass.Sequential;
        }

        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var component = D.CurComp[ci]!;
            if (!D.Progressive || (D.Ss == 0 && D.Ah == 0))
            {
                var tbl = component.DcTableNo;
                if (tbl is < 0 or >= 16) throw JpegErrors.Fatal(JpegErrors.NoArithTable(tbl));
                Array.Clear(_dcStats[tbl] ??= new byte[DcStatBins]);
                _lastDcVal[ci] = 0;
                _dcContext[ci] = 0;
            }
            if (!D.Progressive || D.Ss != 0)
            {
                var tbl = component.AcTableNo;
                if (tbl is < 0 or >= 16) throw JpegErrors.Fatal(JpegErrors.NoArithTable(tbl));
                Array.Clear(_acStats[tbl] ??= new byte[AcStatBins]);
            }
        }

        _c = 0;
        _a = 0;
        _ct = -16;
        InsufficientData = false;
        _restartsToGo = D.RestartInterval;
    }

    /// <summary>3.1.1 - get_byte</summary>
    private int GetByte()
    {
        var source = D.Source;
        if (source.Position == source.Limit) source.Fill();
        return source.Buffer[source.Position++];
    }

    /// <summary>
    /// 3.1.1 - arith_decode: one binary decision in the context <paramref name="st"/> (Annex D.2), its state moved on.
    /// </summary>
    private int Decode(ref byte st)
    {
        // Renormalization and data input, section D.2.6
        while (_a < 0x8000)
        {
            if (--_ct < 0)
            {
                int data;
                if (D.UnreadMarker != 0)
                {
                    data = 0;
                }
                else
                {
                    data = GetByte();
                    if (data == 0xFF)
                    {
                        do data = GetByte();
                        while (data == 0xFF);
                        if (data == 0)
                        {
                            data = 0xFF;
                        }
                        else
                        {
                            D.UnreadMarker = data;
                            data = 0;
                        }
                    }
                }
                _c = (_c << 8) | data;
                if ((_ct += 8) < 0 && ++_ct == 0)
                    _a = 0x8000;   // two initial bytes read: becomes 0x10000 below
            }
            _a <<= 1;
        }

        int sv = st;
        var qe = JpegTables.ArithmeticStates[sv & 0x7F];
        var nl = (byte)(qe & 0xFF);
        qe >>= 8;
        var nm = (byte)(qe & 0xFF);
        qe >>= 8;

        // Decoding and estimation, sections D.2.4 and D.2.5
        var temp = _a - qe;
        _a = temp;
        temp <<= _ct;
        if (_c >= temp)
        {
            _c -= temp;
            // Conditional LPS exchange
            if (_a < qe)
            {
                _a = qe;
                st = (byte)((sv & 0x80) ^ nm);
            }
            else
            {
                _a = qe;
                st = (byte)((sv & 0x80) ^ nl);
                sv ^= 0x80;
            }
        }
        else if (_a < 0x8000)
        {
            // Conditional MPS exchange
            if (_a < qe)
            {
                st = (byte)((sv & 0x80) ^ nl);
                sv ^= 0x80;
            }
            else
            {
                st = (byte)((sv & 0x80) ^ nm);
            }
        }

        return sv >> 7;
    }

    /// <summary>3.1.1 - process_restart</summary>
    private void ProcessRestart()
    {
        D.ReadRestartMarker();

        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var component = D.CurComp[ci]!;
            if (!D.Progressive || (D.Ss == 0 && D.Ah == 0))
            {
                Array.Clear(_dcStats[component.DcTableNo]!);
                _lastDcVal[ci] = 0;
                _dcContext[ci] = 0;
            }
            if (!D.Progressive || D.Ss != 0) Array.Clear(_acStats[component.AcTableNo]!);
        }

        _c = 0;
        _a = 0;
        _ct = -16;
        _restartsToGo = D.RestartInterval;
    }

    public override void DecodeMcu(McuBlocks mcu)
    {
        if (D.RestartInterval != 0)
        {
            if (_restartsToGo == 0) ProcessRestart();
            _restartsToGo--;
        }

        switch (_pass)
        {
            case Pass.Sequential: DecodeSequential(mcu); break;
            case Pass.DcFirst: DecodeDcFirst(mcu); break;
            case Pass.AcFirst: DecodeAcFirst(mcu); break;
            case Pass.DcRefine: DecodeDcRefine(mcu); break;
            default: DecodeAcRefine(mcu); break;
        }
    }

    /// <summary>
    /// Figures F.19 and F.21-F.24: a DC difference, added to the component's last DC value, which is returned. False
    /// when the magnitude overflows, which is warned of and stops the segment.
    /// </summary>
    private bool DecodeDc(int ci, int tbl, out int dc)
    {
        var stats = _dcStats[tbl]!;
        var st = _dcContext[ci];   // Table F.4: statistics bin S0

        if (Decode(ref stats[st]) == 0)
        {
            _dcContext[ci] = 0;
        }
        else
        {
            var sign = Decode(ref stats[st + 1]);
            st += 2 + sign;
            int m;
            if ((m = Decode(ref stats[st])) != 0)
            {
                st = 20;   // Table F.4: X1 = 20
                while (Decode(ref stats[st]) != 0)
                {
                    if ((m <<= 1) == 0x8000)
                    {
                        D.Warnings.Add(JpegErrors.ArithBadCode);
                        _ct = -1;
                        dc = 0;
                        return false;
                    }
                    st++;
                }
            }

            // Section F.1.4.4.1.2: the conditioning category for the next DC
            if (m < (int)((1L << D.ArithDcL[tbl]) >> 1))
                _dcContext[ci] = 0;
            else if (m > (int)((1L << D.ArithDcU[tbl]) >> 1))
                _dcContext[ci] = 12 + sign * 4;
            else
                _dcContext[ci] = 4 + sign * 4;

            var v = m;
            st += 14;
            while ((m >>= 1) != 0)
                if (Decode(ref stats[st]) != 0) v |= m;
            v += 1;
            if (sign != 0) v = -v;
            _lastDcVal[ci] = (_lastDcVal[ci] + v) & 0xffff;
        }

        dc = _lastDcVal[ci];
        return true;
    }

    /// <summary>
    /// Figures F.20-F.24: the AC coefficients from <paramref name="ss"/> to <paramref name="se"/>, each shifted left
    /// by <paramref name="al"/>. False when the code overflows, which is warned of and stops the segment.
    /// </summary>
    private bool DecodeAc(Span<short> block, int tbl, int ss, int se, int al)
    {
        var stats = _acStats[tbl]!;
        var order = JpegTables.NaturalOrder;

        for (var k = ss; k <= se; k++)
        {
            var st = 3 * (k - 1);
            if (Decode(ref stats[st]) != 0) break;   // EOB flag
            while (Decode(ref stats[st + 1]) == 0)
            {
                st += 3;
                k++;
                if (k > se)
                {
                    D.Warnings.Add(JpegErrors.ArithBadCode);
                    _ct = -1;   // spectral overflow
                    return false;
                }
            }

            var sign = Decode(ref _fixedBin[0]);
            st += 2;
            int m;
            if ((m = Decode(ref stats[st])) != 0)
            {
                if (Decode(ref stats[st]) != 0)
                {
                    m <<= 1;
                    st = k <= D.ArithAcK[tbl] ? 189 : 217;
                    while (Decode(ref stats[st]) != 0)
                    {
                        if ((m <<= 1) == 0x8000)
                        {
                            D.Warnings.Add(JpegErrors.ArithBadCode);
                            _ct = -1;   // magnitude overflow
                            return false;
                        }
                        st++;
                    }
                }
            }

            var v = m;
            st += 14;
            while ((m >>= 1) != 0)
                if (Decode(ref stats[st]) != 0) v |= m;
            v += 1;
            if (sign != 0) v = -v;
            block[order[k]] = (short)((uint)v << al);
        }
        return true;
    }

    /// <summary>3.1.1 - decode_mcu</summary>
    private void DecodeSequential(McuBlocks mcu)
    {
        if (_ct == -1) return;   // after an error, nothing

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var ci = D.McuMembership[blkn];
            var component = D.CurComp[ci]!;
            var block = mcu.Arrays[blkn].AsSpan(mcu.Offsets[blkn], 64);

            if (!DecodeDc(ci, component.DcTableNo, out var dc)) return;
            block[0] = (short)dc;
            if (!DecodeAc(block, component.AcTableNo, 1, 63, 0)) return;
        }
    }

    /// <summary>3.1.1 - decode_mcu_DC_first</summary>
    private void DecodeDcFirst(McuBlocks mcu)
    {
        if (_ct == -1) return;

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var ci = D.McuMembership[blkn];
            if (!DecodeDc(ci, D.CurComp[ci]!.DcTableNo, out var dc)) return;
            mcu.Arrays[blkn][mcu.Offsets[blkn]] = (short)(dc << D.Al);
        }
    }

    /// <summary>3.1.1 - decode_mcu_AC_first</summary>
    private void DecodeAcFirst(McuBlocks mcu)
    {
        if (_ct == -1) return;
        DecodeAc(mcu.Arrays[0].AsSpan(mcu.Offsets[0], 64), D.CurComp[0]!.AcTableNo, D.Ss, D.Se, D.Al);
    }

    /// <summary>3.1.1 - decode_mcu_DC_refine</summary>
    private void DecodeDcRefine(McuBlocks mcu)
    {
        var p1 = 1 << D.Al;
        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            if (Decode(ref _fixedBin[0]) != 0)
            {
                ref var dc = ref mcu.Arrays[blkn][mcu.Offsets[blkn]];
                dc |= (short)p1;
            }
        }
    }

    /// <summary>3.1.1 - decode_mcu_AC_refine</summary>
    private void DecodeAcRefine(McuBlocks mcu)
    {
        if (_ct == -1) return;

        var block = mcu.Arrays[0].AsSpan(mcu.Offsets[0], 64);
        var stats = _acStats[D.CurComp[0]!.AcTableNo]!;
        var order = JpegTables.NaturalOrder;
        var p1 = 1 << D.Al;
        var m1 = -1 << D.Al;

        // EOBx: the previous stage's end of block
        int kex;
        for (kex = D.Se; kex > 0; kex--)
            if (block[order[kex]] != 0) break;

        for (var k = D.Ss; k <= D.Se; k++)
        {
            var st = 3 * (k - 1);
            if (k > kex && Decode(ref stats[st]) != 0) break;   // EOB flag
            for (;;)
            {
                ref var thiscoef = ref block[order[k]];
                if (thiscoef != 0)
                {
                    // A previously nonzero coefficient
                    if (Decode(ref stats[st + 2]) != 0)
                        thiscoef = (short)(thiscoef < 0 ? thiscoef + m1 : thiscoef + p1);
                    break;
                }
                if (Decode(ref stats[st + 1]) != 0)
                {
                    // A newly nonzero coefficient
                    thiscoef = (short)(Decode(ref _fixedBin[0]) != 0 ? m1 : p1);
                    break;
                }
                st += 3;
                k++;
                if (k > D.Se)
                {
                    D.Warnings.Add(JpegErrors.ArithBadCode);
                    _ct = -1;   // spectral overflow
                    return;
                }
            }
        }
    }
}
