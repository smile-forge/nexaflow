using System;
using Nexaflow.Media.Jpeg2000;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// <c>JPXDecode</c>: a JPEG 2000 image decoded by <see cref="Jpeg2000Decoder"/> into the samples PdfPig reads an
/// image's data as — interleaved, every component at one precision, each row starting on a byte.
/// </summary>
internal sealed class PdfJpxFilter : IFilter
{
    public bool IsSupported => true;

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        // An image dictionary with a /ColorSpace of its own interprets the samples by it, whatever colour the JPEG 2000
        // data describes (ISO 32000-2, 7.4.9), so the JP2 file's colour boxes are left unread: an /Indexed colour space
        // reads the samples as indices into its own lookup table, one component a pixel. Without a /ColorSpace, PdfPig
        // takes the colour space from the JP2 boxes, which describe the palette's colours, so the palette is applied.
        var declaresColourSpace = streamDictionary.ContainsKey(NameToken.ColorSpace);

        var image = Jpeg2000Decoder.Decode(input, new Jpeg2000DecodeOptions { ApplyPalette = !declaresColourSpace });
        image.MakeUniformPrecision();
        // Without a /ColorSpace PdfPig reads sYCC and e-sYCC data as RGB, so it is converted first, as opj_decompress
        // converts it; any other colour space is handed on as it was coded.
        if (!declaresColourSpace) image.ConvertYccToRgb();

        return image.ToPackedSamples();
    }
}
