using System;
using Nexaflow.Media.Jpeg2000;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// <c>JPXDecode</c>: a JPEG 2000 image decoded by <see cref="Jpeg2000Decoder"/> into the samples PdfPig reads an
/// image's data as — interleaved, every component at one precision, each row starting on a byte — its colour channels
/// alone (<see cref="PdfJpxSamples"/>). A page draws its JPEG 2000 images through <see cref="PdfJpxSamples"/> itself,
/// where the colour space is known and the data's opacity can be its soft mask.
/// </summary>
internal sealed class PdfJpxFilter : IFilter
{
    public bool IsSupported => true;

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex) =>
        PdfJpxSamples.Read(input, streamDictionary, colourComponents: null).Colour;
}
