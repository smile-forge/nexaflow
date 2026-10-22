using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// One thing a page draws. The tree is a PDF's content as objects rather than operators: what is drawn, with what,
/// through which clip and transparency, in painting order. Every node is immutable and placed in page space —
/// points, origin at the top left of the page as shown, y down — so the tree can be read, reordered and laid out
/// without the document it came from. A changed node is a new node, made by its <c>With…</c> methods.
/// </summary>
internal abstract class PdfContentNode
{
    /// <summary>
    /// Where the node can leave ink, in page space; <see cref="Rect.Empty"/> when it leaves none, and
    /// <see cref="PdfSpace.Everywhere"/> when it paints whatever its clip lets it (a shading painted with <c>sh</c>).
    /// </summary>
    public abstract Rect Bounds { get; }
}

/// <summary>Why a group exists — what in the document made its children belong together.</summary>
internal enum PdfGroupRole
{
    /// <summary>The page's own content, or a pattern cell's, a mask's or a glyph procedure's.</summary>
    Root,
    /// <summary>Content drawn after a clip was set (<c>W</c>, <c>W*</c>, or text in a clipping mode).</summary>
    Clip,
    /// <summary>A form XObject's content.</summary>
    Form,
    /// <summary>A transparency group: its children are composited together before its opacity and mask apply.</summary>
    Transparency,
    /// <summary>One drawable wrapped so a soft mask or blend mode from the graphics state applies to it.</summary>
    Effects,
    /// <summary>Content between <c>BMC</c>/<c>BDC</c> and <c>EMC</c>, kept with its tag.</summary>
    Marked,
    /// <summary>What a Type 3 font's glyph procedure draws.</summary>
    Glyph,
    /// <summary>An annotation's appearance.</summary>
    Appearance,
}

/// <summary>Content that shares a clip, a transparency treatment, or an origin in the document.</summary>
internal sealed class PdfGroupNode : PdfContentNode
{
    /// <summary>A group with nothing in it.</summary>
    public static PdfGroupNode Nothing { get; } = new(PdfGroupRole.Root, []);

    /// <param name="role">What made it a group.</param>
    /// <param name="children">What it draws, in painting order.</param>
    /// <param name="clip">The clip its children are drawn through, in page space; null for none.</param>
    /// <param name="clipRule">How <paramref name="clip"/> decides what is inside.</param>
    /// <param name="opacity">Applied to the composited children, 0..1.</param>
    /// <param name="mask">A soft mask over the composited children.</param>
    /// <param name="blend">How the composited children combine with what is beneath them.</param>
    /// <param name="tag">The marked-content tag or XObject name, when the document gave one.</param>
    public PdfGroupNode(
        PdfGroupRole role, IReadOnlyList<PdfContentNode> children, PdfShape? clip = null,
        FillRule clipRule = FillRule.Nonzero, double opacity = 1, PdfSoftMask? mask = null,
        BlendMode blend = BlendMode.Normal, string? tag = null)
    {
        Role     = role;
        Children = children;
        Clip     = clip;
        ClipRule = clipRule;
        Opacity  = opacity;
        Mask     = mask;
        Blend    = blend;
        Tag      = tag;
        Bounds   = Measure(children, clip);
    }

    public PdfGroupRole Role { get; }
    public IReadOnlyList<PdfContentNode> Children { get; }
    public PdfShape? Clip { get; }
    public FillRule ClipRule { get; }
    public double Opacity { get; }
    public PdfSoftMask? Mask { get; }
    public BlendMode Blend { get; }
    public string? Tag { get; }
    public override Rect Bounds { get; }

    /// <summary>Whether the group changes how its children look, rather than only keeping them together.</summary>
    public bool Affects => Clip is not null || Opacity < 1 || Mask is not null || Blend != BlendMode.Normal;

    /// <summary>The same group drawing <paramref name="children"/> instead.</summary>
    public PdfGroupNode WithChildren(IReadOnlyList<PdfContentNode> children) =>
        new(Role, children, Clip, ClipRule, Opacity, Mask, Blend, Tag);

    private static Rect Measure(IReadOnlyList<PdfContentNode> children, PdfShape? clip)
    {
        var ink = Rect.Empty;
        foreach (var child in children) ink.Union(child.Bounds);
        if (clip is null || ink.IsEmpty) return ink;

        ink.Intersect(clip.Bounds(Matrix.Identity));
        return ink;
    }
}

/// <summary>A filled and/or stroked path.</summary>
internal sealed class PdfPathNode : PdfContentNode
{
    /// <param name="shape">The outline, in the user space it was drawn in.</param>
    /// <param name="toPage">From that user space to page space.</param>
    /// <param name="rule">How filling decides what is inside.</param>
    /// <param name="fill">What the inside is painted with; null when the path is only stroked.</param>
    /// <param name="stroke">What the outline is stroked with; null when the path is only filled.</param>
    /// <param name="pen">How the stroke is shaped, in user space.</param>
    public PdfPathNode(PdfShape shape, Matrix toPage, FillRule rule, PdfPaint? fill, PdfPaint? stroke, PdfPen pen)
    {
        Shape  = shape;
        ToPage = toPage;
        Rule   = rule;
        Fill   = fill;
        Stroke = stroke;
        Pen    = pen;
        Bounds = Measure(shape, toPage, stroke is null ? 0 : pen.Width);
    }

    public PdfShape Shape { get; }
    public Matrix ToPage { get; }
    public FillRule Rule { get; }
    public PdfPaint? Fill { get; }
    public PdfPaint? Stroke { get; }
    public PdfPen Pen { get; }
    public override Rect Bounds { get; }

