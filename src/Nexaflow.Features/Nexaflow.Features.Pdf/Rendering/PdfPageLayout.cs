using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using UglyToad.PdfPig.Graphics.Core;
using System.Collections.Generic;
using System.Threading;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// Lays a <see cref="PdfPageNode"/> out as a <see cref="LayoutTree"/>: the shared layout tree every document in the app
/// is drawn from, so a PDF page is painted, culled, kept and pressed by the same engine as a markdown page.
/// <para>
/// A page is laid once, at <see cref="DipsPerPoint"/> — a point is 1/72 inch and a device-independent pixel 1/96 — and
/// zooming it is the element's scale, never a new layout: the marks are vectors, so a page laid once is sharp at every
/// size. Every piece sits at the page's origin, so a piece's own frame is page space and a clip or mask from the tree
/// applies as it was read.
/// </para>
/// </summary>
internal static class PdfPageLayout
{
    /// <summary>Device-independent pixels to a PDF point.</summary>
    public const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>
    /// <paramref name="page"/> as a layout tree, its paper painted with <paramref name="paper"/>. Laying is checked against
    /// <paramref name="ct"/> group by group, since a page of many blends composites each of them.
    /// </summary>
    public static LayoutTree Lay(PdfPageNode page, Brush paper, CancellationToken ct = default) =>
        new PdfLaying(new PdfBrushes(new Rect(0, 0, page.Size.Width * DipsPerPoint, page.Size.Height * DipsPerPoint)), ct).Page(page, paper);
}

/// <summary>The pieces a laid PDF page is made of.</summary>
internal static class PdfPieces
{
    /// <summary>The page itself: its paper, and everything on it.</summary>
    public const string Page = "pdf-page";

    /// <summary>Content drawn through one clip, fade or mask.</summary>
    public const string Group = "pdf-group";

    /// <summary>One run of text, standing for its stretch of the page's text.</summary>
    public const string Text = "pdf-text";

    /// <summary>An annotation over the page: its appearance, and what pressing it does.</summary>
    public const string Annotation = "pdf-annotation";

    /// <summary>Paths, images and shadings painted one after another, between the pieces before and after them.</summary>
    public const string Ink = "pdf-ink";
}

/// <summary>One page, or one pattern cell or mask, being laid.</summary>
internal sealed class PdfLaying
{
    private static readonly Matrix Scale = new(PdfPageLayout.DipsPerPoint, 0, 0, PdfPageLayout.DipsPerPoint, 0, 0);

    private readonly PdfBrushes _brushes;
    private readonly LayoutBuilder _into = new();

    /// <summary>Whether an ink piece is open, gathering the drawables met one after another.</summary>
    private bool _inking;

    /// <summary>
    /// How finely a blended group is composited, in pixels to a device-independent pixel: twice the layout's resolution,
    /// so a page read at up to twice its size shows no step where blending happened.
    /// </summary>
    private const double BlendResolution = 2;

    /// <summary>The most pixels one blended group is composited in, whatever its size.</summary>
    private const double BlendPixels = 8_000_000;

    /// <summary>The page being laid, when a page is: what a blended group's backdrop is read from.</summary>
    private PdfPageNode? _page;

    private Brush _paper = Brushes.White;

    /// <summary>What lies wholly beneath what is being laid: the page's content, under an annotation's appearance.</summary>
    private PdfGroupNode? _beneath;

    /// <summary>The groups being laid, outermost first: the way down to the node at hand.</summary>
    private readonly List<PdfGroupNode> _chain = [];

    /// <summary>
    /// The page's blended groups composited so far, by node. Each is composited once, against what the page painted
    /// before it, and the backdrop of a later blend draws that finished picture rather than compositing it again: a page
    /// of many blends costs one composite apiece, not one for every blend beneath every other.
    /// </summary>
    private readonly Dictionary<PdfGroupNode, PictureMark> _composited;

    /// <summary>
    /// Whether this laying is the page's own, so that its composites are the page's. A backdrop's are not kept: a
    /// backdrop holds only what reaches the blend it is laid for, so a composite made in it is cropped to that blend.
    /// </summary>
    private readonly bool _keepsComposites;

    private readonly CancellationToken _cancel;

