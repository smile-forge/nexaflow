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
/// drawing, which is why this sits beside <see cref="Piece"/> rather than inside either of them.
/// </para>
/// </summary>
public abstract record LayoutMark
{
    /// <param name="fallback">
    /// The colour to use where the content did not ask for one — the theme's, passed at paint time, which
    /// is why it is not baked in: a theme can change without the content doing so.
    /// </param>
    public abstract void PaintOn(DrawingContext dc, Brush fallback);

    /// <summary>
    /// How far this drawing reaches, in the frame of the piece it belongs to.
    ///
    /// <para>
    /// Asked of the mark rather than handed in with it, because the mark is the only thing that knows.
    /// It is what a piece's own extent is worked out from, and a builder that had to state it twice is a
    /// builder that can state it wrongly.
    /// </para>
    /// </summary>
    public abstract Rect Covers { get; }
}

/// <summary>Glyphs already shaped and positioned — what a typesetter hands over.</summary>
public sealed record GlyphMark(GlyphRun Run, Brush? Foreground) : LayoutMark
{
    public override Rect Covers =>
        Rect.Offset(Run.ComputeInkBoundingBox(), Run.BaselineOrigin.X, Run.BaselineOrigin.Y);
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawGlyphRun(Foreground ?? fallback, Run);
}

/// <summary>
/// A run of text left for WPF to shape at paint time — what content that measures its own words uses,
/// where a typesetter that has already chosen every glyph uses <see cref="GlyphMark"/>.
/// </summary>
public sealed record TextMark(FormattedText Glyphs, Point At, Brush? Foreground) : LayoutMark
{
    /// <summary>
    /// Where the words land, which is not where the run was placed. Given room and an alignment the type
    /// engine does the shifting itself, so a centred title placed at the left margin draws in the middle of
    /// the page — and a piece that took its extent from the placement would be as wide as the column and
    /// would wash the margins either side of itself when selected.
    /// </summary>
    public override Rect Covers
    {
        get
        {
            var room = Glyphs.MaxTextWidth;
            if (double.IsNaN(room) || double.IsInfinity(room) || room <= 0) room = Glyphs.Width;

            var shift = Glyphs.TextAlignment switch
            {
                TextAlignment.Center => (room - Glyphs.Width) / 2,
                TextAlignment.Right => room - Glyphs.Width,
                _ => 0,
            };

            return new Rect(At.X + shift, At.Y, Glyphs.Width, Glyphs.Height);
        }
    }
    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        Glyphs.SetForegroundBrush(Foreground ?? fallback);
        dc.DrawText(Glyphs, At);
    }
}

/// <summary>A hairline: a fraction's bar, a strike, the stroke of a radical.</summary>
public sealed record LineMark(Point From, Point To, Brush? Foreground) : LayoutMark
{
    public override Rect Covers => new(From, To);
    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        var pen = new Pen(Foreground ?? fallback, 1.0);
        pen.Freeze();
        dc.DrawLine(pen, From, To);
    }
}

/// <summary>
/// A shape, filled and/or stroked: a music glyph drawn as an outline, a beam, a slur, a bracket, a stem.
///
/// <para>
/// The one mark a score needs that a formula did not. Its glyphs go down as filled outlines rather than as
/// text, deliberately — WPF's text pipeline gamma-corrects glyph coverage and visibly fattens a music
/// font's thin strokes — and the rest of what an engraver draws is neither a glyph nor an axis-aligned
/// rectangle: a beam is a parallelogram, a slur is a Bézier, and a stem is a line with a real thickness
/// where <see cref="LineMark"/>'s is fixed at one pixel.
/// </para>
/// <para>
/// The geometry is already positioned — a builder that knows where a piece landed is the only thing that
/// can place it, and nothing here should have to work it out a second time.
/// </para>
/// </summary>
public sealed record GeometryMark(Geometry Shape, Brush? Fill, Brush? Stroke, double Thickness) : LayoutMark
{
    public override Rect Covers => Shape.Bounds;
    /// <summary>A filled shape in whatever colour the content did not ask for.</summary>
    public static GeometryMark Filled(Geometry shape, Brush? fill = null) => new(shape, fill, null, 0);

    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        Pen? pen = null;
        if (Stroke is not null || Thickness > 0)
        {
            pen = new Pen(Stroke ?? fallback, Thickness);
            pen.Freeze();
        }

        dc.DrawGeometry(Fill is null && pen is not null ? null : Fill ?? fallback, pen, Shape);
    }
}

/// <summary>A filled rectangle: a rule, a bar of a barcode, a wash behind a piece.</summary>
public sealed record RuleMark(Rect Bounds, Brush? Foreground) : LayoutMark
{
    public override Rect Covers => Bounds;
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawRectangle(Foreground ?? fallback, null, Bounds);
}
