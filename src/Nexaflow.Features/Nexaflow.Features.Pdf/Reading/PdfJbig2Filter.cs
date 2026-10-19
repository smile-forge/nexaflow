using System;
using Nexaflow.Media.Jbig2;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// <c>JBIG2Decode</c>: a JBIG2 stream decoded by <see cref="Jbig2Decoder"/>, with the segments of the
/// <c>JBIG2Globals</c> stream its decode parameters name, into the samples PdfPig reads a one-bit image's data as — a bit
/// a pixel, every row whole, 0 for black: JBIG2 codes black as 1, and the filter's output inverts it (ISO 32000-2,
/// 7.4.7). The page is laid out at the width and height the image dictionary gives, cropped where the JBIG2 page is
/// larger and white where it is smaller, as PDFium lays it out.
/// </summary>
internal sealed class PdfJbig2Filter : IFilter
{
    private readonly Jbig2GlobalsCache _globals = new();

    public bool IsSupported => true;

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        var parameters = DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
        Jbig2Globals? globals = null;
        if (parameters.TryGet(NameToken.Jbig2Globals, out StreamToken globalsStream) && !globalsStream.Data.IsEmpty)
            globals = _globals.Get(globalsStream.Decode(filterProvider));

        var image = Jbig2Decoder.Decode(input, globals);
        var width = streamDictionary.TryGet(NameToken.Width, out NumericToken w) ? w.Int : image.Width;
        var height = streamDictionary.TryGet(NameToken.Height, out NumericToken h) ? h.Int : image.Height;
        return Samples(image, width, height);
    }

    /// <summary>The page's bits at <paramref name="width"/> by <paramref name="height"/>, 0 for black.</summary>
    internal static byte[] Samples(Jbig2Image image, int width, int height)
    {
        byte[] bits;
        if (width == image.Width && height == image.Height)
        {
            bits = image.Bits.ToArray();
        }
        else
        {
            // Laid out at the dictionary's size: what the page has of each row, white past it.
            var stride = (width + 7) >> 3;
            bits = new byte[(long)stride * Math.Max(0, height)];
            var copiedWidth = Math.Min(width, image.Width);
            var copiedBytes = (copiedWidth + 7) >> 3;
            var lastMask = (copiedWidth & 7) == 0 ? (byte)0xFF : (byte)(0xFF << (8 - (copiedWidth & 7)));
            var source = image.Bits.Span;
            for (var y = 0; y < Math.Min(height, image.Height); y++)
            {
                var row = bits.AsSpan(y * stride, stride);
                source.Slice(y * image.Stride, copiedBytes).CopyTo(row);
                if (copiedBytes > 0) row[copiedBytes - 1] &= lastMask;
            }
        }

        // JBIG2's 1 for black, inverted to the 0 for black a one-bit DeviceGray image and an image mask read.
        for (var i = 0; i < bits.Length; i++) bits[i] = (byte)~bits[i];
        return bits;
    }
}