    public PdfLaying(PdfBrushes brushes, CancellationToken cancel = default)
        : this(brushes, new Dictionary<PdfGroupNode, PictureMark>(ReferenceEqualityComparer.Instance), true, cancel)
    {
    }

    private PdfLaying(PdfBrushes brushes, Dictionary<PdfGroupNode, PictureMark> composited, bool keepsComposites,
        CancellationToken cancel)
    {
        _brushes = brushes;
        _composited = composited;
        _keepsComposites = keepsComposites;
        _cancel = cancel;
    }

    /// <summary>The page, its paper first.</summary>
    public LayoutTree Page(PdfPageNode page, Brush paper)
    {
        _page = page;
        _paper = paper;
        Paper(page);
        Walk(page.Content);

        foreach (var annotation in page.Annotations)
        {
            _beneath = page.Content;
            Annotation(annotation);
        }

        _beneath = null;
        Close();
        return _into.Seal();
    }

    /// <summary>Opens the page piece and lays its paper.</summary>
    private void Paper(PdfPageNode page)
    {
        var size = Unpainted(page);
        _into.Draw(new WashMark(size, _paper));
    }

    /// <summary>Opens the page piece, clipped to the page, with nothing yet painted on it; the page's extent.</summary>
    private Rect Unpainted(PdfPageNode page)
    {
        var size = new Rect(0, 0, page.Size.Width * PdfPageLayout.DipsPerPoint, page.Size.Height * PdfPageLayout.DipsPerPoint);
        _into.Open(PdfPieces.Page, paints: new LayoutPaint(Clip: new RectangleGeometry(size).Frozen()));
        return size;
    }

    /// <summary>
    /// What a page paints before a blended group, laid as a page of its own on no paper. A page is a transparency group
    /// of its own, painted on nothing and laid on its paper only once it is whole (ISO 32000-1, 11.4.7), so the paper is
    /// no part of what a blend sees: where the page has painted nothing, nothing lies beneath. A blended group inside it
    /// is drawn as the page composited it.
    /// </summary>
    private LayoutTree Ground(PdfPageNode page, PdfGroupNode content)
    {
        _page = page;
        Unpainted(page);
        Walk(content);
        Close();
        return _into.Seal();
    }

    /// <summary>A group laid on its own, on nothing: the source of a blend.</summary>
    private LayoutTree Alone(PdfGroupNode group)
    {
        _into.Open(PdfPieces.Group);
        Group(group);
        Close();
        return _into.Seal();
    }

    /// <summary>Lays a group's children with the group on the way down to them.</summary>
    private void Walk(PdfGroupNode group)
    {
        _chain.Add(group);
        Children(group);
        _chain.RemoveAt(_chain.Count - 1);
    }

    /// <summary>A group on its own — a mask's, a pattern cell's — as a drawing, in the space its nodes are placed in.</summary>
    public Drawing Drawing(PdfGroupNode group)
    {
        // A mask's or a pattern cell's drawing has no page beneath it, so a blend inside it is drawn as Normal; the
        // interpreter reports those.
        _into.Open(PdfPieces.Group);
        Group(group);
        Close();
        var tree = _into.Seal();

        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
            LayoutPainter.Paint(dc, tree.Root, Brushes.Black);
        drawing.Freeze();
        return drawing;
    }

    /// <summary>
    /// Draws a path's, an image's or a shading's mark into the ink piece open at this point of the page, opening one when
    /// none is. A piece paints its own marks before the pieces inside it, so marks and pieces never share a piece: what a
    /// page paints between two groups gathers in an ink piece of its own, and the pieces stand in painting order.
    /// </summary>
    private void Draw(LayoutMark mark)
    {
        if (!_inking)
        {
            _into.Open(PdfPieces.Ink);
            _inking = true;
        }

        _into.Draw(mark);
    }

    /// <summary>Opens a piece where the page's painting has reached, after the ink before it.</summary>
    private void Open(string kind, ISourcePart? part = null, LayoutPaint? paints = null)
    {
        EndInk();
        _into.Open(kind, part, paints: paints);
    }

    /// <summary>Closes the innermost piece, and the ink gathered last inside it.</summary>
    private void Close()
    {
        EndInk();
        _into.Close();
    }

    private void EndInk()
    {
        if (!_inking) return;
        _inking = false;
        _into.Close();
    }