    private static Rect Measure(PdfShape shape, Matrix toPage, double strokeWidth)
    {
        var ink = shape.Bounds(toPage);
        if (ink.IsEmpty) return ink;

        // Half the stroke can fall outside the outline, scaled the way the page scales the user space; a zero-width
        // stroke is still a hairline a device pixel wide.
        var reach = Math.Max(strokeWidth * PdfSpace.LargestScale(toPage), 1) / 2;
        ink.Inflate(reach, reach);
        return ink;
    }
}

/// <summary>One glyph as a page sets it.</summary>
/// <param name="Outline">The glyph's outline in glyph space (one em to the unit), or null when it is drawn
/// otherwise — a Type 3 glyph's procedure — or leaves no ink, such as a space.</param>
/// <param name="ToPage">From glyph space to page space.</param>
/// <param name="Text">What the glyph says, as Unicode; empty when the document gives no mapping.</param>
/// <param name="Advance">How far the glyph moves the pen along the line, in glyph space (ems).</param>
/// <param name="Box">The glyph's advance box in page space — where a reader's selection of it lies.</param>
internal readonly record struct PdfGlyph(PdfShape? Outline, Matrix ToPage, string Text, double Advance, Rect Box);

/// <summary>
/// A run of glyphs set one after another in one font and one paint — what one text-showing operator draws.
/// </summary>
internal sealed class PdfTextNode : PdfContentNode
{
    /// <param name="glyphs">The glyphs, in the order they were set.</param>
    /// <param name="fill">What the glyphs are filled with; null when the rendering mode does not fill.</param>
    /// <param name="stroke">What the glyphs are outlined with; null when the rendering mode does not stroke.</param>
    /// <param name="pen">How the outline is shaped, its width in page space.</param>
    /// <param name="fontName">The font's name as the document gave it, without a subset prefix.</param>
    /// <param name="vertical">Whether the font writes top to bottom.</param>
    /// <param name="textStart">Where the run's text starts in its page's <see cref="PdfPageNode.Text"/>.</param>
    public PdfTextNode(
        IReadOnlyList<PdfGlyph> glyphs, PdfPaint? fill, PdfPaint? stroke, PdfPen pen, string fontName, bool vertical,
        int textStart = 0)
    {
        Glyphs    = glyphs;
        Fill      = fill;
        Stroke    = stroke;
        Pen       = pen;
        FontName  = fontName;
        Vertical  = vertical;
        TextStart = textStart;
        Text      = string.Concat(glyphs.Select(static g => g.Text));
        Bounds    = Measure(glyphs);
    }

    public IReadOnlyList<PdfGlyph> Glyphs { get; }
    public PdfPaint? Fill { get; }
    public PdfPaint? Stroke { get; }
    public PdfPen Pen { get; }
    public string FontName { get; }
    public bool Vertical { get; }
    public int TextStart { get; }

    /// <summary>The run's text: every glyph's, in order.</summary>
    public string Text { get; }

    public override Rect Bounds { get; }

    /// <summary>The same run, its text placed at <paramref name="start"/> in its page's text.</summary>
    public PdfTextNode WithTextStart(int start) =>
        start == TextStart ? this : new(Glyphs, Fill, Stroke, Pen, FontName, Vertical, start);

    private static Rect Measure(IReadOnlyList<PdfGlyph> glyphs)
    {
        var ink = Rect.Empty;
        foreach (var glyph in glyphs)
        {
            ink.Union(glyph.Box);
            if (glyph.Outline is { IsEmpty: false } outline) ink.Union(outline.Bounds(glyph.ToPage));
        }

        return ink;
    }
}

/// <summary>An image drawn into the unit square of the space it was placed in.</summary>
internal sealed class PdfImageNode : PdfContentNode
{
    /// <param name="picture">The image, decoded when it is first drawn.</param>
    /// <param name="toPage">From the unit square (0,0 bottom left, 1,1 top right, as PDF places images) to page space.</param>
    /// <param name="opacity">The graphics state's fill alpha.</param>
    /// <param name="stencil">For an image mask, the paint that shows through its set samples; null for an ordinary image.</param>
    public PdfImageNode(PdfPicture picture, Matrix toPage, double opacity, PdfPaint? stencil)
    {
        Picture = picture;
        ToPage  = toPage;
        Opacity = opacity;
        Stencil = stencil;
        Bounds  = PdfSpace.UnitSquare.Bounds(toPage);
    }

    public PdfPicture Picture { get; }
    public Matrix ToPage { get; }
    public double Opacity { get; }
    public PdfPaint? Stencil { get; }
    public override Rect Bounds { get; }
}

/// <summary>A shading painted over everything its clip allows (<c>sh</c>).</summary>
internal sealed class PdfShadingNode : PdfContentNode
{
    /// <param name="shading">The shading, in its own space.</param>
    /// <param name="toPage">From the shading's space to page space.</param>
    /// <param name="opacity">The graphics state's fill alpha.</param>
    public PdfShadingNode(PdfShading shading, Matrix toPage, double opacity)
    {
        Shading = shading;
        ToPage  = toPage;
        Opacity = opacity;
        Bounds  = shading.Bounds is { } box ? PdfSpace.Transform(box, toPage)
                : shading.Mesh is { } mesh ? PdfSpace.Transform(mesh.Bounds, toPage)
                : PdfSpace.Everywhere;
    }

    public PdfShading Shading { get; }
    public Matrix ToPage { get; }
    public double Opacity { get; }
    public override Rect Bounds { get; }
}
