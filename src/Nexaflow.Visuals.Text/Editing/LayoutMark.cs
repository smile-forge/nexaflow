using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// One thing a piece of laid-out content drew.
///
/// <para>
/// Holding the drawing on the layout tree is what lets content be painted by walking the same tree that
/// answers where the caret goes and what a drag selected. One pass produces structure, geometry and
/// picture together, so they cannot disagree about where anything is — and a single piece can be
/// repainted on its own.
/// </para>
/// <para>
/// The vocabulary is deliberately small and content-agnostic. A formula and a barcode are, at this level,
/// the same two things: marks on a page at coordinates. Neither needs the other's notion of what it is
/// drawing, which is why this sits beside <see cref="ILayoutNode"/> rather than inside either of them.
/// </para>
/// </summary>
public abstract record LayoutMark
{
    /// <param name="fallback">
    /// The colour to use where the content did not ask for one — the theme's, passed at paint time, which
    /// is why it is not baked in: a theme can change without the content doing so.
    /// </param>
    public abstract void PaintOn(DrawingContext dc, Brush fallback);
}

/// <summary>Glyphs already shaped and positioned — what a typesetter hands over.</summary>
public sealed record GlyphMark(GlyphRun Run, Brush? Foreground) : LayoutMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawGlyphRun(Foreground ?? fallback, Run);
}

/// <summary>
/// A run of text left for WPF to shape at paint time — what content that measures its own words uses,
/// where a typesetter that has already chosen every glyph uses <see cref="GlyphMark"/>.
/// </summary>
public sealed record TextMark(FormattedText Glyphs, Point At, Brush? Foreground) : LayoutMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        Glyphs.SetForegroundBrush(Foreground ?? fallback);
        dc.DrawText(Glyphs, At);
    }
}

/// <summary>A hairline: a fraction's bar, a strike, the stroke of a radical.</summary>
public sealed record LineMark(Point From, Point To, Brush? Foreground) : LayoutMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        var pen = new Pen(Foreground ?? fallback, 1.0);
        pen.Freeze();
        dc.DrawLine(pen, From, To);
    }
}

/// <summary>A filled rectangle: a rule, a bar of a barcode, a wash behind a piece.</summary>
public sealed record RuleMark(Rect Bounds, Brush? Foreground) : LayoutMark
{
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawRectangle(Foreground ?? fallback, null, Bounds);
}
