using System.Runtime.InteropServices;
using Nexaflow.Media.Jbig2.Internal;

namespace Nexaflow.Media.Jbig2;

/// <summary>
/// Decodes JBIG2 — generic regions arithmetic or MMR coded, refinement, text regions and symbol dictionaries Huffman or
/// arithmetically coded, halftone regions and pattern dictionaries, striped pages — to a page of bits.
/// </summary>
public static class Jbig2Decoder
{
    /// <summary>
    /// Decodes the first page of a JBIG2 file, or of a stream embedded in a PDF — one without a file header, its
    /// segments in order — with the segments of its <c>JBIG2Globals</c> stream, if it has one, in
    /// <paramref name="globals"/>, which any number of decodes may share. A stream damaged part way decodes as far as it goes: a segment cut short is decoded
    /// to its end, one that cannot be decoded at all is left off the page, and either is reported in
    /// <see cref="Jbig2Image.Warnings"/>.
    /// </summary>
    /// <exception cref="Jbig2Exception">The data holds no page, its page cannot be read, or decoding it would take
    /// more memory than <see cref="Jbig2DecodeOptions.MemoryLimit"/>.</exception>
    public static Jbig2Image Decode(ReadOnlyMemory<byte> data, Jbig2Globals? globals = null, Jbig2DecodeOptions? options = null)
    {
        options ??= Jbig2DecodeOptions.Default;
        var context = new Jbig2DecodeContext(options.MemoryLimit, options.WorkLimit);
        try
        {
            var document = new Jbig2Document(Reader(data), globals?.Document, context);
            var page = document.FirstPage ?? throw new Jbig2Exception("The data holds no page");
            if (page.PageNumber != 1) context.Warn($"The data has no page 1; page {page.PageNumber} is decoded");
            var bitmap = page.GetBitmap();
            ClearPadding(bitmap);
            IReadOnlyList<string> warnings = globals is { Warnings.Count: > 0 } ? [.. globals.Warnings, .. context.Warnings] : context.Warnings;
            return new Jbig2Image(bitmap.Width, bitmap.Height, bitmap.ByteArray, warnings);
        }
        catch (Jbig2DataException e)
        {
            throw new Jbig2Exception($"The page cannot be decoded: {e.Message}");
        }
    }

    /// <summary>Zeros the bits past the width in each row's last byte, which a page filled with its default pixel sets.</summary>
    private static void ClearPadding(Jbig2Bitmap bitmap)
    {
        if ((bitmap.Width & 7) == 0) return;
        var mask = (byte)(0xFF << (8 - (bitmap.Width & 7)));
        for (var last = bitmap.RowStride - 1; last < bitmap.ByteArray.Length; last += bitmap.RowStride) bitmap.ByteArray[last] &= mask;
    }

    /// <summary>A reader over the data where it lies, when an array holds it; otherwise over a copy.</summary>
    internal static Jbig2Reader Reader(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment)
            ? new Jbig2Reader(segment.Array!, segment.Offset, segment.Count)
            : new Jbig2Reader(data.ToArray(), 0, data.Length);
}
