/*
 * The arithmetic of libjpeg-turbo 3.1.1's src/jdsample.c and src/jdcolor.c, a row at a time:
 * This file was part of the Independent JPEG Group's software:
 * Copyright (C) 1991-1997, Thomas G. Lane.
 * libjpeg-turbo Modifications:
 * Copyright 2009 Pierre Ossman <ossman@cendio.se> for Cendio AB
 * Copyright (C) 2009, 2011-2012, 2014-2016, 2022, 2024, D. R. Commander.
 * Copyright (C) 2013, Linaro Limited.
 * For conditions of distribution and use, see the accompanying README.ijg file.
 */

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Nexaflow.Media.Jpeg.Internal;

/// <summary>
/// The fancy upsamplers and colour converters, sixteen samples at a time through <see cref="Vector256"/>, each lane
/// doing the scalar code's integer arithmetic exactly; the samples left over go through the scalar code.
/// </summary>
/// <remarks>
/// A fancy upsampler's first and last columns are its general case with the edge sample standing in for the missing
/// neighbour — h2v2's <c>(thiscolsum * 4 + 8) &gt;&gt; 4</c> is <c>(thiscolsum * 3 + thiscolsum + 8) &gt;&gt; 4</c> — so the
/// neighbours are read from a copy of the row with its edge samples repeated, and every column is the general case.
/// Two outputs per input are interleaved by packing them into 16-bit lanes, low byte first.
/// </remarks>
internal static class RowKernels
{
    private static readonly bool Vectorised = Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    // jdcolor.c's YCbCr to RGB constants: FIX(x) at SCALEBITS 16, and ONE_HALF.
    private const int CrR = 91881, CbB = 116130, CrG = -46802, CbG = -22554, OneHalf = 1 << 15;

    // Byte positions of R, G and B in three 16-byte blocks of RGB24 (255: none, which Shuffle zeroes).
    private static readonly Vector128<byte>[] RgbShuffles = BuildRgbShuffles();

