namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What content is laid out <em>for</em>: the thing that shapes its words, makes its outlines and holds its colours.
///
/// <para>
/// <strong>A layout is made in a medium, not translated into one.</strong> A builder asks the medium for every resource it
/// draws with — a face, an ink, a run of words set, an outline, a pen — and holds what comes back as an opaque handle. The
/// handle is the medium's own object (a WPF medium's run of words <em>is</em> its formatted text), so measuring a word and
/// drawing it are one shaping, and painting hands the handle straight back to the medium that made it with nothing converted.
/// That is what keeps a layout tree free of any one renderer without making it a second copy of the drawing.
/// </para>
/// <para>
/// What a builder reads back — how wide words came out, where a letter landed, how far an outline reaches, whether a point is
/// inside it — is asked of the handle in the layout's own units, so a builder never names a renderer.
/// </para>
/// </summary>
public interface ILayoutMedium
{
    /// <summary>A colour to draw in.</summary>
    LayoutInk Ink(LayoutColor color);

    /// <summary>A face words are set in, found once and kept by whatever sets words in it.</summary>
    LayoutFace Face(string family, int weight = 400, bool italic = false);

    /// <summary>
    /// <paramref name="text"/> set as one run in <paramref name="face"/> at <paramref name="size"/>, measured at the layout's
    /// density — unbroken until it is given room.
    /// </summary>
    /// <param name="dots">The ink a <see cref="TextLines.Dotted"/> rule is drawn in, where it has one of its own.</param>
    IShapedText Shape(string text, LayoutFace face, double size, LayoutInk ink, TextLines lines = TextLines.None, LayoutInk? dots = null);

    /// <summary>
    /// What measures words set in <paramref name="face"/> at <paramref name="size"/> without setting a run of them — found once
    /// for a paragraph, not once a word, and remembering what it measured.
    /// </summary>
    TextMeasure Measuring(LayoutFace face, double size);

    /// <summary>
    /// One line of characters set unbroken, to read where each of its letters stands — set once, every letter read off it,
    /// rather than the line set again for every letter.
    /// </summary>
    ShapedLine Line(string line, LayoutFace face, double size);

    /// <summary>
    /// Glyphs a typesetter has already chosen and placed, in <paramref name="face"/>'s font. The arrays are handed over, not
    /// lent: the medium keeps them as they are, so a formula's thousands of glyph runs are never copied on the way in.
    /// </summary>
    IShapedGlyphs Glyphs(LayoutFace face, double size, LayoutPoint origin, ushort[] indices, double[] advances,
                         LayoutVector[]? offsets = null);

    /// <summary>
    /// A face from a font file that content's own data names — the maths faces a typesetter's tables describe — opened from
    /// wherever this medium keeps such files.
    /// </summary>
    LayoutFace Font(string file);

    /// <summary>
    /// The glyph <paramref name="character"/> is drawn with in <paramref name="face"/>, set at <paramref name="size"/> with the left
    /// end of its baseline at <paramref name="origin"/> — or null where the face has no glyph for it.
    /// </summary>
    IShapedGlyphs? Character(LayoutFace face, char character, double size, LayoutPoint origin);

    /// <summary>How far one letter set in <paramref name="face"/> at a size of one reaches: across, above its baseline, and below it.</summary>
    LetterInk Letter(LayoutFace face, char character);

    /// <summary>An outline made figure by figure — see <see cref="ShapeBuilder"/>.</summary>
    ShapeBuilder Outline(bool evenOdd = true);

    LayoutShape Rectangle(LayoutRect box, double radiusX = 0, double radiusY = 0);

    LayoutShape Ellipse(LayoutPoint centre, double radiusX, double radiusY);

    /// <summary>Two outlines made one by <paramref name="combine"/>.</summary>
    LayoutShape Combine(LayoutShape a, LayoutShape b, ShapeCombine combine);

    /// <summary>The outline a pen of <paramref name="thickness"/> would ink tracing <paramref name="shape"/>.</summary>
    LayoutShape Widen(LayoutShape shape, double thickness);

