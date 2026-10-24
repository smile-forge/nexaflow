using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.Rendering;

namespace Nexaflow.Markdown.Layout.Scores;

/// <summary>
/// The faces a score is set in, as a medium found them — the music font, the face its words are set in, and the one its chords
/// are — found once for a medium, as a style's are.
/// </summary>
public sealed class PortableScoreFonts
{
    private PortableScoreFonts(LayoutFace? music, LayoutFace words, LayoutFace wordsHeavy, LayoutFace wordsSlanted, LayoutFace chords,
                               LayoutFace chordsHeavy)
    {
        Music = music;
        Words = words;
        WordsHeavy = wordsHeavy;
        WordsSlanted = wordsSlanted;
        Chords = chords;
        ChordsHeavy = chordsHeavy;
    }

    /// <summary>Bravura, or null where the medium has no music font to draw from.</summary>
    public LayoutFace? Music { get; }

    /// <summary>The face a score's words are set in where nothing names another: the one prose is set in.</summary>
    public LayoutFace Words { get; }

    public LayoutFace WordsHeavy { get; }

    public LayoutFace WordsSlanted { get; }

    /// <summary>
    /// The face chord symbols are set in — a serif, where everything else on the page is the document's sans, so a reader scanning
    /// for the next chord does not find a syllable.
    /// </summary>
    public LayoutFace Chords { get; }

    public LayoutFace ChordsHeavy { get; }

    public static PortableScoreFonts Of(ILayoutMedium medium)
    {
        LayoutFace? music;
        try { music = medium.Font("Bravura.otf"); }
        catch (InvalidOperationException) { music = null; }

        const string words = "Segoe UI", chords = "Times New Roman, Georgia, serif";

        return new PortableScoreFonts(music, medium.Face(words), medium.Face(words, weight: 600), medium.Face(words, italic: true),
                                      medium.Face(chords), medium.Face(chords, weight: 600));
    }
}

/// <summary>
/// The engraver's layer onto the renderer-free layout tree: each piece opened in a <see cref="PortableBuilder"/>, each glyph a
/// filled outline the medium cuts from the music font, each word set by the medium.
/// </summary>
internal sealed class PortableScoreLayer(ILayoutMedium medium, LayoutInk ink, PortableScoreFonts fonts) : IScoreLayer
{
    private readonly PortableBuilder _build = new(medium);

    /// <summary>What was engraved, once it is sealed.</summary>
    public PortableTree? Tree { get; private set; }

    public int Open(string kind, ISourcePart? part, LayoutPoint at) => _build.Open(kind, part, at);

    public void Close() => _build.Close();

    public LayoutVector Anchor => _build.Anchor;

    public LayoutRect Reached => _build.Reached;

    public void Reserves(double top, double height) => _build.Reserves(top, height);

    public void Covers(LayoutRect what) => _build.Covers(what);

    public void Runs(IReadOnlyList<int> members, bool vertical) => _build.Runs(members, vertical);

    public LayoutRect Seal()
    {
        Tree = _build.Seal();
        return Tree.Root.Box;
    }

    /// <summary>
    /// The size a glyph is cut at: an em of four staff spaces, to the hundredth — so a cut is kept for every size a score uses
    /// rather than for every size it works out.
    /// </summary>
    private static double Em(double staffSpace, double scale) => Math.Round(4.0 * staffSpace * scale, 2);

    public bool Glyph(int codepoint, LayoutPoint baseline, double staffSpace, double scale)
    {
        if (fonts.Music is not { } music || medium.GlyphOutline(music, codepoint, Em(staffSpace, scale), baseline) is not { } outline)
            return false;

        _build.Draw(new PortableGeometryMark(outline, null, null));
        return true;
    }

    public void Rule(LayoutRect box) => _build.Draw(new PortableRuleMark(box, null));

    public void Box(LayoutRect box) => _build.Draw(new PortableGeometryMark(medium.Rectangle(box), null, null));

    public void Inked(LayoutRect box) => _build.Draw(new PortableGeometryMark(medium.Rectangle(box), ink, null));

    public void Dot(LayoutRect box) =>
        _build.Draw(new PortableGeometryMark(medium.Ellipse(new LayoutPoint(box.X + (box.Width / 2), box.Y + (box.Height / 2)),
                                                            box.Width / 2, box.Height / 2), null, null));

    public void Ring(LayoutPoint centre, double radiusX, double radiusY, double thickness) =>
        _build.Draw(new PortableGeometryMark(medium.Ellipse(centre, radiusX, radiusY), null, medium.Stroke(null, thickness)));

