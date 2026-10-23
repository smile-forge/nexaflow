using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics.Colors.Icc;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// What PdfPig makes a page into when the page is asked for as a <see cref="PdfPageNode"/>: its content read by a
/// <see cref="PdfPageInterpreter"/>, its annotations with their appearances, and its text.
/// <para>
/// PdfPig makes one of these per document, by reflection, handing it the document's own scanner and resource store;
/// the caches the document's pages share come in on the <see cref="PdfReadingOptions"/> the document was opened with.
/// </para>
/// </summary>
internal sealed class PdfPageFactory : BasePageFactory<PdfPageNode>
{
    private static readonly NameToken RectKey   = NameToken.Create("Rect");
    private static readonly NameToken ParentKey = NameToken.Create("Parent");
    private static readonly NameToken NameKey   = NameToken.Create("T");
    private static readonly NameToken BBoxKey   = NameToken.Create("BBox");
    private static readonly NameToken MatrixKey = NameToken.Create("Matrix");
    private static readonly NameToken OcKey     = NameToken.Create("OC");

    private readonly PdfReadingCaches _caches;

    public PdfPageFactory(
        IPdfTokenScanner pdfScanner, IResourceStore resourceStore, ILookupFilterProvider filterProvider,
        IPageContentParser pageContentParser, ParsingOptions parsingOptions)
        : base(pdfScanner, resourceStore, filterProvider, pageContentParser, parsingOptions)
    {
        _caches = (parsingOptions as PdfReadingOptions)?.Caches ?? throw new InvalidOperationException(
            $"{nameof(PdfPageFactory)} reads documents opened with {nameof(PdfReadingOptions)}.");
    }

    /// <summary>
    /// The page's own crop box — or, while a part of the page is read, the part, so the page is read as that part.
    /// </summary>
    protected override CropBox GetCropBox(DictionaryToken dictionary, PageTreeMembers pageTreeMembers, MediaBox mediaBox)
    {
        var own = base.GetCropBox(dictionary, pageTreeMembers, mediaBox);
        if (_caches.Reading?.Part is not { } part || part == PdfPagePart.Whole) return own;

        var rotation = dictionary.TryGet(NameToken.Rotate, PdfScanner, out NumericToken? rotate) ? rotate.Int : pageTreeMembers.Rotation;
        return new CropBox(part.InUserSpace(own.Bounds, rotation));
    }

    protected override PdfPageNode ProcessPage(
        int pageNumber, DictionaryToken dictionary, NamedDestinations namedDestinations, MediaBox mediaBox,
        CropBox cropBox, UserSpaceUnit userSpaceUnit, PageRotationDegrees rotation, TransformationMatrix initialMatrix,
        IReadOnlyList<IGraphicsStateOperation> operations)
    {
        var visible = cropBox.GetVisibleBounds(rotation);
        // Read onto a sheet, the page comes out where it lies on the sheet: shown space, then the sheet's.
        var flip = PdfSpace.Flip(visible.Height) * (_caches.Reading?.ToSheet ?? Matrix.Identity);
        var unsupported = new List<PdfUnsupported>();
        var text = new StringBuilder();

        var outputIntent = ResourceStore.GetPageOutputIntentProfile(dictionary);
        var interpreter = new PdfPageInterpreter(pageNumber, ResourceStore, PdfScanner, PageContentParser,
            FilterProvider, cropBox, userSpaceUnit, rotation, initialMatrix, outputIntent, ParsingOptions, _caches, flip,
            unsupported, text, onPage: true);
        var content = interpreter.Process(pageNumber, operations);

        var annotations = Annotations(pageNumber, dictionary, namedDestinations, cropBox, userSpaceUnit, rotation,
            initialMatrix, outputIntent, flip, unsupported);

        return new PdfPageNode(pageNumber, new Size(visible.Width, visible.Height),
            PdfSpace.ToMatrix(initialMatrix) * flip, content, annotations, text.ToString(), unsupported);
    }

    private IReadOnlyList<PdfAnnotationNode> Annotations(
        int pageNumber, DictionaryToken page, NamedDestinations destinations, CropBox cropBox, UserSpaceUnit unit,
        PageRotationDegrees rotation, TransformationMatrix initialMatrix, IIccProfile? outputIntent, Matrix flip,
        List<PdfUnsupported> unsupported)
    {
        IEnumerable<Annotation> read;
        try
        {
            read = new AnnotationProvider(PdfScanner, page, TransformationMatrix.Identity, destinations,
                ParsingOptions.Logger).GetAnnotations().ToList();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException or OutOfMemoryException))
        {
            unsupported.Add(new PdfUnsupported(PdfUnsupportedKind.DamagedContent, $"annotations: {ex.Message}"));
            return [];
        }

        var toPage = PdfSpace.ToMatrix(initialMatrix) * flip;
        var visible = cropBox.GetVisibleBounds(rotation);
        var shown = Rect.Transform(new Rect(0, 0, visible.Width, visible.Height), _caches.Reading?.ToSheet ?? Matrix.Identity);
        var annotations = new List<PdfAnnotationNode>();
        foreach (var annotation in read)
        {
            var flags = annotation.Flags;
            if (annotation.Type == AnnotationType.Popup || flags.HasFlag(AnnotationFlags.Hidden) ||
                flags.HasFlag(AnnotationFlags.NoView))
                continue;
            if (annotation.AnnotationDictionary.TryGet(OcKey, out IToken? layer) && !_caches.OptionalContent.Shows(layer))
                continue;

            var rect = Rectangle(annotation.AnnotationDictionary) ?? annotation.Rectangle;
    var bounds = PdfSpace.Transform(new Rect(new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Top)), toPage);

