/*
 * Ported from libjpeg-turbo 3.1.1, src/jdphuff.c:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1995-1997, Thomas G. Lane.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2015-2016, 2018-2022, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Progressive Huffman decoding: libjpeg's jdphuff.c — DC and AC first scans, and their successive-approximation
/// refinements. Each scan also records, per coefficient, the precision it leaves (<see cref="Decompressor.CoefBits"/>),
/// which block smoothing reads.
/// </summary>
internal sealed class ProgressiveHuffmanDecoder : HuffmanBitDecoder
{
    private enum Pass { DcFirst, AcFirst, DcRefine, AcRefine }

    private readonly DerivedHuffmanTable?[] _derived = new DerivedHuffmanTable?[4];
    private DerivedHuffmanTable? _acDerived;
    private readonly int[] _lastDcVal = new int[Decompressor.MaxCompsInScan];
    private uint _eobRun;
    private int _restartsToGo;
    private Pass _pass;

    /// <summary>3.1.1 - jinit_phuff_decoder: every coefficient starts unknown, -1.</summary>
    public ProgressiveHuffmanDecoder(Decompressor decompressor) : base(decompressor)
    {
        D.CoefBits = CreateCoefBits(D.NumComponents);
    }

    public static int[][] CreateCoefBits(int components)
    {
        var bits = new int[components * 2][];
        for (var ci = 0; ci < bits.Length; ci++)
        {
            bits[ci] = new int[64];
            if (ci < components) bits[ci].AsSpan().Fill(-1);
        }
        return bits;
    }

    /// <summary>
    /// The progression checks and bookkeeping of start_pass, shared with the arithmetic decoder: a malformed scan
    /// is fatal, one out of order is warned of, and each coefficient's precision is noted, the last scan's kept.
    /// </summary>
    public static void UpdateProgression(Decompressor d)
    {
        var isDcBand = d.Ss == 0;
        var bad = false;
        if (isDcBand)
        {
            if (d.Se != 0) bad = true;
        }
        else
        {
            if (d.Ss > d.Se || d.Se >= 64) bad = true;
            if (d.CompsInScan != 1) bad = true;   // AC scans may have only one component
        }
        if (d.Ah != 0 && d.Al != d.Ah - 1) bad = true;
        if (d.Al > 13) bad = true;
        if (bad) throw JpegErrors.Fatal(JpegErrors.BadProgression(d.Ss, d.Se, d.Ah, d.Al));

        var coefBits = d.CoefBits!;
        for (var ci = 0; ci < d.CompsInScan; ci++)
        {
            var cindex = d.CurComp[ci]!.Index;
            var bits = coefBits[cindex];
            var previous = coefBits[cindex + d.NumComponents];
            if (!isDcBand && bits[0] < 0) d.Warnings.Add(JpegErrors.BogusProgression(cindex, 0));
            for (var coefi = Math.Min(d.Ss, 1); coefi <= Math.Max(d.Se, 9); coefi++)
                previous[coefi] = d.InputScanNumber > 1 ? bits[coefi] : 0;
            for (var coefi = d.Ss; coefi <= d.Se; coefi++)
            {
                var expected = bits[coefi] < 0 ? 0 : bits[coefi];
                if (d.Ah != expected) d.Warnings.Add(JpegErrors.BogusProgression(cindex, coefi));
                bits[coefi] = d.Al;
            }
        }
    }

    /// <summary>3.1.1 - start_pass_phuff_decoder</summary>
    public override void StartPass()
    {
        var isDcBand = D.Ss == 0;
        UpdateProgression(D);

        _pass = D.Ah == 0 ? isDcBand ? Pass.DcFirst : Pass.AcFirst : isDcBand ? Pass.DcRefine : Pass.AcRefine;

        for (var ci = 0; ci < D.CompsInScan; ci++)
        {
            var component = D.CurComp[ci]!;
            if (isDcBand)
            {
                if (D.Ah == 0) DerivedHuffmanTable.Make(D.DcHuffTables, true, component.DcTableNo, false, _derived);
            }
            else
            {
                _acDerived = DerivedHuffmanTable.Make(D.AcHuffTables, false, component.AcTableNo, false, _derived);
            }
            _lastDcVal[ci] = 0;
        }

        BitsLeft = 0;
        BitBuffer = 0;
        InsufficientData = false;
        _eobRun = 0;
        _restartsToGo = D.RestartInterval;
    }

    /// <summary>3.1.1 - process_restart</summary>
    private void ProcessRestart()
    {
        ReadRestart();
        Array.Clear(_lastDcVal);
        _eobRun = 0;
        _restartsToGo = D.RestartInterval;
        if (D.UnreadMarker == 0) InsufficientData = false;
    }

    public override void DecodeMcu(McuBlocks mcu)
    {
        if (D.RestartInterval != 0 && _restartsToGo == 0) ProcessRestart();

        switch (_pass)
        {
            case Pass.DcFirst:
                if (!InsufficientData) DecodeDcFirst(mcu);
                break;
            case Pass.AcFirst:
                if (!InsufficientData) DecodeAcFirst(mcu);
                break;
            case Pass.DcRefine:
                // Zeros read past the data change nothing, so the flag is not checked.
                DecodeDcRefine(mcu);
                break;
            default:
                if (!InsufficientData) DecodeAcRefine(mcu);
                break;
        }

        if (D.RestartInterval != 0) _restartsToGo--;
    }

    /// <summary>3.1.1 - decode_mcu_DC_first</summary>
    private void DecodeDcFirst(McuBlocks mcu)
    {
        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var al = D.Al;

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            var ci = D.McuMembership[blkn];
            var table = _derived[D.CurComp[ci]!.DcTableNo]!;

            var s = HuffDecode(ref getBuffer, ref bitsLeft, table);
            if (s != 0)
            {
                var r = GetBits(ref getBuffer, ref bitsLeft, s);
                s = HuffExtend(r, s);
            }

            var last = _lastDcVal[ci];
            if ((last >= 0 && s > int.MaxValue - last) || (last < 0 && s < int.MinValue - last))
                throw JpegErrors.Fatal(JpegErrors.BadDctCoefficient);
            s += last;
            _lastDcVal[ci] = s;
            mcu.Arrays[blkn][mcu.Offsets[blkn]] = (short)(s << al);
        }

        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
    }

    /// <summary>3.1.1 - decode_mcu_AC_first</summary>
    private void DecodeAcFirst(McuBlocks mcu)
    {
        var eobRun = _eobRun;
        if (eobRun > 0)
        {
            // A band of zeros: nothing to do.
            _eobRun = eobRun - 1;
            return;
        }

        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var block = mcu.Arrays[0].AsSpan(mcu.Offsets[0], 64);
        var table = _acDerived!;
        var order = JpegTables.NaturalOrder;
        var se = D.Se;
        var al = D.Al;

        for (var k = D.Ss; k <= se; k++)
        {
            var s = HuffDecode(ref getBuffer, ref bitsLeft, table);
            var r = s >> 4;
            s &= 15;
            if (s != 0)
            {
                k += r;
                r = GetBits(ref getBuffer, ref bitsLeft, s);
                s = HuffExtend(r, s);
                block[order[k]] = (short)(s << al);
            }
            else if (r == 15)
            {
                k += 15;   // ZRL
            }
            else
            {
                // EOBr: a run of 2^r blocks plus r appended bits, this one among them.
                eobRun = 1u << r;
                if (r != 0) eobRun += (uint)GetBits(ref getBuffer, ref bitsLeft, r);
                eobRun--;
                break;
            }
        }

        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
        _eobRun = eobRun;
    }

    /// <summary>3.1.1 - decode_mcu_DC_refine: the next bit of each block's DC value.</summary>
    private void DecodeDcRefine(McuBlocks mcu)
    {
        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var p1 = 1 << D.Al;

        for (var blkn = 0; blkn < D.BlocksInMcu; blkn++)
        {
            if (GetBits(ref getBuffer, ref bitsLeft, 1) != 0)
            {
                ref var dc = ref mcu.Arrays[blkn][mcu.Offsets[blkn]];
                dc |= (short)p1;
            }
        }

        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
    }

    /// <summary>3.1.1 - decode_mcu_AC_refine</summary>
    private void DecodeAcRefine(McuBlocks mcu)
    {
        var getBuffer = BitBuffer;
        var bitsLeft = BitsLeft;
        var eobRun = _eobRun;
        var block = mcu.Arrays[0].AsSpan(mcu.Offsets[0], 64);
        var table = _acDerived!;
        var order = JpegTables.NaturalOrder;
        var se = D.Se;
        var p1 = 1 << D.Al;
        var m1 = -1 << D.Al;

        var k = D.Ss;
        if (eobRun == 0)
        {
            for (; k <= se; k++)
            {
                var s = HuffDecode(ref getBuffer, ref bitsLeft, table);
                var r = s >> 4;
                s &= 15;
                if (s != 0)
                {
                    if (s != 1) D.Warnings.Add(JpegErrors.HuffBadCode);   // a new coefficient's size is always 1
                    s = GetBits(ref getBuffer, ref bitsLeft, 1) != 0 ? p1 : m1;
                }
                else if (r != 15)
                {
                    eobRun = 1u << r;
                    if (r != 0) eobRun += (uint)GetBits(ref getBuffer, ref bitsLeft, r);
                    break;   // the rest of the block is the EOB run's
                }
                // (s is 0 for a ZRL)

                // Past already-nonzero coefficients and r still-zero ones, appending a correction bit to each
                // nonzero one: 1 when its magnitude must grow.
                do
                {
                    ref var thiscoef = ref block[order[k]];
                    if (thiscoef != 0)
                    {
                        if (GetBits(ref getBuffer, ref bitsLeft, 1) != 0 && (thiscoef & p1) == 0)
                            thiscoef = (short)(thiscoef >= 0 ? thiscoef + p1 : thiscoef + m1);
                    }
                    else if (--r < 0)
                    {
                        break;   // the zero coefficient sought
                    }
                    k++;
                }
                while (k <= se);

                if (s != 0) block[order[k]] = (short)s;
            }
        }

        if (eobRun > 0)
        {
            // The rest of the band after the end of block: a correction bit for each already-nonzero coefficient.
            for (; k <= se; k++)
            {
                ref var thiscoef = ref block[order[k]];
                if (thiscoef != 0 && GetBits(ref getBuffer, ref bitsLeft, 1) != 0 && (thiscoef & p1) == 0)
                    thiscoef = (short)(thiscoef >= 0 ? thiscoef + p1 : thiscoef + m1);
            }
            eobRun--;
        }

        BitBuffer = getBuffer;
        BitsLeft = bitsLeft;
        _eobRun = eobRun;
    }
}
