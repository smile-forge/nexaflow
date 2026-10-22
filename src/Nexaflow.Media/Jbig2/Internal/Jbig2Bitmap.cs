/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * image/Bitmap.java, image/Bitmaps.java and util/CombinationOperator.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: bitmaps are charged to the decode's memory limit; one blit and one extract, clipped to both
 * bitmaps, stand in for the shifted, unshifted and pixel-by-pixel variants; no unsafe code.
 */

using System.Runtime.Intrinsics;

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>How a bitmap's pixels combine with those already where it is placed, 7.4.1.5 and 7.4.8.5.</summary>
internal enum CombinationOperator : byte
{
    Or,
    And,
    Xor,
    Xnor,
    Replace,
}

internal static class CombinationOperators
{
    public static CombinationOperator FromCode(int code) => code switch
    {
        0 => CombinationOperator.Or,
        1 => CombinationOperator.And,
        2 => CombinationOperator.Xor,
        3 => CombinationOperator.Xnor,
        _ => CombinationOperator.Replace,
    };
}

internal readonly record struct Jbig2Rectangle(int X, int Y, int Width, int Height);

/// <summary>A bi-level bitmap, one bit a pixel, most significant first, each row starting on a byte; 1 is black.</summary>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height, Jbig2DecodeContext context)
    {
        ByteArray = context.Allocate(width, height);
        Width = width;
        Height = height;
        RowStride = (width + 7) >> 3;
    }

    public int Width { get; }

    public int Height { get; }

    public int RowStride { get; }

    public byte[] ByteArray { get; }

    public int GetByteIndex(int x, int y) => y * RowStride + (x >> 3);

    public byte GetPixel(int x, int y) => (byte)(ByteArray[GetByteIndex(x, y)] >> (7 - (x & 7)) & 1);

    public void SetPixel(int x, int y, int value)
    {
        ref var b = ref ByteArray[GetByteIndex(x, y)];
        var mask = 1 << (7 - (x & 7));
        b = (byte)((value & 1) != 0 ? b | mask : b & ~mask);
    }

    public byte GetByte(int index) => ByteArray[index];

    public void SetByte(int index, byte value) => ByteArray[index] = value;

    public int GetByteAsInteger(int index) => ByteArray[index];

    /// <summary>A pixel, or 0 for one outside the bitmap — as the standard reads the pixels around a region.</summary>
    public byte GetPixelOrZero(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? GetPixel(x, y) : (byte)0;
}

internal static class Jbig2Bitmaps
{
    public static byte CombineBytes(byte value1, byte value2, CombinationOperator op) => op switch
    {
        CombinationOperator.Or => (byte)(value2 | value1),
        CombinationOperator.And => (byte)(value2 & value1),
        CombinationOperator.Xor => (byte)(value2 ^ value1),
        CombinationOperator.Xnor => (byte)~(value1 ^ value2),
        _ => value2,
    };

    /// <summary>
    /// The work of going through a bitmap: a step a pixel, one more a row and one more for the bitmap — so a bitmap no
    /// pixels wide, which a damaged header can make billions of rows high, or one of no pixels at all, still costs.
    /// </summary>
    public static long Work(int width, int height) => ((long)Math.Max(0, width) + 1) * Math.Max(0, height) + 1;

    /// <summary>A copy of the part of <paramref name="source"/> under <paramref name="roi"/>; pixels outside the source are 0.</summary>
    public static Jbig2Bitmap Extract(Jbig2Rectangle roi, Jbig2Bitmap source, Jbig2DecodeContext context)
    {
        var target = new Jbig2Bitmap(roi.Width, roi.Height, context);
        context.Work(Work(roi.Width, roi.Height));
        var y0 = Math.Max(0, -roi.Y);
        var y1 = Math.Min(roi.Height, source.Height - roi.Y);
        for (var y = y0; y < y1; y++)
        {
            var sourceRow = (roi.Y + y) * source.RowStride;
            var targetRow = y * target.RowStride;
            for (var b = 0; b < target.RowStride; b++)
                target.ByteArray[targetRow + b] = Gather(source, sourceRow, roi.X + (b << 3));
            if ((roi.Width & 7) != 0)
                target.ByteArray[targetRow + target.RowStride - 1] &= (byte)(0xFF << (8 - (roi.Width & 7)));
        }
        if (roi.X < 0 || roi.X + roi.Width > source.Width) ClearOutside(target, -roi.X, source.Width - roi.X);
        return target;
    }