    private static Vector128<byte>[] BuildRgbShuffles()
    {
        var masks = new Vector128<byte>[9];
        Span<byte> mask = stackalloc byte[16];
        for (var block = 0; block < 3; block++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                for (var i = 0; i < 16; i++)
                {
                    var position = block * 16 + i;
                    mask[i] = position % 3 == channel ? (byte)(position / 3) : (byte)255;
                }
                masks[block * 3 + channel] = Vector128.Create((ReadOnlySpan<byte>)mask);
            }
        }
        return masks;
    }

    /// <summary>Sixteen bytes widened to 16-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> Widen(ReadOnlySpan<byte> bytes)
    {
        var v = Vector128.Create(bytes[..16]);
        return Vector256.Create(Vector128.WidenLower(v), Vector128.WidenUpper(v)).AsInt16();
    }

    /// <summary>
    /// The row with its edge samples repeated, in 16-bit lanes from <paramref name="padded"/>[1]: what a
    /// fancy upsampler reads as each sample's neighbours.
    /// </summary>
    private static void Pad(ReadOnlySpan<short> sums, int count, Span<short> padded)
    {
        padded[0] = sums[0];
        padded[count + 1] = sums[count - 1];
    }

    /// <summary>
    /// 3.1.1 - h2v1_fancy_upsample: <c>(3 * nearer + further + 1 or 2) &gt;&gt; 2</c>, two outputs a sample;
    /// <paramref name="count"/> (the downsampled width) above two.
    /// </summary>
    public static void H2V1Fancy(ReadOnlySpan<byte> input, int count, Span<byte> output, Span<short> scratch)
    {
        var padded = scratch[..(count + 2)];
        var c = 0;
        if (Vectorised)
        {
            for (; c + 16 <= count; c += 16) Widen(input[c..]).CopyTo(padded[(c + 1)..]);
        }
        for (; c < count; c++) padded[c + 1] = input[c];
        Pad(padded[1..], count, padded);

        c = 0;
        if (Vectorised)
        {
            for (; c + 16 <= count; c += 16)
            {
                var current = Vector256.Create((ReadOnlySpan<short>)padded[(c + 1)..]) * 3;
                var even = (current + Vector256.Create((ReadOnlySpan<short>)padded[c..]) + Vector256.Create((short)1)) >>> 2;
                var odd = (current + Vector256.Create((ReadOnlySpan<short>)padded[(c + 2)..]) + Vector256.Create((short)2)) >>> 2;
                Interleave(even, odd, output[(2 * c)..]);
            }
        }
        for (; c < count; c++)
        {
            var current = padded[c + 1] * 3;
            output[2 * c] = (byte)((current + padded[c] + 1) >> 2);
            output[2 * c + 1] = (byte)((current + padded[c + 2] + 2) >> 2);
        }
    }

    /// <summary>
    /// 3.1.1 - h2v2_fancy_upsample: column sums <c>3 * nearer row + further row</c>, then
    /// <c>(3 * this + neighbour + 8 or 7) &gt;&gt; 4</c>, two outputs a sample; <paramref name="count"/> above two.
    /// </summary>
    public static void H2V2Fancy(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int count, Span<byte> output, Span<short> scratch)
    {
        var padded = scratch[..(count + 2)];
        var c = 0;
        if (Vectorised)
        {
            for (; c + 16 <= count; c += 16) (Widen(near[c..]) * 3 + Widen(far[c..])).CopyTo(padded[(c + 1)..]);
        }
        for (; c < count; c++) padded[c + 1] = (short)(near[c] * 3 + far[c]);
        Pad(padded[1..], count, padded);

        c = 0;
        if (Vectorised)
        {
            for (; c + 16 <= count; c += 16)
            {
                var current = Vector256.Create((ReadOnlySpan<short>)padded[(c + 1)..]) * 3;
                var even = (current + Vector256.Create((ReadOnlySpan<short>)padded[c..]) + Vector256.Create((short)8)) >>> 4;
                var odd = (current + Vector256.Create((ReadOnlySpan<short>)padded[(c + 2)..]) + Vector256.Create((short)7)) >>> 4;
                Interleave(even, odd, output[(2 * c)..]);
            }
        }
        for (; c < count; c++)
        {
            var current = padded[c + 1] * 3;
            output[2 * c] = (byte)((current + padded[c] + 8) >> 4);
            output[2 * c + 1] = (byte)((current + padded[c + 2] + 7) >> 4);
        }
    }

    /// <summary>3.1.1 - h1v2_fancy_upsample: <c>(3 * nearer + further + bias) &gt;&gt; 2</c>, bias 1 above and 2 below.</summary>
    public static void H1V2Fancy(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int count, int bias, Span<byte> output)
    {
        var c = 0;
        if (Vectorised)
        {
            var b = Vector256.Create((short)bias);
            for (; c + 32 <= count; c += 32)
            {
                var low = (Widen(near[c..]) * 3 + Widen(far[c..]) + b) >>> 2;
                var high = (Widen(near[(c + 16)..]) * 3 + Widen(far[(c + 16)..]) + b) >>> 2;
                Vector256.Narrow(low.AsUInt16(), high.AsUInt16()).CopyTo(output[c..]);
            }
        }
        for (; c < count; c++) output[c] = (byte)((near[c] * 3 + far[c] + bias) >> 2);
    }

    /// <summary>Two rows of 16-bit outputs, each below 256, written alternately as bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Interleave(Vector256<short> even, Vector256<short> odd, Span<byte> output) =>
        (even | (odd << 8)).AsByte().CopyTo(output);

    /// <summary>3.1.1 - ycc_rgb_convert, a row.</summary>
    public static void YccToRgb(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, int width, Span<byte> rgb)
    {
        var i = 0;
        if (Vectorised)
        {
            for (; i + 16 <= width; i += 16)
            {
                Convert16(y[i..], cb[i..], cr[i..], out var r, out var g, out var b);
                StoreRgb(r, g, b, rgb[(i * 3)..]);
            }
        }
        for (; i < width; i++)
        {
            int luma = y[i], blue = cb[i] - 128, red = cr[i] - 128;
            rgb[i * 3] = Limit(luma + ((CrR * red + OneHalf) >> 16));
            rgb[i * 3 + 1] = Limit(luma + ((CbG * blue + CrG * red + OneHalf) >> 16));
            rgb[i * 3 + 2] = Limit(luma + ((CbB * blue + OneHalf) >> 16));
        }
    }

    /// <summary>3.1.1 - ycck_cmyk_convert, a row: YCbCr to R=1-C, G=1-M and B=1-Y; K passed through.</summary>
    public static void YcckToCmyk(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, ReadOnlySpan<byte> k,
        int width, Span<byte> cmyk)
    {
        var i = 0;
        if (Vectorised)
        {
            var full = Vector128.Create((byte)255);
            for (; i + 16 <= width; i += 16)
            {
                Convert16(y[i..], cb[i..], cr[i..], out var r, out var g, out var b);
                StoreFour(full - r, full - g, full - b, Vector128.Create(k[i..]), cmyk[(i * 4)..]);
            }
        }
        for (; i < width; i++)
        {
            int luma = y[i], blue = cb[i] - 128, red = cr[i] - 128;
            cmyk[i * 4] = Limit(255 - (luma + ((CrR * red + OneHalf) >> 16)));
            cmyk[i * 4 + 1] = Limit(255 - (luma + ((CbG * blue + CrG * red + OneHalf) >> 16)));
            cmyk[i * 4 + 2] = Limit(255 - (luma + ((CbB * blue + OneHalf) >> 16)));
            cmyk[i * 4 + 3] = k[i];
        }
    }

    /// <summary>
    /// The R, G and B of sixteen pixels, before range limiting as bytes: 255 minus a clamped value is the clamp of 255
    /// minus it, so YCCK reuses these.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Convert16(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr,
        out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        var y16 = Widen(y);
        var cb16 = Widen(cb) - Vector256.Create((short)128);
        var cr16 = Widen(cr) - Vector256.Create((short)128);

        var (yLow, yHigh) = Vector256.Widen(y16);
        var (cbLow, cbHigh) = Vector256.Widen(cb16);
        var (crLow, crHigh) = Vector256.Widen(cr16);
        var half = Vector256.Create(OneHalf);

        var rLow = yLow + Vector256.ShiftRightArithmetic(crLow * CrR + half, 16);
        var rHigh = yHigh + Vector256.ShiftRightArithmetic(crHigh * CrR + half, 16);
        var gLow = yLow + Vector256.ShiftRightArithmetic(cbLow * CbG + crLow * CrG + half, 16);
        var gHigh = yHigh + Vector256.ShiftRightArithmetic(cbHigh * CbG + crHigh * CrG + half, 16);
        var bLow = yLow + Vector256.ShiftRightArithmetic(cbLow * CbB + half, 16);
        var bHigh = yHigh + Vector256.ShiftRightArithmetic(cbHigh * CbB + half, 16);

        var rg = Vector256.Narrow(Clamp(rLow, rHigh), Clamp(gLow, gHigh));
        r = rg.GetLower();
        g = rg.GetUpper();
        b = Vector256.Narrow(Clamp(bLow, bHigh), Vector256<ushort>.Zero).GetLower();

        static Vector256<ushort> Clamp(Vector256<int> low, Vector256<int> high)
        {
            var zero = Vector256<int>.Zero;
            var max = Vector256.Create(255);
            return Vector256.Narrow(Vector256.Min(Vector256.Max(low, zero), max), Vector256.Min(Vector256.Max(high, zero), max)).AsUInt16();
        }
    }

    /// <summary>Sixteen pixels' R, G and B, stored as RGB24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRgb(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, Span<byte> output)
    {
        var masks = RgbShuffles;
        (Pick(r, masks[0]) | Pick(g, masks[1]) | Pick(b, masks[2])).CopyTo(output);
        (Pick(r, masks[3]) | Pick(g, masks[4]) | Pick(b, masks[5])).CopyTo(output[16..]);
        (Pick(r, masks[6]) | Pick(g, masks[7]) | Pick(b, masks[8])).CopyTo(output[32..]);
    }

    /// <summary>
    /// A byte shuffle whose index 255 gives zero, by the processor's own instruction (pshufb, tbl): the portable
    /// shuffle, its indices not constants, falls back to a loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Pick(Vector128<byte> bytes, Vector128<byte> indices)
    {
        if (System.Runtime.Intrinsics.X86.Ssse3.IsSupported) return System.Runtime.Intrinsics.X86.Ssse3.Shuffle(bytes, indices);
        if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported) return System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.VectorTableLookup(bytes, indices);
        return Vector128.Shuffle(bytes, indices);
    }

    /// <summary>Sixteen pixels' four samples, stored interleaved: each pixel a 32-bit lane, low byte first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreFour(Vector128<byte> c0, Vector128<byte> c1, Vector128<byte> c2, Vector128<byte> c3, Span<byte> output)
    {
        var (low01, high01) = Vector128.Widen(c0);
        var (low1, high1) = Vector128.Widen(c1);
        var (low2, high2) = Vector128.Widen(c2);
        var (low3, high3) = Vector128.Widen(c3);
        var pairLow = low01 | (low1 << 8);
        var pairHigh = high01 | (high1 << 8);
        var otherLow = low2 | (low3 << 8);
        var otherHigh = high2 | (high3 << 8);

        var (a, b) = Vector128.Widen(pairLow);
        var (c, d) = Vector128.Widen(otherLow);
        (a | (c << 16)).AsByte().CopyTo(output);
        (b | (d << 16)).AsByte().CopyTo(output[16..]);
        (a, b) = Vector128.Widen(pairHigh);
        (c, d) = Vector128.Widen(otherHigh);
        (a | (c << 16)).AsByte().CopyTo(output[32..]);
        (b | (d << 16)).AsByte().CopyTo(output[48..]);
    }

    /// <summary>3.1.1 - null_convert, three components: interleaved as they are.</summary>
    public static void InterleaveThree(ReadOnlySpan<byte> c0, ReadOnlySpan<byte> c1, ReadOnlySpan<byte> c2, int width, Span<byte> output)
    {
        var i = 0;
        if (Vectorised)
        {
            for (; i + 16 <= width; i += 16)
                StoreRgb(Vector128.Create(c0[i..]), Vector128.Create(c1[i..]), Vector128.Create(c2[i..]), output[(i * 3)..]);
        }
        for (; i < width; i++)
        {
            output[i * 3] = c0[i];
            output[i * 3 + 1] = c1[i];
            output[i * 3 + 2] = c2[i];
        }
    }

    /// <summary>3.1.1 - null_convert, four components: interleaved as they are.</summary>
    public static void InterleaveFour(ReadOnlySpan<byte> c0, ReadOnlySpan<byte> c1, ReadOnlySpan<byte> c2, ReadOnlySpan<byte> c3,
        int width, Span<byte> output)
    {
        var i = 0;
        if (Vectorised)
        {
            for (; i + 16 <= width; i += 16)
                StoreFour(Vector128.Create(c0[i..]), Vector128.Create(c1[i..]), Vector128.Create(c2[i..]), Vector128.Create(c3[i..]),
                    output[(i * 4)..]);
        }
        for (; i < width; i++)
        {
            output[i * 4] = c0[i];
            output[i * 4 + 1] = c1[i];
            output[i * 4 + 2] = c2[i];
            output[i * 4 + 3] = c3[i];
        }
    }

    /// <summary>sample_range_limit: 0 below, 255 above.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Limit(int value) => (byte)Math.Clamp(value, 0, 255);
}
