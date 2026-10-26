using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The words a diagram sets, made from what it was handed: what somebody wrote (<see cref="Written"/>), words it works out
/// (<see cref="Worked"/>), words wrapped to a width (<see cref="Wrapped"/>) and the icon something names (<see cref="Iconed"/>) — each
/// made once into <see cref="DiagramWords"/>, which decides how they behave from then on.
///
/// <para>
/// A builder makes one for the block it is laying out, from the reading, the state of what is being written, the style and the
/// medium it was handed, and asks it for every word it draws. What each piece of a run of words is — its own characters, an entity
/// code, a break, a binding — was settled when the block was read (<see cref="WithWordPieces"/>), so this asks the tree and never
/// looks at the characters.
/// </para>
/// </summary>
/// <param name="inset">What another language written in a part is laid out as, or null where nothing is written there.</param>
internal sealed class DiagramText(ContentReading reading, EditState state, LayoutStyle style, ILayoutMedium medium,
                                          Func<ContentPart, LayoutInset?> inset)
{
    /// <summary>The face a diagram's own words are drawn in.</summary>
    public const string BodyFont = "Segoe UI";

    /// <summary>How much wider than its widest line a broken line's room is set, so measuring it again breaks it the same way.</summary>
    private const double Hair = 0.01;

    /// <summary>
    /// What a run of words says, as one line of text — what a chip says it stands for. While the reader is writing inside it, it says
    /// exactly what they typed — there is nothing to show them but that.
    /// </summary>
    public string Shown(ContentPart part) => string.Join(' ', Lines(part, breaks: true).Select(line => line.Says));

    /// <summary>One line of a run of words: what it says, what it stands for, and whether each of its characters is one written there.</summary>
    private readonly record struct Line(string Says, ISourcePart Part, bool Maps);

    /// <summary>What the diagram holds about what its words are made of — asked once, of the tree the builder was handed.</summary>
    private MermaidWords Pieces => this.pieces ??= MermaidWords.Held(reading.Root.Node);

    private MermaidWords? pieces;

    /// <summary>
    /// The lines a run of words is set as: broken at each line break written in it where <paramref name="breaks"/>, and
    /// otherwise one, with any break in it shown as it was typed.
    ///
    /// <para>
    /// A line is the pieces between two breaks set one after another, and stands for those pieces; a run that is only its own
    /// characters is one line, standing for the run.
    /// </para>
    /// </summary>
    private IReadOnlyList<Line> Lines(ContentPart part, bool breaks)
    {
        // A name or a label handed over whole is set as the words in it.
        var words = part.Kind == Kinds.Words || part.Node.IsLeaf ? part
            : part.Words() is { Kind: Kinds.Words } inner ? inner
            : part;

        if (Pieces.Of(words.Node) is not { } pieces)
            return [words.Node.IsLeaf ? new Line(words.Text, words, Maps: true) : new Line(string.Empty, words, Maps: false)];

        var writing = WritingIn(words);
        var lines = new List<Line>();
        var from = 0;

        for (var at = 0; at <= pieces.Count; at++)
        {
            if (at < pieces.Count && !(breaks && pieces[at].Breaks)) continue;

            lines.Add(Set(from, at));
            from = at + 1;
        }

        return lines;

        Line Set(int first, int end)
        {
            if (end == first) return new Line(string.Empty, words, Maps: false);

            var run = new List<WordPiece>(end - first);
            for (var at = first; at < end; at++) run.Add(pieces[at]);

            // Where the reader is writing, every piece is the characters they typed.
            var says = string.Concat(run.Select(piece => writing ? piece.Written : piece.Says));
            ISourcePart stands = run.Count == pieces.Count ? words : new PartRun([.. run.Select(piece => piece.In(words))]);

            return new Line(says, stands, Maps: writing || run.All(piece => piece.AsWritten));
        }
    }

    /// <summary>A run of diagram text: the face every diagram label is set in, at the size and weight it is asked for.</summary>
    public IShapedText Text(string text, double size, LayoutInk ink, int? weight = null, bool? italic = null) =>
        medium.Shape(text, style.Face(BodyFont, weight ?? 400, italic ?? false), size, ink);

    /// <summary>
    /// A part's words, as they read rather than as they were written — entity codes decoded, bindings read.
    ///
    /// <para>
    /// Where the part is a whole block of another language, that content is laid out and handed back in their place:
    /// a builder measures and sets what it is given, so every diagram draws a tune on a node without knowing a tune
    /// from a molecule.
    /// </para>
    /// </summary>
    public DiagramWords Written(ContentPart? part, ContentPart? hole, double size, LayoutInk ink, int? weight = null, bool? italic = null)
    {
        var letter = Text("x", size, ink);
        if (hole is not null || part is null) return new DiagramWords(letter, part, hole, letter, ink, maps: false, writes: false);

        if (Inset(part) is { } nested)
            return new DiagramWords(letter, part, null, letter, ink, maps: false, writes: false, nested);

        // A line broken where it was written is set as the lines it was broken into, and pressed rather than typed into, since
        // what it shows is no longer the characters written.
        var lines = Lines(part, breaks: !WritingIn(part));

        // Words that are words share one letter to be measured against, a letter for each size and colour: every label in a
        // diagram asks how tall a line of it is, and a letter made for each would be shaped once for each.
        var shared = Letter(size, ink);

        return lines is [var line]
            ? new DiagramWords(Text(line.Says, size, ink, weight, italic), line.Part, null, shared, ink, line.Maps, writes: true)
            : new DiagramWords(Text(string.Join('\n', lines.Select(each => each.Says)), size, ink, weight, italic), part, null, shared,
                                       ink, maps: false, writes: true);
    }

    /// <summary>The letter words of this size and colour are measured against, set once for the diagram.</summary>
    private IShapedText Letter(double size, LayoutInk ink)
    {
        if (this.letters.TryGetValue((size, ink), out var letter)) return letter;

        return this.letters[(size, ink)] = Text("x", size, ink);
    }

    private readonly Dictionary<(double Size, LayoutInk Ink), IShapedText> letters = [];

    /// <summary>As <see cref="Written"/>, a line to each line break written in it, and each line set no wider than
    /// <paramref name="width"/> (<see cref="Fitted"/>).</summary>
    public IReadOnlyList<DiagramWords> Wrapped(ContentPart? part, ContentPart? hole, double size, LayoutInk ink, double width,
                                                       int? weight = null, bool? italic = null)
    {
        var whole = Written(part, hole, size, ink, weight, italic);
        if (part is null || hole is not null) return [whole];

        // Another content is one thing, however wide it turned out: breaking it would be breaking a tune in half.
        if (whole.Nested) return [whole];

        var lines = Lines(part, breaks: true);
        if (lines.Count == 1 && whole.Width <= width) return [whole];

        var letter = Text("x", size, ink);
        return [.. lines.Select(line => new DiagramWords(Fitted(Text(line.Says, size, ink, weight, italic), width), line.Part, null, letter,
                                                                 ink, line.Maps, writes: line.Maps))];
    }

    /// <summary>
    /// A line of words set no wider than <paramref name="width"/>: where it is wider, broken after the last space that fits, or
    /// inside a word longer than the width, into lines set in the middle of one another — as wide as the widest of them, so it is
    /// measured and placed as what it takes.
    /// </summary>
    private static IShapedText Fitted(IShapedText text, double width)
    {
        if (text.Width <= width) return text;

        text.Bound(Math.Max(1, width), LayoutAlign.Centre);
        text.Bound(Math.Max(1, text.Width + Hair), LayoutAlign.Centre);

        return text;
    }

    /// <summary>Whether the reader is writing inside <paramref name="part"/>, where it is shown exactly as typed.</summary>
    private bool WritingIn(ContentPart part) => state.Raw is { } raw && raw.Start <= part.Start && raw.End >= part.End;

    /// <summary>Words the diagram works out rather than anybody writing (a share, a total): pressed as
    /// the <paramref name="part"/> they stand for, no caret. See <see cref="DiagramWords"/>.</summary>
    public DiagramWords Worked(string says, ContentPart? part, double size, LayoutInk ink, int? weight = null, bool? italic = null) =>
        new(Text(says, size, ink, weight, italic), part, null, Text("x", size, ink), ink, maps: false, writes: false);

    /// <summary>
    /// The icon something names, set in the face its glyph is in and standing for what names it — or null where it names no
    /// icon the app draws (<see cref="WithIcons"/>), and what is drawn instead is the diagram's to say. What is asked about may
    /// be the icon's own words, or what holds it: a line naming an icon, or metadata setting one.
    /// </summary>
    public DiagramWords? Iconed(ContentPart? part, double size, LayoutInk ink)
    {
        var named = part;
        while (named is not null && named.Node is not RenderedIconNode) named = named.Parent;
        named ??= part?.SelfAndDescendants().FirstOrDefault(each => each.Node is RenderedIconNode);
        if (named?.Node is not RenderedIconNode icon) return null;

        var face = icon.Family is null ? style.Face(BodyFont) : style.IconFace;
        var glyph = medium.Shape(icon.Glyph, face, size, ink);

        return new DiagramWords(glyph, named, null, Text("x", size, ink), ink, maps: false, writes: false);
    }

    /// <summary>What the thing a name first writes is drawn with (<see cref="MermaidStyling.Style"/>) — nothing of its own, where no styling says.</summary>
    public static MermaidStyle StyleOf(ContentPart? name) => (name?.Node as StyledNode)?.Style ?? MermaidStyle.None;

    /// <summary>
    /// The content written inside <paramref name="part"/>, laid out — or null where nothing is written inside it, or it is being
    /// shown as the characters it was typed as. Which language reads it was settled by a stage and is hanging on the node.
    /// </summary>
    public LayoutInset? Inset(ContentPart? part)
    {
        if (part is not { Length: > 0 }) return null;
        if (state.Raw is { } raw && raw.Start <= part.Start && raw.End >= part.End) return null;

        return inset(part);
    }
}