            // An annotation the page's crop box leaves out is no part of the page shown.
            if (!bounds.IntersectsWith(shown)) continue;

            PdfGroupNode? appearance = null;
            try
            {
                appearance = Appearance(pageNumber, annotation, rect, cropBox, unit, rotation, initialMatrix, outputIntent,
                    flip, unsupported);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException or OutOfMemoryException))
            {
                unsupported.Add(new PdfUnsupported(PdfUnsupportedKind.DamagedContent, $"{annotation.Type} appearance: {ex.Message}"));
            }

            annotations.Add(new PdfAnnotationNode(
                annotation.Type, bounds, appearance, Target(annotation.Action), annotation.Content,
                annotation.Type == AnnotationType.Widget ? FieldName(annotation.AnnotationDictionary) : null,
                flags.HasFlag(AnnotationFlags.Print)));
        }

        return annotations;
    }

    /// <summary>
    /// An annotation's normal appearance, placed as PDF places one (§12.5.5): the appearance's box, through its own
    /// matrix, is fitted to the annotation's rectangle.
    /// </summary>
    private PdfGroupNode? Appearance(
        int pageNumber, Annotation annotation, PdfRectangle rect, CropBox cropBox, UserSpaceUnit unit,
        PageRotationDegrees rotation, TransformationMatrix initialMatrix, IIccProfile? outputIntent, Matrix flip,
        List<PdfUnsupported> unsupported)
    {
        if (annotation.NormalAppearance is not { } appearances) return null;

        var stream = appearances.IsStateless
            ? appearances.StatelessAppearanceStream
            : annotation.AppearanceState is { } state && appearances.GetStates.Contains(state) ? appearances.Get(state) : null;
        if (stream is null) return null;

        var dictionary = stream.StreamDictionary;
        if (!dictionary.TryGet(BBoxKey, PdfScanner, out ArrayToken? boxArray) || boxArray.Length < 4) return null;
        var box = boxArray.ToRectangle(PdfScanner).Normalise();

        var formMatrix = TransformationMatrix.Identity;
        if (dictionary.TryGet(MatrixKey, PdfScanner, out ArrayToken? matrixArray) && matrixArray.Length >= 6)
            formMatrix = TransformationMatrix.FromArray(matrixArray.Data.OfType<NumericToken>().Select(static n => n.Double).ToArray());

        var placed = formMatrix.Transform(box);
        var left = Math.Min(Math.Min(placed.BottomLeft.X, placed.BottomRight.X), Math.Min(placed.TopLeft.X, placed.TopRight.X));
        var right = Math.Max(Math.Max(placed.BottomLeft.X, placed.BottomRight.X), Math.Max(placed.TopLeft.X, placed.TopRight.X));
        var bottom = Math.Min(Math.Min(placed.BottomLeft.Y, placed.BottomRight.Y), Math.Min(placed.TopLeft.Y, placed.TopRight.Y));
        var top = Math.Max(Math.Max(placed.BottomLeft.Y, placed.BottomRight.Y), Math.Max(placed.TopLeft.Y, placed.TopRight.Y));
        if (right - left <= 0 || top - bottom <= 0) return null;

        var sx = rect.Width / (right - left);
        var sy = rect.Height / (top - bottom);
        var fit = TransformationMatrix.FromValues(sx, 0, 0, sy, rect.Left - left * sx, rect.Bottom - bottom * sy);

        var reader = new PdfPageInterpreter(pageNumber, ResourceStore, PdfScanner, PageContentParser, FilterProvider,
            cropBox, unit, rotation, fit.Multiply(initialMatrix), outputIntent, ParsingOptions, _caches, flip, unsupported,
            text: null, onPage: true);
        var drawn = reader.ProcessForm(stream, NameToken.Create(annotation.Type.ToString()));
        return drawn.Children.Count == 0 ? null : new PdfGroupNode(PdfGroupRole.Appearance, drawn.Children, tag: annotation.Type.ToString());
    }

    private PdfRectangle? Rectangle(DictionaryToken annotation) =>
        annotation.TryGet(RectKey, PdfScanner, out ArrayToken? rect) && rect.Length >= 4
            ? rect.ToRectangle(PdfScanner).Normalise()
            : null;

    private static PdfLinkTarget? Target(PdfAction? action) => action switch
    {
        UriAction uri                                     => new PdfUriTarget(uri.Uri),
        AbstractGoToAction { Destination: { } destination } => new PdfPageTarget(destination.PageNumber, destination.Coordinates?.Top, destination.Coordinates?.Left),
        _                                                   => null,
    };

    /// <summary>A widget's field's fully qualified name: its own partial name after its ancestors', dot-separated.</summary>
    private string? FieldName(DictionaryToken widget)
    {
        var parts = new List<string>();
        var at = widget;
        for (var depth = 0; at is not null && depth < 32; depth++)
        {
            if (at.TryGet(NameKey, PdfScanner, out StringToken? partial) && !string.IsNullOrEmpty(partial.Data))
                parts.Add(partial.Data);
            at = at.TryGet(ParentKey, PdfScanner, out DictionaryToken? parent) ? parent : null;
        }

        if (parts.Count == 0) return null;
        parts.Reverse();
        return string.Join('.', parts);
    }
}