    /// <summary>
    /// <paramref name="shape"/> made cheap to ask about: a combination worked out once, so a press asking whether it is inside
    /// does not do the boolean again.
    /// </summary>
    LayoutShape Resolved(LayoutShape shape);

    /// <summary>
    /// What an outline is stroked with. Without an ink, it strokes in whatever the content did not ask for — the theme's,
    /// handed over when it is painted.
    /// </summary>
    LayoutStroke Stroke(LayoutInk? ink, double thickness, ReadOnlySpan<double> dashes = default, StrokeEnds? ends = null);

    /// <summary>A turn a piece is drawn in, about its own anchor.</summary>
    LayoutTurn Turn(LayoutMatrix matrix);

    /// <summary>
    /// A turn of <paramref name="degrees"/> clockwise about <paramref name="about"/> — what nearly every turn is, said as itself so a
    /// medium can make the rotation it has rather than one worked out from a matrix.
    /// </summary>
    LayoutTurn Rotation(double degrees, LayoutPoint about);

    /// <summary>The pixel grid a piece's edges land on, across and down, in its own frame. The arrays are handed over.</summary>
    LayoutGuides Guides(double[] across, double[] down);
}

/// <summary>
/// A turn a piece is drawn in, as a medium holds it — made once with the piece, so painting it again hands the medium what it
/// made rather than making it again. What a press and a caret are carried through is <see cref="Matrix"/>.
/// </summary>
public abstract class LayoutTurn
{
    public abstract LayoutMatrix Matrix { get; }
}

/// <summary>The pixel grid a piece's edges land on, as a medium holds it.</summary>
public abstract class LayoutGuides
{
}

/// <summary>How far one letter's ink reaches, set at a size of one: across, above its baseline, and below it.</summary>
public readonly record struct LetterInk(double Width, double Height, double Depth);

/// <summary>A colour, straight alpha.</summary>
public readonly record struct LayoutColor(byte A, byte R, byte G, byte B)
{
    public static LayoutColor Of(byte r, byte g, byte b) => new(255, r, g, b);
}

/// <summary>A colour as a medium draws it.</summary>
public abstract class LayoutInk
{
    public abstract LayoutColor Color { get; }
}

/// <summary>A face words are set in, as a medium found it.</summary>
public abstract class LayoutFace
{
    public abstract string Family { get; }

    public abstract int Weight { get; }

    public abstract bool Italic { get; }
}

/// <summary>Rules a run of words is drawn with.</summary>
[Flags]
public enum TextLines
{
    None = 0,
    Under = 1,
    Through = 2,

    /// <summary>A rule of dots under the words: what says a word stands for more than itself.</summary>
    Dotted = 4,
}

/// <summary>How wide words come out in one face at one size — measured, not set.</summary>
public abstract class TextMeasure
{
    /// <summary>How wide <paramref name="text"/> is set, trailing space left off, and how far along it moves what follows.</summary>
    public abstract (double Width, double Advance) Of(ReadOnlySpan<char> text);
}

/// <summary>One line of characters as a medium set it, asked where its letters stand.</summary>
public abstract class ShapedLine : IDisposable
{
    public abstract double Height { get; }

    /// <summary>Where the letter at <paramref name="index"/> stands — all of its pieces where it was set in several — or empty.</summary>
    public abstract LayoutRect Letter(int index);

    public abstract void Dispose();
}

/// <summary>Where a run of words stands in the room it was given.</summary>
public enum LayoutAlign
{
    Left,
    Right,
    Centre,
    Justify,
}

/// <summary>
/// A run of words as a medium set it — what is drawn, and what is measured, at once.
///
/// <para>
/// An interface rather than a base class so a medium can make its own text type <em>be</em> one: a run of words is the most
/// numerous thing a document makes, and a wrapper round each is an object for every one of them.
/// </para>
/// </summary>
public interface IShapedText
{
    string Text { get; }

