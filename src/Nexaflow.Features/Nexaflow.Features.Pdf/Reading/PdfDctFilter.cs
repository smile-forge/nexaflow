using System;
using Nexaflow.Media.Jpeg;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// <c>DCTDecode</c>: a JPEG image decoded by <see cref="JpegDecoder"/> into the samples PdfPig reads an image's data
/// as — a byte a sample, interleaved, every row whole: grey, RGB or CMYK as the JPEG's components are, CMYK as the file
/// codes it (Adobe-inverted when it is), for the image's colour space and decode array to interpret.
/// </summary>
internal sealed class PdfDctFilter : IFilter
{
    private static readonly NameToken ColorTransform = NameToken.Create("ColorTransform");

    public bool IsSupported => true;

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        var header = JpegDecoder.Identify(input);
        var options = new JpegDecodeOptions { SourceColourSpace = SourceColourSpace(header, streamDictionary) };
        return JpegDecoder.Decode(input, options).Samples;
    }

    /// <summary>
    /// The colour space a JPEG in a PDF is coded in, where PDF reads it differently from libjpeg. The markers come
    /// first: an Adobe APP14 marker's transform, which libjpeg reads as PDF does (ISO 32000-2, 7.4.8); then a JFIF
    /// marker, or three components not named 'R', 'G', 'B', mean YCbCr whatever <c>/ColorTransform</c> says — some
    /// producers write a JFIF YCbCr JPEG with <c>/ColorTransform 0</c>, and only an RGB JPEG names its components so
    /// (0966320.pdf). Past those, the image dictionary's <c>/ColorTransform</c>, and failing that its default:
    /// transformed with three components, not with any other number. Null where that is libjpeg's own reading, and for
    /// a lossless image, which no colour transform applies to.
    /// </summary>
    internal static JpegColourSpace? SourceColourSpace(JpegHeader header, DictionaryToken dictionary)
    {
        if (header.AdobeTransform is not null || header.Process == JpegProcess.Lossless) return null;

        bool transform;
        if (header.Components == 3 && (header.HasJfifMarker || !NamesRgb(header)))
            transform = true;
        else if (dictionary.TryGet<NumericToken>(ColorTransform, out var token))
            transform = token.Int == 1;
        else
            transform = header.Components == 3;   // GHOSTSCRIPT-699178-0.pdf, GHOSTSCRIPT-700370-2.pdf, GHOSTSCRIPT-700139-0.pdf

        return header.Components switch
        {
            3 => transform ? JpegColourSpace.YCbCr : JpegColourSpace.Rgb,
            4 => transform ? JpegColourSpace.Ycck : JpegColourSpace.Cmyk,
            _ => null,
        };
    }

    private static bool NamesRgb(JpegHeader header) =>
        header.ComponentIds[0] == 'R' && header.ComponentIds[1] == 'G' && header.ComponentIds[2] == 'B';
}
