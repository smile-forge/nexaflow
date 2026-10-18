using System.Windows;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// What PdfPig makes a page into when only its place is wanted: its size, read from its boxes and rotation without
/// touching its content — the cost of laying out a thousand-page document's scroll range before any page is read.
/// <para>
/// The boxes are read the way PdfPig reads them for a whole page: the media box, inherited when the page has none;
/// the crop box clipped to it. A crop box inherited from the page tree is the one thing PdfPig keeps to itself, so
/// such a page's slot is its media box, and the size the page is read at (<see cref="PdfPageNode.Size"/>) is the one
/// to trust once it is read.
/// </para>
/// </summary>
internal sealed class PdfPageSlotFactory : IPageFactory<PdfPageSlot>
{
    private readonly IPdfTokenScanner _scanner;

    public PdfPageSlotFactory(
        IPdfTokenScanner pdfScanner, IResourceStore resourceStore, ILookupFilterProvider filterProvider,
        IPageContentParser pageContentParser, ParsingOptions parsingOptions)
    {
        _scanner = pdfScanner;
    }

    public PdfPageSlot Create(int number, DictionaryToken dictionary, PageTreeMembers pageTreeMembers, NamedDestinations namedDestinations)
    {
        var rotation = new PageRotationDegrees(
            dictionary.TryGet(NameToken.Rotate, _scanner, out NumericToken? rotate) ? rotate.Int : pageTreeMembers.Rotation);

        var media = Box(dictionary, NameToken.MediaBox) ?? pageTreeMembers.MediaBox?.Bounds ?? MediaBox.Letter.Bounds;
        var crop = Box(dictionary, NameToken.CropBox) ?? media;
        if (media.Intersect(crop) is { } shown) crop = shown;

        var visible = new CropBox(crop).GetVisibleBounds(rotation);
        return new PdfPageSlot(number, new Size(visible.Width, visible.Height));
    }

    private PdfRectangle? Box(DictionaryToken dictionary, NameToken key) =>
        dictionary.TryGet(key, out var token) && DirectObjectFinder.TryGet(token, _scanner, out ArrayToken? box) &&
        box.Length == 4
            ? box.ToRectangle(_scanner)
            : null;
}
