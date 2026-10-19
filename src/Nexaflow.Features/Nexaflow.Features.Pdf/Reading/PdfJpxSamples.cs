using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Media.Jpeg2000;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// A <c>JPXDecode</c> image's samples: its colour, packed as a PDF image's data is, and apart from it the opacity the
/// data carries when <c>/SMaskInData</c> makes that the image's soft mask (ISO 32000-2, 8.9.5, table 87; 11.6.5.3). Its
/// colour never holds a channel that is not colour — PDF reads an image's samples as its colour space's components alone,
/// whatever else the JPEG 2000 data codes (7.4.9).
/// </summary>
/// <param name="Colour">The colour components, interleaved, each row starting on a byte.</param>
/// <param name="Opacity">The soft mask's levels, a byte a sample at <paramref name="Width"/> × <paramref name="Height"/>,
/// row by row; null where the data's opacity is not the image's soft mask, or the data has none.</param>
/// <param name="Width">The decoded image's width, in samples.</param>
/// <param name="Height">The decoded image's height, in samples.</param>
internal readonly record struct PdfJpxSamples(Memory<byte> Colour, byte[]? Opacity, int Width, int Height)
{
    private static readonly NameToken SMaskInData = NameToken.Create("SMaskInData");

    /// <summary>
    /// Whether the image is masked by the opacity in its own data: a JPEG 2000 image whose <c>/SMaskInData</c> is not 0,
    /// and with no <c>/SMask</c>, which takes its place. Such an image, like one with an <c>/SMask</c>, is masked by that
    /// alone and not by the graphics state's soft mask.
    /// </summary>
    public static bool CarriesSoftMask(DictionaryToken dictionary) =>
        Mode(dictionary) != 0 && !dictionary.ContainsKey(NameToken.Smask)
        && dictionary.TryGet(NameToken.Filter, out NameToken filter) && filter.Equals(NameToken.JpxDecode);

    /// <summary>
    /// Decodes <paramref name="data"/>, a <c>JPXDecode</c> image's stream under <paramref name="dictionary"/>.
    /// <paramref name="colourComponents"/> is how many components its colour space reads, when that is known — it is
    /// where a component past the colour, which the data's channel definitions may not mark, is told apart from it, and
    /// without it the data's opacity is not read at all.
    /// </summary>
    /// <exception cref="Jpeg2000Exception">The data is not JPEG 2000, or is damaged past decoding.</exception>
    /// <exception cref="InvalidDataException">The data holds fewer colour components than the colour space reads.</exception>
    public static PdfJpxSamples Read(ReadOnlyMemory<byte> data, DictionaryToken dictionary, int? colourComponents)
    {
        // An image dictionary with a /ColorSpace of its own interprets the samples by it, whatever colour the JPEG 2000
        // data describes (ISO 32000-2, 7.4.9), so the JP2 file's colour boxes are left unread: an /Indexed colour space
        // reads the samples as indices into its own lookup table, one component a pixel. Without a /ColorSpace, PdfPig
        // takes the colour space from the JP2 boxes, which describe the palette's colours, so the palette is applied.
        var declaresColourSpace = dictionary.ContainsKey(NameToken.ColorSpace);

        var image = Jpeg2000Decoder.Decode(data, new Jpeg2000DecodeOptions { ApplyPalette = !declaresColourSpace });
        image.MakeUniformPrecision();
        // Without a /ColorSpace PdfPig reads sYCC and e-sYCC data as RGB, so it is converted first, as opj_decompress
        // converts it; any other colour space is handed on as it was coded.
        if (!declaresColourSpace) image.ConvertYccToRgb();

        // Taken after the conversion, which rewrites the components it converts.
        var components = image.Components;
        var unmarked = components.Where(static c => c.ChannelType == Jpeg2000ChannelType.Colour).ToList();
        var colour = unmarked;
        if (colourComponents is { } count)
        {
            if (unmarked.Count < count)
                throw new InvalidDataException($"{unmarked.Count} colour components for a colour space of {count}");
            colour = unmarked.GetRange(0, count);
        }

        var mode = Mode(dictionary);
        var opacity = colourComponents is { } read && CarriesSoftMask(dictionary) ? OpacityOf(components, unmarked, read) : null;

        // /SMaskInData 2 says the colour was preblended with the opacity, as a premultiplied channel definition does.
        var premultiplied = opacity is not null && (mode == 2 || opacity.ChannelType == Jpeg2000ChannelType.PremultipliedOpacity);
        return new PdfJpxSamples(
            image.ToPackedSamples(colour, premultiplied ? opacity : null),
            opacity is null ? null : image.ToLevels(opacity),
            image.Width,
            image.Height);
    }

    private static int Mode(DictionaryToken dictionary) =>
        dictionary.TryGet(SMaskInData, out NumericToken mode) ? mode.Int : 0;

    /// <summary>
    /// The component the image's opacity is in: the one the data's channel definitions mark so, or, where none is marked —
    /// a bare codestream, or a JP2 file read under the PDF's own colour space, which leaves its boxes unread — the one past
    /// the colour space's components.
    /// </summary>
    private static Jpeg2000Component? OpacityOf(
        IReadOnlyList<Jpeg2000Component> components, List<Jpeg2000Component> unmarked, int colourComponents) =>
        components.FirstOrDefault(static c => c.ChannelType is Jpeg2000ChannelType.Opacity or Jpeg2000ChannelType.PremultipliedOpacity)
        ?? (unmarked.Count > colourComponents ? unmarked[colourComponents] : null);
}