    /// <summary>How wide it is set, trailing space left off.</summary>
    double Width { get; }

    /// <summary>How wide it is set, trailing space and all.</summary>
    double WidthWithSpace { get; }

    double Height { get; }

    /// <summary>How far below its top its first line's baseline is.</summary>
    double Baseline { get; }

    /// <summary>How far its ink reaches, top to bottom.</summary>
    double Extent { get; }

    /// <summary>The room it is broken into lines to fit, or infinity where it is not broken.</summary>
    double Room { get; }

    LayoutAlign Align { get; }

    /// <summary>
    /// Gives it room and an alignment — only where they differ from what it has, since either set throws away everything the
    /// medium worked out about it.
    /// </summary>
    void Bound(double room, LayoutAlign align);

    /// <summary>
    /// What a stretch of it covers, measured from <paramref name="at"/> — one rectangle round every line it runs over; empty where
    /// it covers nothing.
    /// </summary>
    LayoutRect Covers(LayoutPoint at, int from, int count);
}

/// <summary>Glyphs placed by a typesetter, as a medium holds them.</summary>
public interface IShapedGlyphs
{
    /// <summary>How far their ink reaches, where they were placed.</summary>
    LayoutRect Covers { get; }
}

/// <summary>An outline, as a medium holds it: frozen once made.</summary>
public abstract class LayoutShape
{
    /// <summary>How far it reaches.</summary>
    public abstract LayoutRect Bounds { get; }

    /// <summary>Whether <paramref name="point"/> is inside it.</summary>
    public abstract bool Contains(LayoutPoint point);

    /// <summary>Whether any of it is inside <paramref name="area"/>.</summary>
    public abstract bool Touches(LayoutRect area);
}

/// <summary>How two outlines are made one.</summary>
public enum ShapeCombine
{
    Union,
    Intersect,
    Exclude,
    Xor,
}

/// <summary>
/// An outline made figure by figure, then frozen into a <see cref="LayoutShape"/>. A medium's own builder writes each
/// segment straight into its own geometry, so nothing is recorded to be read again.
/// </summary>
public abstract class ShapeBuilder
{
    public abstract void Begin(LayoutPoint at, bool filled = true, bool closed = true);

    public abstract void LineTo(LayoutPoint to);

    public abstract void BezierTo(LayoutPoint first, LayoutPoint second, LayoutPoint to);

    public abstract void QuadraticTo(LayoutPoint control, LayoutPoint to);

    public abstract void ArcTo(LayoutPoint to, double radiusX, double radiusY, double rotation, bool large, bool clockwise);

    /// <summary>The outline, finished. Nothing may be added afterwards.</summary>
    public abstract LayoutShape Close();
}

/// <summary>A picture a host handed over, as a medium draws it.</summary>
public abstract class LayoutPicture
{
    public abstract double Width { get; }

    public abstract double Height { get; }
}

/// <summary>What an outline is stroked with, as a medium holds it.</summary>
public abstract class LayoutStroke
{
    /// <summary>Its own ink, or null where it strokes in whatever it is handed.</summary>
    public abstract LayoutInk? Ink { get; }

    public abstract double Thickness { get; }
}

/// <summary>How a stroke's ends and corners are drawn.</summary>
public enum StrokeCap
{
    Flat,
    Square,
    Round,
    Triangle,
}

/// <summary>How a stroke turns a corner.</summary>
public enum StrokeJoin
{
    Miter,
    Bevel,
    Round,
}

/// <summary>
/// How a stroked outline is drawn beyond its ink, thickness and dashes: its ends, its corners, and where along its dashes it
/// starts. The defaults are a pen's own.
/// </summary>
public sealed record StrokeEnds(StrokeCap StartCap = StrokeCap.Flat, StrokeCap EndCap = StrokeCap.Flat,
                                StrokeCap DashCap = StrokeCap.Square, StrokeJoin Join = StrokeJoin.Miter,
                                double MiterLimit = 10, double DashOffset = 0);