    public void Polygon(LayoutPoint first, LayoutPoint second, LayoutPoint third, LayoutPoint fourth)
    {
        var shape = medium.Outline();
        shape.Begin(first);
        shape.LineTo(second);
        shape.LineTo(third);
        shape.LineTo(fourth);

        _build.Draw(new PortableGeometryMark(shape.Close(), null, null));
    }

    public void Crescent(LayoutPoint from, LayoutPoint bow, LayoutPoint to, LayoutPoint back)
    {
        var shape = medium.Outline();
        shape.Begin(from);
        shape.QuadraticTo(bow, to);
        shape.QuadraticTo(back, from);

        _build.Draw(new PortableGeometryMark(shape.Close(), null, null));
    }

    public void Stroke(LayoutPoint from, LayoutPoint to, double thickness)
    {
        var line = medium.Outline();
        line.Begin(from, filled: false, closed: false);
        line.LineTo(to);

        _build.Draw(new PortableGeometryMark(line.Close(), null, medium.Stroke(null, thickness)));
    }

    public IScoreWords Words(string text, double size, bool semibold = false, bool italic = false) =>
        new PortableScoreWords(medium.Shape(text, semibold ? fonts.WordsHeavy : italic ? fonts.WordsSlanted : fonts.Words, size, ink));

    public IScoreWords Chord(string text, double size)
    {
        var (written, signs) = ChordSpelling.Spelled(text);

        return new PortableScoreWords(medium.Shape(written, fonts.ChordsHeavy, size, ink,
                                                   faces: signs.Count == 0 ? null : [.. signs.Select(at => (at, 1, fonts.Chords))]));
    }

    public void Text(IScoreWords words, LayoutPoint at) => _build.Draw(new PortableTextMark(((PortableScoreWords)words).Glyphs, at, null));

    public void Prose(IScoreWords words, LayoutPoint at, double room, LayoutAlign align, ISourcePart? part, string kind,
                      IReadOnlyList<ISourcePart>? letters) =>
        PortableText.Place(_build, ((PortableScoreWords)words).Glyphs, at, room, align, part, kind, letters, medium, fonts.Words, 0, lined: false);

    public double Advance(int codepoint, double staffSpace, double scale = 1.0) =>
        fonts.Music is { } music && Em(staffSpace, scale) > 0 ? medium.GlyphAdvance(music, codepoint, Em(staffSpace, scale)) : staffSpace * 1.2;

    public double Width(string text, double size) => medium.Measuring(fonts.Words, size).Of(text).Width;
}

/// <summary>Words a medium set for a score.</summary>
internal sealed class PortableScoreWords(IShapedText glyphs) : IScoreWords
{
    public IShapedText Glyphs { get; } = glyphs;

    public double Width => Glyphs.Width;

    public double Height => Glyphs.Height;

    public (double Top, double Height) Letters => (Glyphs.Height + Glyphs.Overhang - Glyphs.Extent, Glyphs.Extent);
}

/// <summary>
/// A tune, engraved onto the renderer-free layout tree by the same engraver the WPF builder uses, through a
/// <see cref="PortableScoreLayer"/>, set in the middle of its page.
/// </summary>
internal sealed class PortableMusicBuilder(ContentReading reading, Engraver engraver, PortableStyle style, ILayoutMedium medium,
                                           PortableScoreFonts fonts)
{
    /// <summary>Fallback size for source that couldn't be engraved.</summary>
    private const double SourceSize = 13;

    public PortableLaid Lay(double room = double.PositiveInfinity)
    {
        if (double.IsNaN(room) || room <= 0 || !double.IsFinite(room)) return Score(Engraver.Unbounded);

        var score = Score(room * Engraver.PageWidth);
        var page = new PortableBuilder(medium);
        var width = Math.Max(room, score.Width);

        new PortableInset(score).Set(page, new LayoutPoint((width - score.Width) / 2, 0), "MusicPage");

        return new PortableLaid(page.Seal(), width, score.Height, score.Trouble);
    }

    private PortableLaid Score(double room)
    {
        var tune = engraver.Read();

        var troubled = Engraver.Troubled(tune);
        if (troubled.Count > 0)
            return PortableSourceShown.Lay(reading.Root, [.. troubled.Select(part => PortableDiagnostic.Of(part, part.Trouble!))], medium,
                                           style.Face(style.MonoFont), SourceSize, style.Text, style, room);

        var layer = new PortableScoreLayer(medium, style.Text, fonts);
        var (width, height) = engraver.Engrave(tune, room, layer);

        return new PortableLaid(layer.Tree!, width, height, []);
    }
}
