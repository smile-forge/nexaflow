/*
 * Ported from libjpeg-turbo 3.1.1, src/jdcoefct.c (smoothing_ok, decompress_smooth_data):
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1994-1997, Thomas G. Lane.
 * libjpeg-turbo Modifications:
 * Copyright 2009 Pierre Ossman <ossman@cendio.se> for Cendio AB
 * Copyright (C) 2010, 2015-2016, 2019-2020, 2022-2024, D. R. Commander.
 * Copyright (C) 2015, 2020, Google, Inc.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Interblock smoothing of a progressive image whose low-frequency coefficients are not all known to full precision —
/// one whose data ran out, or that never sent them: the first nine AC coefficients of a block, while still zero and not
/// known exactly, are estimated from the DC values of it and its 24 neighbours, and with no AC data at all the DC is
/// interpolated too.
/// </summary>
internal sealed class BlockSmoothing
{
    private const int SavedCoefs = 10;

    // Natural-order positions of the first nine zigzag-order coefficients.
    private const int Q01 = 1, Q10 = 8, Q20 = 16, Q11 = 9, Q02 = 2, Q03 = 3, Q12 = 10, Q21 = 17, Q30 = 24;

    private readonly Decompressor _d;

    /// <summary>coef_bits_latch: each component's coef_bits[0..9], and after them the previous scan's.</summary>
    private readonly int[] _latch;

    private BlockSmoothing(Decompressor d, int[] latch)
    {
        _d = d;
        _latch = latch;
    }

    /// <summary>
    /// 3.1.1 - smoothing_ok: smoothing, when it applies and is safe — a progressive image, every component's table
    /// latched with its DC and first nine AC quantizers nonzero, its DC at least partly known, and some of those AC
    /// coefficients still inexact. The coefficients' precisions are latched here.
    /// </summary>
    public static BlockSmoothing? For(Decompressor d)
    {
        if (!d.Progressive || d.CoefBits is null) return null;

        var n = d.NumComponents;
        var latch = new int[n * 2 * SavedCoefs];
        var useful = false;
        for (var ci = 0; ci < n; ci++)
        {
            var qtable = d.Components[ci].QuantTable;
            if (qtable is null) return null;
            if (qtable[0] == 0 || qtable[Q01] == 0 || qtable[Q10] == 0 || qtable[Q20] == 0 || qtable[Q11] == 0 ||
                qtable[Q02] == 0 || qtable[Q03] == 0 || qtable[Q12] == 0 || qtable[Q21] == 0 || qtable[Q30] == 0)
                return null;

            var coefBits = d.CoefBits[ci];
            var previous = d.CoefBits[ci + n];
            if (coefBits[0] < 0) return null;
            latch[ci * SavedCoefs] = coefBits[0];
            for (var coefi = 1; coefi < SavedCoefs; coefi++)
            {
                latch[(n + ci) * SavedCoefs + coefi] = d.InputScanNumber > 1 ? previous[coefi] : -1;
                latch[ci * SavedCoefs + coefi] = coefBits[coefi];
                if (coefBits[coefi] != 0) useful = true;
            }
        }

        return useful ? new BlockSmoothing(d, latch) : null;
    }

    /// <summary>
    /// 3.1.1 - decompress_smooth_data, for one block row of one component: each block smoothed into a copy, then
    /// transformed into <paramref name="plane"/>.
    /// </summary>
    public void Row(JpegComponent component, short[] multipliers, int blockRowIndex, Span<short> workspace)
    {
        var d = _d;
        var ci = component.Index;
        var vsamp = component.VSamp;
        var outputIMcuRow = blockRowIndex / vsamp;
        var blockRow = blockRowIndex % vsamp;
        var lastIMcuRow = d.TotalIMcuRows - 1;

        // Non-dummy block rows in this iMCU row; the last iMCU row's count also scales the image's (as libjpeg has it).
        int blockRows;
        if (outputIMcuRow < lastIMcuRow)
        {
            blockRows = vsamp;
        }
        else
        {
            blockRows = component.HeightInBlocks % vsamp;
            if (blockRows == 0) blockRows = vsamp;
        }

        // An iMCU row the scan never reached uses the previous scan's precisions.
        var bits = _latch.AsSpan((outputIMcuRow > d.LastGoodIMcuRow ? ci + d.NumComponents : ci) * SavedCoefs, SavedCoefs);
        var changeDc = bits[1] == -1 && bits[2] == -1 && bits[3] == -1 && bits[4] == -1 && bits[5] == -1 &&
                       bits[6] == -1 && bits[7] == -1 && bits[8] == -1 && bits[9] == -1;

        var qtbl = component.QuantTable!;
        int q00 = qtbl[0], q01 = qtbl[Q01], q10 = qtbl[Q10], q20 = qtbl[Q20], q11 = qtbl[Q11], q02 = qtbl[Q02];
        int q03 = 0, q12 = 0, q21 = 0, q30 = 0;
        if (changeDc)
        {
            q03 = qtbl[Q03];
            q12 = qtbl[Q12];
            q21 = qtbl[Q21];
            q30 = qtbl[Q30];
        }

        var imageBlockRows = blockRows * d.TotalIMcuRows;
        var imageBlockRow = outputIMcuRow * blockRows + blockRow;
        var current = blockRowIndex;
        var prev = imageBlockRow > 0 ? current - 1 : current;
        var prevPrev = imageBlockRow > 1 ? current - 2 : prev;
        var next = imageBlockRow < imageBlockRows - 1 ? current + 1 : current;
        var nextNext = imageBlockRow < imageBlockRows - 2 ? current + 2 : next;

        var coefficients = component.Coefficients;
        var wide = component.BlocksWide;
        int Dc(int row, int col) => coefficients[(row * wide + col) * 64];

        // The surrounding DC values, by sliding registers; all 25 start so as to be right on narrow images.
        int dc01, dc02, dc03, dc04, dc05, dc06, dc07, dc08, dc09, dc10, dc11, dc12, dc13;
        int dc14, dc15, dc16, dc17, dc18, dc19, dc20, dc21, dc22, dc23, dc24, dc25;
        dc01 = dc02 = dc03 = dc04 = dc05 = Dc(prevPrev, 0);
        dc06 = dc07 = dc08 = dc09 = dc10 = Dc(prev, 0);
        dc11 = dc12 = dc13 = dc14 = dc15 = Dc(current, 0);
        dc16 = dc17 = dc18 = dc19 = dc20 = Dc(next, 0);
        dc21 = dc22 = dc23 = dc24 = dc25 = Dc(nextNext, 0);

        var lastBlockColumn = component.WidthInBlocks - 1;
        var plane = component.Plane;
        var stride = component.PlaneStride;
        for (var blockNum = 0; blockNum <= lastBlockColumn; blockNum++)
        {
            coefficients.AsSpan((current * wide + blockNum) * 64, 64).CopyTo(workspace);

            if (blockNum == 0 && blockNum < lastBlockColumn)
            {
                dc04 = dc05 = Dc(prevPrev, 1);
                dc09 = dc10 = Dc(prev, 1);
                dc14 = dc15 = Dc(current, 1);
                dc19 = dc20 = Dc(next, 1);
                dc24 = dc25 = Dc(nextNext, 1);
            }
            if (blockNum + 1 < lastBlockColumn)
            {
                dc05 = Dc(prevPrev, blockNum + 2);
                dc10 = Dc(prev, blockNum + 2);
                dc15 = Dc(current, blockNum + 2);
                dc20 = Dc(next, blockNum + 2);
                dc25 = Dc(nextNext, blockNum + 2);
            }

            // An estimate applies only to a coefficient still zero and not known to be exact. With DC interpolation,
            // a Gaussian-like kernel; without, ITU-T T.81 K.8 over a 5x5 window.
            int al;
            if ((al = bits[1]) != 0 && workspace[1] == 0)
            {
                var num = q00 * (changeDc
                    ? -dc01 - dc02 + dc04 + dc05 - 3 * dc06 + 13 * dc07 - 13 * dc09 + 3 * dc10 - 3 * dc11 + 38 * dc12 -
                      38 * dc14 + 3 * dc15 - 3 * dc16 + 13 * dc17 - 13 * dc19 + 3 * dc20 - dc21 - dc22 + dc24 + dc25
                    : -7 * dc11 + 50 * dc12 - 50 * dc14 + 7 * dc15);
                workspace[1] = Predict(num, q01, al);
            }
            if ((al = bits[2]) != 0 && workspace[8] == 0)
            {
                var num = q00 * (changeDc
                    ? -dc01 - 3 * dc02 - 3 * dc03 - 3 * dc04 - dc05 - dc06 + 13 * dc07 + 38 * dc08 + 13 * dc09 - dc10 +
                      dc16 - 13 * dc17 - 38 * dc18 - 13 * dc19 + dc20 + dc21 + 3 * dc22 + 3 * dc23 + 3 * dc24 + dc25
                    : -7 * dc03 + 50 * dc08 - 50 * dc18 + 7 * dc23);
                workspace[8] = Predict(num, q10, al);
            }
            if ((al = bits[3]) != 0 && workspace[16] == 0)
            {
                var num = q00 * (changeDc
                    ? dc03 + 2 * dc07 + 7 * dc08 + 2 * dc09 - 5 * dc12 - 14 * dc13 - 5 * dc14 + 2 * dc17 + 7 * dc18 +
                      2 * dc19 + dc23
                    : -dc03 + 13 * dc08 - 24 * dc13 + 13 * dc18 - dc23);
                workspace[16] = Predict(num, q20, al);
            }
            if ((al = bits[4]) != 0 && workspace[9] == 0)
            {
                var num = q00 * (changeDc
                    ? -dc01 + dc05 + 9 * dc07 - 9 * dc09 - 9 * dc17 + 9 * dc19 + dc21 - dc25
                    : dc10 + dc16 - 10 * dc17 + 10 * dc19 - dc02 - dc20 + dc22 - dc24 + dc04 - dc06 + 10 * dc07 -
                      10 * dc09);
                workspace[9] = Predict(num, q11, al);
            }
            if ((al = bits[5]) != 0 && workspace[2] == 0)
            {
                var num = q00 * (changeDc
                    ? 2 * dc07 - 5 * dc08 + 2 * dc09 + dc11 + 7 * dc12 - 14 * dc13 + 7 * dc14 + dc15 + 2 * dc17 -
                      5 * dc18 + 2 * dc19
                    : -dc11 + 13 * dc12 - 24 * dc13 + 13 * dc14 - dc15);
                workspace[2] = Predict(num, q02, al);
            }
            if (changeDc)
            {
                if ((al = bits[6]) != 0 && workspace[3] == 0)
                    workspace[3] = Predict(q00 * (dc07 - dc09 + 2 * dc12 - 2 * dc14 + dc17 - dc19), q03, al);
                if ((al = bits[7]) != 0 && workspace[10] == 0)
                    workspace[10] = Predict(q00 * (dc07 - 3 * dc08 + dc09 - dc17 + 3 * dc18 - dc19), q12, al);
                if ((al = bits[8]) != 0 && workspace[17] == 0)
                    workspace[17] = Predict(q00 * (dc07 - dc09 - 3 * dc12 + 3 * dc14 + dc17 - dc19), q21, al);
                if ((al = bits[9]) != 0 && workspace[24] == 0)
                    workspace[24] = Predict(q00 * (dc07 + 2 * dc08 + dc09 - dc17 - 2 * dc18 - dc19), q30, al);

                // coef_bits[0] is not negative, or this would not be called.
                var num = q00 *
                          (-2 * dc01 - 6 * dc02 - 8 * dc03 - 6 * dc04 - 2 * dc05 - 6 * dc06 + 6 * dc07 + 42 * dc08 +
                           6 * dc09 - 6 * dc10 - 8 * dc11 + 42 * dc12 + 152 * dc13 + 42 * dc14 - 8 * dc15 - 6 * dc16 +
                           6 * dc17 + 42 * dc18 + 6 * dc19 - 6 * dc20 - 2 * dc21 - 6 * dc22 - 8 * dc23 - 6 * dc24 -
                           2 * dc25);
                workspace[0] = (short)(num >= 0 ? ((q00 << 7) + num) / (q00 << 8) : -(((q00 << 7) - num) / (q00 << 8)));
            }

            InverseDct.Transform(workspace, multipliers, plane.AsSpan(current * 8 * stride + blockNum * 8), stride);

            dc01 = dc02; dc02 = dc03; dc03 = dc04; dc04 = dc05;
            dc06 = dc07; dc07 = dc08; dc08 = dc09; dc09 = dc10;
            dc11 = dc12; dc12 = dc13; dc13 = dc14; dc14 = dc15;
            dc16 = dc17; dc17 = dc18; dc18 = dc19; dc19 = dc20;
            dc21 = dc22; dc22 = dc23; dc23 = dc24; dc24 = dc25;
        }
    }

    /// <summary>The estimate num / (q * 256), rounded, and kept below 2^Al when Al is positive.</summary>
    private static short Predict(int num, int q, int al)
    {
        int pred;
        if (num >= 0)
        {
            pred = ((q << 7) + num) / (q << 8);
            if (al > 0 && pred >= 1 << al) pred = (1 << al) - 1;
        }
        else
        {
            pred = ((q << 7) - num) / (q << 8);
            if (al > 0 && pred >= 1 << al) pred = (1 << al) - 1;
            pred = -pred;
        }
        return (short)pred;
    }
}