    /// <summary>
    /// Combines <paramref name="source"/> into <paramref name="target"/> with its top left pixel at (x, y), each pixel
    /// by <paramref name="op"/>; what falls outside the target is left out.
    /// </summary>
    public static void Blit(Jbig2Bitmap source, Jbig2Bitmap target, int x, int y, CombinationOperator op, Jbig2DecodeContext context)
    {
        var x0 = Math.Max(0, x);
        var x1 = (int)Math.Min(target.Width, (long)x + source.Width);
        var y0 = Math.Max(0, y);
        var y1 = (int)Math.Min(target.Height, (long)y + source.Height);

        // A blit that lands nowhere still counts, or a grid of empty cells would cost nothing.
        context.Work(1 + Math.Max(0L, (long)(x1 - x0)) * Math.Max(0, y1 - y0));
        if (x0 >= x1 || y0 >= y1) return;

        var firstByte = x0 >> 3;
        var lastByte = (x1 - 1) >> 3;
        var firstMask = (byte)(0xFF >> (x0 & 7));
        var lastMask = (byte)(0xFF << (7 - ((x1 - 1) & 7)));
        var aligned = (x & 7) == 0;

        for (var row = y0; row < y1; row++)
        {
            var sourceRow = (row - y) * source.RowStride;
            var targetRow = row * target.RowStride;
            var b = firstByte;
            var end = lastByte;

            // Whole bytes on both sides between the edges: a straight run.
            if (aligned)
            {
                var innerStart = (x0 & 7) == 0 ? firstByte : firstByte + 1;
                var innerEnd = ((x1 & 7) == 0 ? lastByte : lastByte - 1);
                if (innerEnd >= innerStart)
                {
                    if (innerStart != firstByte) CombineMasked(target, targetRow + firstByte, Gather(source, sourceRow, (firstByte << 3) - x), firstMask, op);
                    var count = innerEnd - innerStart + 1;
                    CombineRun(source.ByteArray.AsSpan(sourceRow + ((innerStart << 3) - x >> 3), count), target.ByteArray.AsSpan(targetRow + innerStart, count), op);
                    if (innerEnd != lastByte) CombineMasked(target, targetRow + lastByte, Gather(source, sourceRow, (lastByte << 3) - x), lastMask, op);
                    continue;
                }
            }

            for (; b <= end; b++)
            {
                var mask = (byte)0xFF;
                if (b == firstByte) mask &= firstMask;
                if (b == lastByte) mask &= lastMask;
                CombineMasked(target, targetRow + b, Gather(source, sourceRow, (b << 3) - x), mask, op);
            }
        }
    }

    /// <summary>The eight pixels of a row from pixel <paramref name="bit"/> on, as a byte; pixels before the row's start are 0.</summary>
    private static byte Gather(Jbig2Bitmap source, int rowStart, int bit)
    {
        var index = bit >> 3;
        var shift = bit & 7;
        var bytes = source.ByteArray;
        var high = (uint)index < (uint)source.RowStride ? bytes[rowStart + index] : 0;
        if (shift == 0) return (byte)high;
        var low = (uint)(index + 1) < (uint)source.RowStride ? bytes[rowStart + index + 1] : 0;
        return (byte)((high << 8 | low) >> (8 - shift));
    }

    private static void CombineMasked(Jbig2Bitmap target, int index, byte value, byte mask, CombinationOperator op)
    {
        ref var old = ref target.ByteArray[index];
        old = (byte)(old & ~mask | CombineBytes(old, value, op) & mask);
    }

    private static void ClearOutside(Jbig2Bitmap bitmap, int from, int to)
    {
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (x < from || x >= to) bitmap.SetPixel(x, y, 0);
    }

    /// <summary>A run of whole bytes combined, a vector at a time.</summary>
    private static void CombineRun(ReadOnlySpan<byte> source, Span<byte> target, CombinationOperator op)
    {
        if (op == CombinationOperator.Replace)
        {
            source.CopyTo(target);
            return;
        }

        var i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; i + Vector256<byte>.Count <= source.Length; i += Vector256<byte>.Count)
            {
                var s = Vector256.Create(source[i..]);
                var t = Vector256.Create(target[i..]);
                var r = op switch
                {
                    CombinationOperator.Or => s | t,
                    CombinationOperator.And => s & t,
                    CombinationOperator.Xor => s ^ t,
                    _ => ~(s ^ t),
                };
                r.CopyTo(target[i..]);
            }
        }
        for (; i < source.Length; i++) target[i] = CombineBytes(target[i], source[i], op);
    }
}
