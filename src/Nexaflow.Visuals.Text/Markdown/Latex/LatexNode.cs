using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A piece of a typeset formula: where it sits, what part of the LaTeX it came from, and what it drew.
/// <para>
/// Holding the drawing on the node is what lets a formula be painted by walking its own tree rather than
/// by asking the typesetter to lay it out again. One pass produces structure, geometry and picture
/// together, so they cannot disagree — and a single term can be repainted, or revealed, on its own.
/// </para>
/// <para>
/// <b>Two coordinate frames meet here, deliberately.</b> <see cref="ILayoutNode.Bounds"/> is absolute and
/// normalised — where the piece really is on the element, which is what a caret, a wash and a hit test
/// need. The marks are raw, exactly as the typesetter handed them over, because the transforms that place
/// them are pushed onto the drawing context as the walk descends. Baking those into the marks instead
/// would lose the rotation an accent is drawn with.
/// </para>
/// </summary>
internal sealed class LatexNode : LayoutNode
{
    private readonly List<LatexMark> _marks = [];

    public LatexNode(Rect bounds, int sourceStart, int sourceLength, string kind, bool isInk)
        : base(bounds, sourceStart, sourceLength, kind, isInk)
    {
    }

    /// <summary>
    /// The typesetter's own reading of what this piece was laid out from. Kept because the geometry of a
    /// matrix's cells is still matched up through it, and for nothing else.
    /// <para>
    /// It is a typesetting tree: built to decide box sizes, so it drops braces and spacing, and a style
    /// atom names no parts at all. What a piece <em>is</em> comes from <see cref="Part"/>.
    /// </para>
    /// </summary>
    public XamlMath.IFormulaNode? Formula { get; set; }

    /// <summary>
    /// The part of the parse tree this piece was drawn from — the link back that says what it <em>is</em>
    /// rather than merely where it came from.
    /// <para>
    /// Several pieces share one: a fraction's box and its bar are one construct drawn in parts. What a
    /// piece is <em>to</em> the thing holding it lives on the parse tree, not here, which is why this is a
    /// reference and not a copy of anything — the layout tree stays about layout.
    /// </para>
    /// <para>
    /// Set once, when the formula is typeset. Today it is worked out by matching the stretch of source
    /// the box was named for, because the boxes were built by a parser of the typesetter's own; when they
    /// are built from this tree instead, each box will arrive already knowing and the matching goes.
    /// </para>
    /// </summary>
    public Nexaflow.Maths.Latex.TexPart? Part { get; set; }

    /// <summary>What this piece drew, in the order it drew it.</summary>
    public IReadOnlyList<LatexMark> Marks => _marks;

    /// <summary>
    /// The pixel grid this piece's edges snap to. WpfMath pushes one per box so glyph stems land on whole
    /// device pixels; without it the same formula rasterises a shade differently.
    /// </summary>
    public GuidelineSet? Guidelines { get; set; }

    /// <summary>Transforms to place this piece and everything under it — <c>\overrightarrow</c> and friends.</summary>
    public IReadOnlyList<Transform> Transforms { get; set; } = [];

    /// <summary>A wash behind the piece, from <c>\colorbox</c>, and where it goes. Painted under everything.</summary>
    public Brush? Background { get; set; }

    public Rect BackgroundBounds { get; set; }

    public void Drew(LatexMark mark) => _marks.Add(mark);
}

/// <summary>One thing a piece of a formula drew.</summary>
internal abstract record LatexMark
{
    /// <param name="fallback">
    /// The colour to use where the formula did not ask for one — the theme's, passed at paint time, which
    /// is why it is not baked in: a theme can change without the formula doing so.
    /// </param>
    public abstract void PaintOn(DrawingContext dc, Brush fallback);
}

internal sealed record GlyphMark(GlyphRun Run, Brush? Foreground) : LatexMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawGlyphRun(Foreground ?? fallback, Run);
}

internal sealed record LineMark(Point From, Point To, Brush? Foreground) : LatexMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        var pen = new Pen(Foreground ?? fallback, 1.0);
        pen.Freeze();
        dc.DrawLine(pen, From, To);
    }
}

internal sealed record RuleMark(Rect Bounds, Brush? Foreground) : LatexMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawRectangle(Foreground ?? fallback, null, Bounds);
}
