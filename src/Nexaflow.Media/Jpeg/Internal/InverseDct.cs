/*
 * Ported from libjpeg-turbo 3.1.1, simd/x86_64/jidctint-avx2.asm and src/jidctint.c (jpeg_idct_islow):
 * Copyright 2009 Pierre Ossman <ossman@cendio.se> for Cendio AB
 * Copyright (C) 2009, 2016, 2018, 2020, 2024, D. R. Commander.
 * Copyright (C) 2018, Matthias Räncker.
 * Based on the x86 SIMD extension for IJG JPEG library
 * Copyright (C) 1999-2006, MIYASAKA Masaru.
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1998, Thomas G. Lane.
 * Modification developed 2002-2018 by Guido Vollbeding.
 * libjpeg-turbo Modifications:
 * Copyright (C) 2015, 2020, 2022, D. R. Commander.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// Dequantization and the accurate integer inverse DCT (libjpeg's JDCT_ISLOW, the default), an 8x8 block at a time,
/// computed as libjpeg-turbo computes it on x86: jsimd_idct_islow_avx2, instruction for instruction.
/// </summary>
/// <remarks>
/// That differs from jidctint.c's C only on coefficients no valid image holds. It works in 16-bit lanes: the
/// dequantizing product keeps its low 16 bits, the sums in0±in4, in7+in3 and in5+in1 wrap at 16 bits, each pass's
/// outputs saturate to 16 bits, and the final range limit clamps where the C wraps through a masked table. libjpeg-turbo
/// as built for x86 — the library Pillow ships, and what Windows' codec agrees with — produces these values, so they are
/// the ones to match. Where AVX2 is absent, <see cref="TransformScalar"/> does the same arithmetic a value at a time.
/// </remarks>
internal static class InverseDct
{
    private const int ConstBits = 13;
    private const int Pass1Bits = 2;
    private const int DescalePass1 = ConstBits - Pass1Bits;
    private const int DescalePass2 = ConstBits + Pass1Bits + 3;

    private const short F0298 = 2446;   // FIX(0.298631336)
    private const short F0390 = 3196;   // FIX(0.390180644)
    private const short F0541 = 4433;   // FIX(0.541196100)
    private const short F0765 = 6270;   // FIX(0.765366865)
    private const short F0899 = 7373;   // FIX(0.899976223)
    private const short F1175 = 9633;   // FIX(1.175875602)
    private const short F1501 = 12299;  // FIX(1.501321110)
    private const short F1847 = 15137;  // FIX(1.847759065)
    private const short F1961 = 16069;  // FIX(1.961570560)
    private const short F2053 = 16819;  // FIX(2.053119869)
    private const short F2562 = 20995;  // FIX(2.562915447)
    private const short F3072 = 25172;  // FIX(3.072711026)

    // The SIMD code's constant pairs, its low lane first: pmaddwd multiplies each pair of 16-bit lanes by one and
    // adds the products.
    private static readonly Vector256<short> PwF130F054MF130F054 = Pairs(F0541 + F0765, F0541, F0541 - F1847, F0541);
    private static readonly Vector256<short> PwMF078F117F078F117 = Pairs(F1175 - F1961, F1175, F1175 - F0390, F1175);
    private static readonly Vector256<short> PwMF060MF089MF050MF256 = Pairs(F0298 - F0899, -F0899, F2053 - F2562, -F2562);
    private static readonly Vector256<short> PwMF089F060MF256F050 = Pairs(-F0899, F1501 - F0899, -F2562, F3072 - F2562);
    private static readonly Vector256<short> Pw1Neg1 = Vector256.Create(Vector128.Create((short)1), Vector128.Create((short)-1));

    private static Vector256<short> Pairs(int low0, int low1, int high0, int high1) =>
        Vector256.Create(
            Vector128.Create((short)low0, (short)low1, (short)low0, (short)low1, (short)low0, (short)low1, (short)low0, (short)low1),
            Vector128.Create((short)high0, (short)high1, (short)high0, (short)high1, (short)high0, (short)high1, (short)high0, (short)high1));

    /// <summary>
    /// The multipliers for a quantization table: its values as libjpeg's SIMD build holds them, 16-bit
    /// (ISLOW_MULT_TYPE is short).
    /// </summary>
    public static short[] Multipliers(ushort[]? table)
    {
        var multipliers = new short[64];
        if (table is null) return multipliers;   // never latched: all zero, so every sample is 128
        for (var i = 0; i < 64; i++) multipliers[i] = (short)table[i];
        return multipliers;
    }

    /// <summary>Dequantizes and transforms one block into eight rows of <paramref name="output"/>.</summary>
    public static void Transform(ReadOnlySpan<short> block, ReadOnlySpan<short> multipliers, Span<byte> output, int stride)
    {
        if (Avx2.IsSupported) TransformAvx2(block, multipliers, output, stride);
        else TransformScalar(block, multipliers, output, stride);
    }

    /// <summary>3.1.1 - jsimd_idct_islow_avx2: two rows (or columns) in each register.</summary>
    private static void TransformAvx2(ReadOnlySpan<short> block, ReadOnlySpan<short> multipliers, Span<byte> output, int stride)
    {
        block = block[..64];
        multipliers = multipliers[..64];

        var rows01 = Vector256.Create(block);
        var rows23 = Vector256.Create(block[16..]);
        var rows45 = Vector256.Create(block[32..]);
        var rows67 = Vector256.Create(block[48..]);

        Vector256<short> data04, data15, data26, data37;

        // Pass 1: columns. When rows 1 to 7 are all zero, each column is its dequantized DC shifted up.
        if ((rows01.GetUpper() | rows23.GetLower() | rows23.GetUpper() | rows45.GetLower() | rows45.GetUpper() |
             rows67.GetLower() | rows67.GetUpper()) == Vector128<short>.Zero)
        {
            var dc = Sse2.MultiplyLow(rows01.GetLower(), Vector128.Create(multipliers)) << Pass1Bits;
            data04 = Vector256.Create(Vector128.Create(dc.GetElement(0)), Vector128.Create(dc.GetElement(4)));
            data15 = Vector256.Create(Vector128.Create(dc.GetElement(1)), Vector128.Create(dc.GetElement(5)));
            data26 = Vector256.Create(Vector128.Create(dc.GetElement(2)), Vector128.Create(dc.GetElement(6)));
            data37 = Vector256.Create(Vector128.Create(dc.GetElement(3)), Vector128.Create(dc.GetElement(7)));
        }
        else
        {
            rows01 = Avx2.MultiplyLow(rows01, Vector256.Create(multipliers));
            rows23 = Avx2.MultiplyLow(rows23, Vector256.Create(multipliers[16..]));
            rows45 = Avx2.MultiplyLow(rows45, Vector256.Create(multipliers[32..]));
            rows67 = Avx2.MultiplyLow(rows67, Vector256.Create(multipliers[48..]));

            var in04 = Avx2.Permute2x128(rows01, rows45, 0x20);
            var in31 = Avx2.Permute2x128(rows23, rows01, 0x31);
            var in26 = Avx2.Permute2x128(rows23, rows67, 0x20);
            var in75 = Avx2.Permute2x128(rows67, rows45, 0x31);

            Dodct(in04, in31, in26, in75, firstPass: true, out var d01, out var d32, out var d45, out var d76);
            Transpose(d01, d32, d45, d76, out data04, out data15, out data26, out data37);
        }

        // Pass 2: rows.
        var in75b = Avx2.Permute2x128(data37, data15, 0x31);
        var in31b = Avx2.Permute2x128(data37, data15, 0x20);
        Dodct(data04, in31b, data26, in75b, firstPass: false, out var e01, out var e32, out var e45, out var e76);
        Transpose(e01, e32, e45, e76, out var out04, out var out15, out var out26, out var out37);

        // vpacksswb and the add of CENTERJSAMPLE: each sample clamped to a signed byte, then moved to 0..255.
        var centre = Vector256.Create((byte)128);
        var rows0145 = (Avx2.PackSignedSaturate(out04, out15).AsByte() + centre).AsUInt64();
        var rows2367 = (Avx2.PackSignedSaturate(out26, out37).AsByte() + centre).AsUInt64();

        Write(output, 0, rows0145.GetElement(0));
        Write(output, stride, rows0145.GetElement(1));
        Write(output, 2 * stride, rows2367.GetElement(0));
        Write(output, 3 * stride, rows2367.GetElement(1));
        Write(output, 4 * stride, rows0145.GetElement(2));
        Write(output, 5 * stride, rows0145.GetElement(3));
        Write(output, 6 * stride, rows2367.GetElement(2));
        Write(output, 7 * stride, rows2367.GetElement(3));

        static void Write(Span<byte> output, int at, ulong row) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(at, 8), row);
    }

    /// <summary>
    /// DODCT: the 1-D transform of four register pairs — in0_4, in3_1, in2_6 and in7_5 — into data0_1, data3_2,
    /// data4_5 and data7_6.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Dodct(Vector256<short> in04, Vector256<short> in31, Vector256<short> in26, Vector256<short> in75,
        bool firstPass, out Vector256<short> d01, out Vector256<short> d32, out Vector256<short> d45, out Vector256<short> d76)
    {
        // Even part: tmp3 and tmp2 as z2 * c + z3 * c', and in0 ± in4 at 16 bits, shifted into the high word.
        var in62 = Avx2.Permute2x128(in26, in26, 0x01);
        var tmp32Low = Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(in26, in62), PwF130F054MF130F054);
        var tmp32High = Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(in26, in62), PwF130F054MF130F054);

        var in40 = Avx2.Permute2x128(in04, in04, 0x01);
        var sums = in40 + Avx2.Sign(in04, Pw1Neg1);
        var tmp01Low = Avx2.ShiftRightArithmetic(Avx2.UnpackLow(Vector256<short>.Zero, sums).AsInt32(), 16 - ConstBits);
        var tmp01High = Avx2.ShiftRightArithmetic(Avx2.UnpackHigh(Vector256<short>.Zero, sums).AsInt32(), 16 - ConstBits);

        var tmp1312Low = tmp01Low - tmp32Low;
        var tmp1011Low = tmp01Low + tmp32Low;
        var tmp1312High = tmp01High - tmp32High;
        var tmp1011High = tmp01High + tmp32High;

        // Odd part
        var z34 = in75 + in31;
        var z43 = Avx2.Permute2x128(z34, z34, 0x01);
        var z34Low = Avx2.MultiplyAddAdjacent(Avx2.UnpackLow(z34, z43), PwMF078F117F078F117);
        var z34High = Avx2.MultiplyAddAdjacent(Avx2.UnpackHigh(z34, z43), PwMF078F117F078F117);

        var in13 = Avx2.Permute2x128(in31, in31, 0x01);
        var in7153Low = Avx2.UnpackLow(in75, in13);
        var in7153High = Avx2.UnpackHigh(in75, in13);

        var tmp01oLow = Avx2.MultiplyAddAdjacent(in7153Low, PwMF060MF089MF050MF256) + z34Low;
        var tmp01oHigh = Avx2.MultiplyAddAdjacent(in7153High, PwMF060MF089MF050MF256) + z34High;
        var tmp32oLow = Avx2.MultiplyAddAdjacent(in7153Low, PwMF089F060MF256F050) + Avx2.Permute2x128(z34Low, z34Low, 0x01);
        var tmp32oHigh = Avx2.MultiplyAddAdjacent(in7153High, PwMF089F060MF256F050) + Avx2.Permute2x128(z34High, z34High, 0x01);

        // Final output stage
        d01 = Descale(firstPass, tmp1011Low + tmp32oLow, tmp1011High + tmp32oHigh);
        d76 = Descale(firstPass, tmp1011Low - tmp32oLow, tmp1011High - tmp32oHigh);
        d32 = Descale(firstPass, tmp1312Low + tmp01oLow, tmp1312High + tmp01oHigh);
        d45 = Descale(firstPass, tmp1312Low - tmp01oLow, tmp1312High - tmp01oHigh);
    }

    /// <summary>
    /// DESCALE then vpackssdw: rounded and shifted down by the pass's scale, then saturated to 16 bits, two registers'
    /// worth packed into one. The shift is a constant in each pass, so it compiles to an immediate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> Descale(bool firstPass, Vector256<int> low, Vector256<int> high) =>
        firstPass
            ? Avx2.PackSignedSaturate(
                Avx2.ShiftRightArithmetic(low + Vector256.Create(1 << (DescalePass1 - 1)), DescalePass1),
                Avx2.ShiftRightArithmetic(high + Vector256.Create(1 << (DescalePass1 - 1)), DescalePass1))
            : Avx2.PackSignedSaturate(
                Avx2.ShiftRightArithmetic(low + Vector256.Create(1 << (DescalePass2 - 1)), DescalePass2),
                Avx2.ShiftRightArithmetic(high + Vector256.Create(1 << (DescalePass2 - 1)), DescalePass2));

    /// <summary>DOTRANSPOSE: data0_1, data3_2, data4_5 and data7_6 into data0_4, data1_5, data2_6 and data3_7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(Vector256<short> d01, Vector256<short> d32, Vector256<short> d45, Vector256<short> d76,
        out Vector256<short> d04, out Vector256<short> d15, out Vector256<short> d26, out Vector256<short> d37)
    {
        var t5 = Avx2.Permute4x64(d01.AsInt64(), 0xD8).AsInt16();
        var t6 = Avx2.Permute4x64(d32.AsInt64(), 0x72).AsInt16();
        var t7 = Avx2.Permute4x64(d45.AsInt64(), 0xD8).AsInt16();
        var t8 = Avx2.Permute4x64(d76.AsInt64(), 0x72).AsInt16();

        var a1 = Avx2.UnpackLow(t5, t6);
        var a2 = Avx2.UnpackHigh(t5, t6);
        var a3 = Avx2.UnpackLow(t7, t8);
        var a4 = Avx2.UnpackHigh(t7, t8);

        var b5 = Avx2.UnpackLow(a1, a2);
        var b6 = Avx2.UnpackLow(a3, a4);
        var b7 = Avx2.UnpackHigh(a1, a2);
        var b8 = Avx2.UnpackHigh(a3, a4);

        d04 = Avx2.UnpackLow(b5.AsInt64(), b6.AsInt64()).AsInt16();
        d15 = Avx2.UnpackHigh(b5.AsInt64(), b6.AsInt64()).AsInt16();
        d26 = Avx2.UnpackLow(b7.AsInt64(), b8.AsInt64()).AsInt16();
        d37 = Avx2.UnpackHigh(b7.AsInt64(), b8.AsInt64()).AsInt16();
    }

    /// <summary>The same arithmetic, a value at a time: where AVX2 is absent, and what the vector path is held to.</summary>
    public static void TransformScalar(ReadOnlySpan<short> block, ReadOnlySpan<short> multipliers, Span<byte> output, int stride)
    {
        Span<int> workspace = stackalloc int[64];
        Span<int> column = stackalloc int[8];
        Span<int> result = stackalloc int[8];

        var acZero = true;
        for (var i = 8; i < 64 && acZero; i++) acZero = block[i] == 0;

        for (var c = 0; c < 8; c++)
        {
            if (acZero)
            {
                var dc = (short)((short)(block[c] * multipliers[c]) << Pass1Bits);
                for (var m = 0; m < 8; m++) workspace[m * 8 + c] = dc;
                continue;
            }

            for (var k = 0; k < 8; k++) column[k] = (short)(block[k * 8 + c] * multipliers[k * 8 + c]);
            DodctScalar(column, result, DescalePass1);
            for (var m = 0; m < 8; m++) workspace[m * 8 + c] = result[m];
        }

        for (var r = 0; r < 8; r++)
        {
            DodctScalar(workspace.Slice(r * 8, 8), result, DescalePass2);
            for (var m = 0; m < 8; m++) output[r * stride + m] = (byte)(Math.Clamp(result[m], -128, 127) + 128);
        }
    }

    private static void DodctScalar(ReadOnlySpan<int> input, Span<int> output, int descale)
    {
        int in0 = input[0], in1 = input[1], in2 = input[2], in3 = input[3];
        int in4 = input[4], in5 = input[5], in6 = input[6], in7 = input[7];

        var tmp3 = in2 * (F0541 + F0765) + in6 * F0541;
        var tmp2 = in2 * F0541 + in6 * (F0541 - F1847);
        var tmp0 = (short)(in0 + in4) << ConstBits;
        var tmp1 = (short)(in0 - in4) << ConstBits;
        int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

        int z3 = (short)(in7 + in3), z4 = (short)(in5 + in1);
        var z3s = z3 * (F1175 - F1961) + z4 * F1175;
        var z4s = z3 * F1175 + z4 * (F1175 - F0390);
        var o0 = in7 * (F0298 - F0899) + in1 * -F0899 + z3s;
        var o1 = in5 * (F2053 - F2562) + in3 * -F2562 + z4s;
        var o2 = in5 * -F2562 + in3 * (F3072 - F2562) + z3s;
        var o3 = in7 * -F0899 + in1 * (F1501 - F0899) + z4s;

        var round = 1 << (descale - 1);
        output[0] = Saturate16((tmp10 + o3 + round) >> descale);
        output[7] = Saturate16((tmp10 - o3 + round) >> descale);
        output[1] = Saturate16((tmp11 + o2 + round) >> descale);
        output[6] = Saturate16((tmp11 - o2 + round) >> descale);
        output[2] = Saturate16((tmp12 + o1 + round) >> descale);
        output[5] = Saturate16((tmp12 - o1 + round) >> descale);
        output[3] = Saturate16((tmp13 + o0 + round) >> descale);
        output[4] = Saturate16((tmp13 - o0 + round) >> descale);

        static int Saturate16(int value) => Math.Clamp(value, short.MinValue, short.MaxValue);
    }
}
