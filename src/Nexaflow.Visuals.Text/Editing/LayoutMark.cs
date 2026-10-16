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
    /// Paints it moved along by <paramref name="offset"/> — where the piece it belongs to stands, worked out by whatever is
    /// painting rather than pushed as a transform. A transform pushed while a picture is being kept is a whole group of its
    /// own in that picture, one for every piece, so a mark that can simply be drawn further along is. What cannot is moved
    /// by a transform of its own.
    /// </summary>
    public virtual void PaintOn(DrawingContext dc, Brush fallback, Vector offset)
    {
        if (offset is { X: 0, Y: 0 })
        {
            PaintOn(dc, fallback);

            return;
        }

        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        PaintOn(dc, fallback);
        dc.Pop();
    }

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

    public override void PaintOn(DrawingContext dc, Brush fallback, Vector offset)
    {
        Glyphs.SetForegroundBrush(Foreground ?? fallback);
        dc.DrawText(Glyphs, At + offset);
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

    public override void PaintOn(DrawingContext dc, Brush fallback, Vector offset)
    {
        var pen = new Pen(Foreground ?? fallback, 1.0);
        pen.Freeze();
        dc.DrawLine(pen, From + offset, To + offset);
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

    /// <summary>
    /// The dashes the outline is broken into, as a pen takes them — lengths in multiples of the thickness, a dash then a gap —
    /// or null for a solid line: a dotted edge, a reply in a sequence diagram.
    /// </summary>
    public DoubleCollection? Dashes { get; init; }

    /// <summary>
    /// How the outline's ends, corners and dashes are drawn, or null for a pen's own — see <see cref="LayoutPen"/>.
    /// </summary>
    public LayoutPen? Outline { get; init; }

    /// <summary>A filled shape in whatever colour the content did not ask for.</summary>
    public static GeometryMark Filled(Geometry shape, Brush? fill = null) => new(shape, fill, null, 0);

    public override void PaintOn(DrawingContext dc, Brush fallback)
    {
        var pen = Stroke is not null || Thickness > 0 ? PenFor(Stroke ?? fallback) : null;

        dc.DrawGeometry(Fill is null && pen is not null ? null : Fill ?? fallback, pen, Shape);
    }

    /// <summary>
    /// The pen the outline is drawn with in <paramref name="ink"/>, built and frozen the first time it is asked for and handed
    /// over from then on. A page of outlines is painted again on every caret blink and every scroll, and a pen built for each
    /// paint is a pen and its dashes made and frozen for every outline, every time.
    /// </summary>
    private Pen PenFor(Brush ink)
    {
        if (_penned is { } built && ReferenceEquals(built.Mark, this) && ReferenceEquals(built.Ink, ink)) return built.Pen!;

        var pen = new Pen(ink, Thickness);

        if (Outline is { } outline)
        {
            pen.StartLineCap = outline.StartCap;
            pen.EndLineCap = outline.EndCap;
            pen.DashCap = outline.DashCap;
            pen.LineJoin = outline.Join;
            pen.MiterLimit = outline.MiterLimit;
        }

        if (Dashes is { Count: > 0 }) pen.DashStyle = new DashStyle(Dashes, Outline?.DashOffset ?? 0);
        pen.Freeze();

        _penned = new Penned(this, ink, pen);
        return pen;
    }

    private Penned _penned = Penned.None;

    /// <summary>
    /// The pen a mark last built, with what it was built for: the mark itself, because a copy made with <c>with</c> starts from
    /// the original's fields and may differ in what its pen is made of, and the ink, because a mark in no colour of its own is
    /// drawn in whatever the theme hands it.
    ///
    /// <para>
    /// Equal to every other, so whether two marks are equal is still a question of what they draw, and never of whether one of
    /// them has been painted yet.
    /// </para>
    /// </summary>
    private sealed class Penned(GeometryMark? mark, Brush? ink, Pen? pen)
    {
        /// <summary>What a mark holds before it has built a pen.</summary>
        public static readonly Penned None = new(null, null, null);

        public GeometryMark? Mark { get; } = mark;

        public Brush? Ink { get; } = ink;

        public Pen? Pen { get; } = pen;

        public override bool Equals(object? obj) => obj is Penned;

        public override int GetHashCode() => 0;
    }
}

/// <summary>A filled rectangle: a rule, a bar of a barcode, a wash behind a piece.</summary>
public sealed record RuleMark(Rect Bounds, Brush? Foreground) : LayoutMark
{
    public override Rect Covers => Bounds;
    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawRectangle(Foreground ?? fallback, null, Bounds);

    public override void PaintOn(DrawingContext dc, Brush fallback, Vector offset) =>
        dc.DrawRectangle(Foreground ?? fallback, null, Rect.Offset(Bounds, offset));
}

/// <summary>
/// A picture, drawn into the box it was fitted to.
///
/// <para>
/// The one mark whose content came from outside the document. Everything else in the vocabulary is
/// something a builder worked out from the source; this is a thing a host handed over, and all the tree
/// does with it is say where it goes. What it is — a file on disk, a frame of a language pack, bytes a
/// viewer already had — is settled long before a builder sees it.
/// </para>
/// </summary>
public sealed record PictureMark(ImageSource Picture, Rect Bounds) : LayoutMark
{
    public override Rect Covers => Bounds;

    public override void PaintOn(DrawingContext dc, Brush fallback) => dc.DrawImage(Picture, Bounds);

    public override void PaintOn(DrawingContext dc, Brush fallback, Vector offset) =>
        dc.DrawImage(Picture, Rect.Offset(Bounds, offset));
}

/// <summary>
/// A filled rectangle <em>behind</em> a piece rather than in it — a colour box, a highlight.
///
/// <para>
/// The whole difference from a <see cref="RuleMark"/> is when it is painted, and that is a real
/// difference rather than a shade of one: a wash behind one term must not cover the glyphs of another,
/// so every wash in a tree goes down before any ink does. Being a kind of its own is what lets the
/// painter be asked for one layer at a time without anything having to know what the content is.
/// </para>
/// </summary>
public sealed record WashMark(Rect Bounds, Brush Fill) : LayoutMark
{
    public override Rect Covers => Bounds;

    public override void PaintOn(DrawingContext dc, Brush fallback) =>
        dc.DrawRectangle(Fill, null, Bounds);

    public override void PaintOn(DrawingContext dc, Brush fallback, Vector offset) =>
        dc.DrawRectangle(Fill, null, Rect.Offset(Bounds, offset));
}