    private void Children(PdfGroupNode group)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case PdfGroupNode nested: Group(nested); break;
                case PdfPathNode path:    Path(path); break;
                case PdfTextNode text:    Text(text); break;
                case PdfImageNode image:  Image(image); break;
                case PdfShadingNode sh:   Shading(sh); break;
            }
        }
    }

    /// <summary>
    /// A group that changes how its children look is a piece of its own, carrying that change; a group that only keeps
    /// them together — a form, a transparency group at full strength — lays them where it is. A group blended other than
    /// normally is composited against what the page has painted before it; under a later blend, it is drawn as it was
    /// composited.
    /// </summary>
    private void Group(PdfGroupNode group)
    {
        _cancel.ThrowIfCancellationRequested();

        if (group.Blend != BlendMode.Normal && _page is not null)
        {
            if (!_keepsComposites && _composited.TryGetValue(group, out var composite)) Draw(composite);
            else Blended(group, _page);
            return;
        }

        if (!group.Affects)
        {
            Walk(group);
            return;
        }

        var clip = group.Clip?.ToGeometry(Scale, group.ClipRule);
        var mask = group.Mask is { } softMask ? _brushes.Mask(softMask) : null;
        Open(PdfPieces.Group, paints: new LayoutPaint(Clip: clip, Opacity: group.Opacity, Mask: mask));
        Walk(group);
        Close();
    }

    /// <summary>
    /// A group blended by a mode other than Normal. What the page has painted under it is drawn to a picture, on no paper,
    /// the group alone is drawn to another, and the two are combined by the mode (<see cref="PdfBlending"/>) into a picture laid
    /// where the group stands in painting order — as far as the group reaches and its clips allow, and no further, so the
    /// rest of the page stays vector. The group's text keeps its words, so it can still be selected.
    /// </summary>
    private void Blended(PdfGroupNode group, PdfPageNode page)
    {
        var reach = group.Bounds;
        reach.Intersect(new Rect(page.Size));
        foreach (var around in _chain)
            if (around.Clip is { } clip) reach.Intersect(clip.Bounds(Matrix.Identity));
        if (reach.IsEmpty || reach.Width <= 0 || reach.Height <= 0) return;

        var region = PdfSpace.Transform(reach, Scale);
        var resolution = Math.Min(BlendResolution, Math.Sqrt(BlendPixels / (region.Width * region.Height)));

        var before = Before(_chain[0], 1, group, reach);
        if (_beneath is not null) before = new PdfGroupNode(PdfGroupRole.Root, [_beneath, before]);

        var ground = new PdfLaying(_brushes, _composited, false, _cancel).Ground(page, before);
        var backdrop = LayoutPictures.Painted(ground.Root, region, resolution, Brushes.Black, null);

        var alone = new PdfGroupNode(group.Role, group.Children, group.Clip, group.ClipRule, group.Opacity, group.Mask,
            BlendMode.Normal, group.Tag);
        var source = LayoutPictures.Painted(new PdfLaying(_brushes, _cancel).Alone(alone).Root, region, resolution, Brushes.Black, null);
        if (backdrop is null || source is null) return;

        var composite = new PictureMark(PdfBlending.Composite(backdrop, source, group.Blend), region);
        if (_keepsComposites) _composited[group] = composite;
        Draw(composite);

        EndInk();
        foreach (var text in Texts(group))
            if (!text.Vertical && text.Text.Length > 0) Words(text);
    }

    /// <summary>
    /// What <paramref name="at"/> paints before <paramref name="target"/>, as a group of the same groups: everything ahead
    /// of the way down to it, the groups on that way kept with their clips and effects. Only what reaches
    /// <paramref name="reach"/> is kept, since nothing else can show under the target.
    /// </summary>
    private PdfGroupNode Before(PdfGroupNode at, int depth, PdfGroupNode target, Rect reach)
    {
        var next = depth < _chain.Count ? _chain[depth] : (PdfContentNode)target;
        var kept = new List<PdfContentNode>();
        foreach (var child in at.Children)
        {
            if (ReferenceEquals(child, next))
            {
                if (depth < _chain.Count) kept.Add(Before(_chain[depth], depth + 1, target, reach));
                break;
            }

            if (child.Bounds.IntersectsWith(reach)) kept.Add(child);
        }

        return at.WithChildren(kept);
    }

    private static IEnumerable<PdfTextNode> Texts(PdfGroupNode group)
    {
        foreach (var child in group.Children)
        {
            if (child is PdfTextNode text) yield return text;
            else if (child is PdfGroupNode nested)
                foreach (var inner in Texts(nested)) yield return inner;
        }
    }

    private void Path(PdfPathNode path)
    {
        var toDips = path.ToPage * Scale;
        var fill = path.Fill is null ? null : _brushes.Paint(path.Fill);

        if (path.Stroke is null)
        {
            if (fill is not null) Fill(path.Shape.ToGeometry(toDips, path.Rule), fill, path.Fill!);
            return;
        }

        var stroke = _brushes.Paint(path.Stroke);
        if (stroke is null)
        {
            if (fill is not null) Fill(path.Shape.ToGeometry(toDips, path.Rule), fill, path.Fill!);
            return;
        }

        if (PdfSpace.IsUniform(toDips) || path.Pen.Width <= 0)
        {
            var geometry = path.Shape.ToGeometry(toDips, path.Rule);
            if (fill is not null) Fill(geometry, fill, path.Fill!);
            Draw(Stroked(geometry, stroke, path.Pen, PdfSpace.MeanScale(toDips)));
            return;
        }

        // A stroke through a transform that stretches one way more than the other is not a pen any WPF stroke can
        // draw: the line is thicker across one axis than the other. Its outline is widened in the space it was drawn in,
        // where the pen is round, and that outline is filled through the transform.
        if (fill is not null) Fill(path.Shape.ToGeometry(toDips, path.Rule), fill, path.Fill!);
        var user = path.Shape.ToGeometry(Matrix.Identity, path.Rule);
        var pen = Pen(path.Pen, 1, Brushes.Black);
        var widened = user.GetWidenedPathGeometry(pen);
        widened.Transform = new MatrixTransform(toDips);
        Draw(GeometryMark.Filled(widened.Frozen(), stroke));
    }

    /// <summary>
    /// A fill, cut to where its paint paints: a shading that does not extend past its ends leaves the rest of the shape
    /// unpainted.
    /// </summary>
    private void Fill(Geometry shape, Brush fill, PdfPaint paint)
    {
        if (paint is PdfShadingPaint shading && _brushes.Reach(shading.Shading, shading.ToPage * Scale) is { } reach)
            shape = new CombinedGeometry(GeometryCombineMode.Intersect, shape, reach).Frozen();

        Draw(GeometryMark.Filled(shape, fill));
    }

    private static GeometryMark Stroked(Geometry shape, Brush stroke, PdfPen pen, double scale)
    {
        // Zero is PDF's "the thinnest line the device draws"; a device-independent pixel is the thinnest a page laid once
        // for every zoom can promise.
        var width = pen.Width * scale;
        var thickness = width > 0 ? width : 1;
        var thicknessInUser = thickness / Math.Max(scale, 1e-9);
        return new GeometryMark(shape, null, stroke, thickness)
        {
            Dashes = Dashes(pen, thicknessInUser),
            Outline = new LayoutPen(pen.Cap, pen.Cap, pen.Cap, pen.Join, Math.Max(pen.MiterLimit, 1),
                pen.Dashes.Count > 0 ? pen.DashPhase / thicknessInUser : 0),
        };
    }

    /// <summary>A PDF dash array, in user-space lengths, as WPF wants it: in multiples of the line's thickness.</summary>
    private static DoubleCollection? Dashes(PdfPen pen, double thicknessInUser)
    {
        if (pen.Dashes.Count == 0) return null;

        var dashes = new DoubleCollection(pen.Dashes.Count % 2 == 0 ? pen.Dashes.Count : pen.Dashes.Count * 2);
        var repeat = pen.Dashes.Count % 2 == 0 ? 1 : 2;
        for (var r = 0; r < repeat; r++)
            foreach (var length in pen.Dashes)
                dashes.Add(length / thicknessInUser);
        dashes.Freeze();
        return dashes;
    }

    private static Pen Pen(PdfPen pen, double scale, Brush brush)
    {
        var made = new Pen(brush, Math.Max(pen.Width * scale, 1e-3))
        {
            StartLineCap = pen.Cap,
            EndLineCap = pen.Cap,
            DashCap = pen.Cap,
            LineJoin = pen.Join,
            MiterLimit = Math.Max(pen.MiterLimit, 1),
        };
        if (pen.Dashes.Count > 0)
            made.DashStyle = new DashStyle(Dashes(pen, Math.Max(pen.Width, 1e-9)), pen.DashPhase / Math.Max(pen.Width, 1e-9));
        made.Freeze();
        return made;
    }

    /// <summary>
    /// A run of text: its glyphs, one outline filled or stroked as the rendering mode says, and over them the run's text
    /// set in an installed font and stretched to lie where the glyphs do — invisible, and what a press, a drag and a
    /// search land on. The glyphs are the page's own; the words are how a reader reaches them.
    /// </summary>
    private void Text(PdfTextNode text)
    {
        Open(PdfPieces.Text);

        var fill   = text.Fill is null ? null : _brushes.Paint(text.Fill);
        var stroke = text.Stroke is null ? null : _brushes.Paint(text.Stroke);
        if (fill is not null || stroke is not null)
        {
            var outlines = new StreamGeometry { FillRule = FillRule.Nonzero };
            var any = false;
            using (var context = outlines.Open())
            {
                foreach (var glyph in text.Glyphs)
                {
                    if (glyph.Outline is not { IsEmpty: false } outline) continue;
                    outline.StreamTo(context, glyph.ToPage * Scale);
                    any = true;
                }
            }

            if (any)
            {
                outlines.Freeze();
                if (fill is not null)
                {
                    if (text.Fill is PdfShadingPaint shading && _brushes.Reach(shading.Shading, shading.ToPage * Scale) is { } reach)
                        _into.Draw(GeometryMark.Filled(new CombinedGeometry(GeometryCombineMode.Intersect, outlines, reach).Frozen(), fill));
                    else
                        _into.Draw(GeometryMark.Filled(outlines, fill));
                }

                if (stroke is not null) _into.Draw(Stroked(outlines, stroke, text.Pen, PdfPageLayout.DipsPerPoint));
            }
        }

        if (!text.Vertical && text.Text.Length > 0) Words(text);

        Close();
    }

    /// <summary>
    /// The run's text as words a caret can stand in: set in the installed face nearest the run's font, at the run's em
    /// size, then stretched along the run's baseline from its first glyph's origin to its last glyph's advance — so a
    /// press on a glyph lands on that glyph's letter, give or take how the two faces divide the line between them.
    /// </summary>
    private void Words(PdfTextNode text)
    {
        var first = text.Glyphs[0];
        var last = text.Glyphs[^1];
        var toDipsFirst = first.ToPage * Scale;
        var origin = toDipsFirst.Transform(new Point(0, 0));
        var end = (last.ToPage * Scale).Transform(new Point(last.Advance, 0));
        var along = end - origin;
        var em = toDipsFirst.Transform(new Vector(0, 1)).Length;
        if (along.Length <= 0 || em <= 0) return;

        var words = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            PdfTypefaces.For(text.FontName), em, Brushes.Black, 1.0);
        var setWidth = words.WidthIncludingTrailingWhitespace;
        if (setWidth <= 0) return;

        var turn = new Matrix();
        turn.Translate(0, -words.Baseline);
        turn.Scale(along.Length / setWidth, 1);
        turn.Rotate(Math.Atan2(along.Y, along.X) * 180 / Math.PI);
        turn.Translate(origin.X, origin.Y);

        _into.Open(PdfPieces.Text, new SourceSpan(text.TextStart, text.Text.Length),
            paints: new LayoutPaint(Turn: [new MatrixTransform(turn).Frozen()]));
        _into.Words(new LayoutWords(words, default, Maps: true));
        _into.Covers(new Rect(0, 0, setWidth, words.Height));
        _into.Close();
    }

    private void Image(PdfImageNode image)
    {
        var bitmap = image.Picture.Bitmap;
        var pixels = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);

        // From the bitmap's pixels (row 0 at the top) to the unit square PDF draws images into (row 0 at y = 1), then on
        // to the page.
        var placed = new Matrix(1.0 / pixels.Width, 0, 0, -1.0 / pixels.Height, 0, 1) * image.ToPage * Scale;

        if (image.Stencil is { } stencil)
        {
            Stencil(image, stencil, placed, pixels);
            return;
        }

        Placed(bitmap, placed, pixels, image.Opacity);
    }

    /// <summary>
    /// An image mask: the paint it is set with, shown through its set samples. One colour is the commonest case by far
    /// — scanned text, a black-and-white logo — and is the mask's own one-bit picture in that colour; anything else is
    /// the paint laid over the mask's area with the mask as its alpha.
    /// </summary>
    private void Stencil(PdfImageNode image, PdfPaint paint, Matrix placed, Rect pixels)
    {
        if (paint is PdfSolidPaint solid)
        {
            Placed(_brushes.Coloured(image.Picture, solid.Colour), placed, pixels, image.Opacity);
            return;
        }

        if (_brushes.Paint(paint) is not { } brush) return;

        var area = new RectangleGeometry(pixels) { Transform = new MatrixTransform(placed) }.Frozen();
        var mask = new ImageBrush(image.Picture.Bitmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = pixels,
            Stretch = Stretch.Fill,
            Transform = new MatrixTransform(placed),
        }.Frozen();

        Open(PdfPieces.Group, paints: new LayoutPaint(Opacity: image.Opacity, Mask: mask));
        Draw(GeometryMark.Filled(area, brush));
        Close();
    }

    /// <summary>
    /// A bitmap where <paramref name="placed"/> puts it. Upright and unturned, as nearly every image is, it is a picture
    /// in a rectangle; turned, skewed or mirrored, it fills the parallelogram it lands on as an image brush.
    /// </summary>
    private void Placed(ImageSource bitmap, Matrix placed, Rect pixels, double opacity)
    {
        var faded = opacity < 1;
        if (faded) Open(PdfPieces.Group, paints: new LayoutPaint(Opacity: opacity));

        if (placed.M12 == 0 && placed.M21 == 0 && placed.M11 > 0 && placed.M22 > 0)
        {
            Draw(new PictureMark(bitmap, PdfSpace.Transform(pixels, placed)));
        }
        else
        {
            var area = new RectangleGeometry(pixels) { Transform = new MatrixTransform(placed) }.Frozen();
            var brush = new ImageBrush(bitmap)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = pixels,
                Stretch = Stretch.Fill,
                Transform = new MatrixTransform(placed),
            }.Frozen();
            Draw(GeometryMark.Filled(area, brush));
        }

        if (faded) Close();
    }

    /// <summary>A shading painted over its clip: as far as the shading reaches, or everywhere when it extends.</summary>
    private void Shading(PdfShadingNode node)
    {
        var toDips = node.ToPage * Scale;
        if (_brushes.Gradient(node.Shading, toDips, node.Opacity) is not { } brush) return;

        var reach = _brushes.Reach(node.Shading, toDips);
        Geometry area = node.Shading.Bounds is { } box
            ? new RectangleGeometry(box) { Transform = new MatrixTransform(toDips) }.Frozen()
            : new RectangleGeometry(PdfSpace.Everywhere).Frozen();
        if (reach is not null) area = new CombinedGeometry(GeometryCombineMode.Intersect, area, reach).Frozen();

        Draw(GeometryMark.Filled(area, brush));
    }

    /// <summary>
    /// An annotation: its appearance drawn over the page, and, for a link, a piece of its own covering its rectangle that a
    /// press lands on. The press piece holds nothing, because a press means the deepest piece standing where it lands, and a
    /// piece holding others is never that.
    /// </summary>
    private void Annotation(PdfAnnotationNode annotation)
    {
        if (annotation.Appearance is { } appearance)
        {
            Open(PdfPieces.Annotation);
            Group(appearance);
            Close();
        }

        var href = annotation.Link switch
        {
            PdfUriTarget uri       => uri.Uri,
            PdfPageTarget target   => PdfLinks.ToPage(target),
            _                      => null,
        };
        if (href is null) return;

        var bounds = PdfSpace.Transform(annotation.Bounds, Scale);
        Open(PdfPieces.Annotation);
        _into.Covers(bounds);
        _into.Occupies(new RectangleGeometry(bounds).Frozen());
        _into.Links(href, annotation.Link is PdfUriTarget ? href : annotation.Contents);
        Close();
    }
}
