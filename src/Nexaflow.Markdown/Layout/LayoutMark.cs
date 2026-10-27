namespace Nexaflow.Markdown.Layout;

/// <summary>
/// One thing a piece of laid-out content drew — said, not done. A mark holds what it draws with as the medium's own handles
/// and says how far it reaches; drawing it is the medium's painter's, which knows every kind there is, because the vocabulary
/// is closed and small: words, glyphs, a hairline, an outline, a rule, a picture and a wash.
/// </summary>
public abstract record LayoutMark
{
    /// <summary>How far this drawing reaches, in the frame of the piece it belongs to.</summary>
    public abstract LayoutRect Covers { get; }
}

/// <summary>Glyphs already shaped and positioned — what a typesetter hands over.</summary>
public sealed record GlyphMark(IShapedGlyphs Run, LayoutInk? Ink) : LayoutMark
{
    public override LayoutRect Covers => Run.Covers;
}

/// <summary>A run of words, set by the medium. Where it lands, not where it was placed, is what it covers.</summary>
public sealed record TextMark(IShapedText Glyphs, LayoutPoint At, LayoutInk? Ink) : LayoutMark
{
    public override LayoutRect Covers
    {
        get
        {
            var room = Glyphs.Room;
            if (double.IsNaN(room) || double.IsInfinity(room) || room <= 0) room = Glyphs.Width;

            var shift = Glyphs.Align switch
            {
                LayoutAlign.Centre => (room - Glyphs.Width) / 2,
                LayoutAlign.Right => room - Glyphs.Width,
                _ => 0,
            };

            return new LayoutRect(At.X + shift, At.Y, Glyphs.Width, Glyphs.Height);
        }
    }
}

/// <summary>A hairline: a fraction's bar, a strike, the stroke of a radical.</summary>
public sealed record LineMark(LayoutPoint From, LayoutPoint To, LayoutInk? Ink) : LayoutMark
{
    public override LayoutRect Covers => new(From, To);
}

/// <summary>An outline, filled and/or stroked: a music glyph, a beam, a slur, a node of a diagram.</summary>
public sealed record GeometryMark(LayoutShape Shape, LayoutInk? Fill, LayoutStroke? Stroke) : LayoutMark
{
    public override LayoutRect Covers => Shape.Bounds;
}

/// <summary>A filled rectangle: a rule, a bar of a barcode.</summary>
public sealed record RuleMark(LayoutRect Bounds, LayoutInk? Ink) : LayoutMark
{
    public override LayoutRect Covers => Bounds;
}

/// <summary>A picture a host handed over, drawn into the box it was fitted to.</summary>
public sealed record PictureMark(LayoutPicture Picture, LayoutRect Bounds) : LayoutMark
{
    public override LayoutRect Covers => Bounds;
}

/// <summary>A filled rectangle <em>behind</em> a piece — painted before any ink of any piece.</summary>
public sealed record WashMark(LayoutRect Bounds, LayoutInk Fill) : LayoutMark
{
    public override LayoutRect Covers => Bounds;
}
