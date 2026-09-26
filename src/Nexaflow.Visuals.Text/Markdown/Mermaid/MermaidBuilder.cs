using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Icons;
using Nexaflow.Visuals.Text.Editing;


namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The pieces every Mermaid diagram's layout has, whatever the diagram is.</summary>
public static class MermaidPiece
{
    /// <summary>The whole block: its title, its diagram, and what could not be read, top to bottom.</summary>
    public const string Diagram = "Diagram";

    /// <summary>The title set over the diagram.</summary>
    public const string Title = "Title";

    /// <summary>Why part of the block did not draw as itself.</summary>
    public const string Trouble = "Trouble";

    /// <summary>A legend: a row per colour a diagram draws in, saying what it is — see <see cref="DiagramLegend"/>.</summary>
    public const string Legend = "Legend";

    /// <summary>One row of a legend: its swatch, and what it says.</summary>
    public const string Key = "Key";

    /// <summary>The square of colour a legend row explains.</summary>
    public const string Swatch = "Swatch";

    /// <summary>Words set inside a shape — see <see cref="DiagramShapes.Draw"/>.</summary>
    public const string Words = "Words";

    /// <summary>An icon something names, drawn in the glyph it is.</summary>
    public const string Glyph = "Glyph";

    /// <summary>What a shape draws — its outline, filled — which is what a press inside it lands on. See <see cref="DiagramShapes.Draw"/>.</summary>
    public const string Shape = "Shape";

    /// <summary>What a node folds and unfolds by — see <see cref="DiagramChip"/>.</summary>
    public const string Chip = "Chip";

    /// <summary>Another language drawn inside this one's words — see <see cref="DiagramInset"/>.</summary>
    public const string Nested = "Nested";

    /// <summary>What offers the children a node has too many of — see <see cref="DiagramSpill"/>.</summary>
    public const string More = "More";

    /// <summary>What a line draws — an axis's line and ticks — which is what a press near it lands on. See <see cref="DiagramAxis.Draw"/>.</summary>
    public const string Line = "Line";
}

/// <summary>What every Mermaid diagram's builder shares: reading the block once, the title over the
/// diagram, trouble text, and the element it's shown in. Each diagram type draws its own way and is
/// its own builder; <see cref="Build"/> draws at the origin into its own layout, grafted under the
/// title once the title's height is known, so no diagram has to know where its top is.</summary>
internal abstract class MermaidBuilder : ContentBuilder
{
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    /// <summary>The face a diagram's own words are drawn in.</summary>
    private static readonly FontFamily BodyFont = new("Segoe UI");

    private const double SourceSize = 13;
    private const double TitleSize = 15;
    private const double ReasonSize = 12;

    /// <summary>Clear air under the title, and above the reasons.</summary>
    private const double TitleGap = 8;
    private const double ReasonGap = 6;

    /// <summary>The narrowest the reasons are set to, so a small diagram does not stack them a word to a line.</summary>
    private const double ReasonRoom = 260;

    /// <summary>Clear air between the card's edge and what is drawn on it.</summary>
    private const double Pad = 12;

    /// <summary>How round the card's corners are.</summary>
    private const double Corner = 6;

    protected MermaidBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) =>
        Ink = new DiagramInk(style);

    /// <summary>What every diagram calls the style it is drawn in.</summary>
    protected StyleFormat Palette => Style;

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    protected DiagramInk Ink { get; }

    /// <summary>
    /// How wide what a diagram draws may be: the room the block was given, less the card it is drawn on. What a builder
    /// fits its drawing into, and the whole of what the room means to it.
    /// </summary>
    protected double Space => double.IsInfinity(Room) ? Room : Math.Max(1, Room - (Pad * 2));

    /// <summary>
    /// What the diagram's front matter asks for, as its stages hung it on the block (<see cref="WithConfig{TConfig}"/>) — or
    /// <paramref name="otherwise"/>, the diagram's defaults, where they hung nothing.
    /// </summary>
    protected TConfig Configured<TConfig>(TConfig otherwise) where TConfig : class =>
        (Reading.Root.Node as ConfiguredNode<TConfig>)?.Config ?? otherwise;

    /// <summary>Draws the diagram into <paramref name="build"/> at the origin and hands back the room it
    /// took. May throw — whatever it was reading is then shown as written, with the reason.</summary>
    protected abstract Size Draw(MermaidBlock block, LayoutBuilder build);

    /// <summary>The title to set over the diagram: the diagram's own where it writes one, else the
    /// front matter's (<see cref="MermaidBlock.Title"/>).</summary>
    protected virtual (ContentPart? Part, string? Says, bool AsWritten) TitleOf(MermaidBlock block) =>
        (block.Title, block.TitleSays, block.TitleAsWritten);

    /// <summary>The colour the diagram's front matter asks its title to be written in, or null for the theme's heading.</summary>
    protected virtual string? TitleColour => null;

    /// <summary>How big the diagram's front matter asks its title to be set, or null for the size every diagram's title is.</summary>
    protected virtual double? TitleTextSize => null;

    /// <summary>What the title is written in: the colour the front matter asks for, or the theme's heading.</summary>
    protected Brush TitleInk => Ink.Written(TitleColour) ?? Palette.Heading;

    /// <summary>
    /// What goes under the drawing — a legend, a key, a caption — each as its own tree, with the air above it.
    /// </summary>
    private readonly List<(LayoutTree Tree, Size Size, double Gap)> beneath = [];

    /// <summary>
    /// Adds a piece under the drawing, as one of the block's own pieces rather than part of the drawing.
    ///
    /// <para>
    /// That is what keeps it lined up with everything else. A block is a stack — its title, its drawing, whatever goes
    /// under that, and anything it could not read — and the width of the block is the widest of them, which is not known
    /// until all of them have been measured. A piece a diagram sets down inside its own drawing is placed against the
    /// drawing's width, so a legend wider than the drawing widens the block underneath a drawing already off to one side
    /// of it. Handed over instead, it is measured with the rest and every one of them is set about the same middle.
    /// </para>
    /// </summary>
    protected void Beneath(LayoutTree tree, Size size, double gap) => this.beneath.Add((tree, size, gap));

    protected sealed override Laid Build()
    {
        var block = MermaidBlock.Of(Reading);

        var diagram = new LayoutBuilder();
        this.blamed = null;
        var body = Draw(block, diagram);

        var troubled = ContentNested.OwnParts(block.Reading.Root).Where(part => part.Trouble is not null && !part.Derived).ToList();

        // A diagram with nothing to draw is shown as it is written, with what was wrong and what could not be drawn marked in
        // it — or, where nothing says why, every line it could make nothing of.
        if (this.blamed is { } blamed)
        {
            IReadOnlyList<(ContentPart Part, string Reason)> said = [.. troubled.Select(part => (part, part.Trouble!)), .. blamed];
            if (said.Count == 0) said = Unread(block);
            return AsSource(said);
        }

        // Something wrong in what did draw is marked where it is drawn — but only where the reader can put it right there:
        // the block is being written in, and each wrong part is drawn as words that are typed into. Otherwise the block is
        // shown as it is written, with the wrong parts marked in it.
        var drawn = diagram.Seal();
        if (troubled.Count > 0 && (IsReadOnly || troubled.Any(part => !Typed(drawn, part))))
            return AsSource([.. troubled.Select(part => (part, part.Trouble!))]);

        var trouble = troubled.Select(part => Diagnostic.Of(part, part.Trouble!)).ToList();

        var build = new LayoutBuilder();
        // The whole block, so choosing it chooses all of it — but no place for a caret of its own: what is written in a
        // diagram is its lines, and the card they are drawn on is not somewhere to write.
        build.Open(MermaidPiece.Diagram, block.Reading.Root, stops: Stops.None);

        // Everything the block stacks is measured before any of it is placed, and the width of the block is the widest of
        // them. A piece set down before that is known sits off to one side of the block as soon as a later one turns out to
        // be wider — which is how a drawing comes to stand off-centre under its own title, or beside its own legend.
        var (titlePart, titleSays, titleAsWritten) = TitleOf(block);

        FormattedText? title = null;
        string says = string.Empty;
        var maps = false;
        Brush ink = Palette.Heading;

        if (titlePart is not null && !string.IsNullOrWhiteSpace(titleSays))
        {
            // What was written, where the reader is writing in it — a front-matter title says one thing and is written
            // as another, quotes and all, and only the characters they typed can be typed into.
            var written = State.Raw is { } raw && raw.Start <= titlePart.Start && raw.End >= titlePart.End();

            says = written ? titlePart.Text : titleSays!;
            maps = written || titleAsWritten;
            ink = TitleInk;
            title = Text(says, TitleTextSize ?? TitleSize, ink, FontWeights.SemiBold);
        }

        var width = body.Width;

        // The title is set no wider than the room there is; a diagram asked to be wider than that keeps its width.
        if (title is not null) width = Math.Max(width, Math.Min(title.WidthIncludingTrailingWhitespace, Space));
        foreach (var piece in this.beneath) width = Math.Max(width, piece.Size.Width);

        var top = Pad;

        if (title is not null)
        {
            LayoutText.Words(build, title, new Point(Pad, top), width, TextAlignment.Center, titlePart,
                             MermaidPiece.Title, maps: maps, writes: true, ink: ink);
            top += title.Height + TitleGap;
        }

        build.Graft(drawn, new Point(Pad + Math.Max(0, (width - body.Width) / 2), top));
        top += body.Height;

        foreach (var (tree, taken, gap) in this.beneath)
        {
            top += gap;
            build.Graft(tree, new Point(Pad + Math.Max(0, (width - taken.Width) / 2), top));
            top += taken.Height;
        }

        foreach (var reason in trouble.Select(diagnostic => diagnostic.Message).Distinct())
        {
            var text = Text(reason, ReasonSize, Palette.Danger);
            text.MaxTextWidth = Math.Min(Math.Max(width, ReasonRoom), Room);

            top += ReasonGap;
            build.Open(MermaidPiece.Trouble, part: null, new Point(Pad, top), stops: Stops.None);
            build.Draw(new TextMark(text, default, Palette.Danger));
            build.Close();

            width = Math.Max(width, text.Width);
            top += text.Height;
        }

        var size = new Size(width + (Pad * 2), top + Pad);
        Card(build, size);

        build.Close();
        return new Laid(build.Seal(), size, trouble) { Passing = Passing };
    }

    /// <summary>Whether this diagram is drawn as of the moment it is laid, reading the clock as well as the block — see <see cref="Laid.Passing"/>.</summary>
    protected virtual bool Passing => false;

    /// <summary>Whether a part of the tree is drawn as words the reader types into — itself, or something inside it.</summary>
    private static bool Typed(LayoutTree drawn, ContentPart part)
    {
        var inside = part.SelfAndDescendants().ToHashSet();
        return drawn.Root.SelfAndDescendants().Any(piece => piece.Words is { Maps: true } && piece.Part is ContentPart written && inside.Contains(written));
    }

    /// <summary>
    /// Says the diagram is to be shown as it is written — what a diagram with nothing to draw shows — with each part it could
    /// make nothing of blamed, and why. Where it blames nothing and nothing else is wrong, every line it could make nothing of
    /// is.
    /// </summary>
    protected Size AsWritten(LayoutBuilder build, params (ContentPart Part, string Reason)[] blamed)
    {
        this.blamed = blamed;
        return default;
    }

    /// <summary>What the diagram blamed in asking to be shown as it is written, or null where it drew.</summary>
    private IReadOnlyList<(ContentPart Part, string Reason)>? blamed;

    /// <summary>Every line of a diagram shown as it is written that says anything — not its header, a comment or a directive.</summary>
    private static IReadOnlyList<(ContentPart Part, string Reason)> Unread(MermaidBlock block)
    {
        var keyword = block.Keyword?.Text is { Length: > 0 } named ? named : "diagram";
        var reason = $"Nothing written here is something a {keyword} draws, so it is shown as it is written.";

        return [.. block.Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == MermaidKinds.Line && part.Stated() is { Kind: not (Kinds.Comment or MermaidKinds.Directive or MermaidKinds.Header) })
            .Select(line => (line, reason))];
    }

    /// <summary>The card a diagram sits on, same surface and border as a code block. Drawn last but
    /// painted first, so it can be sized to what the diagram turned out to be.</summary>
    private void Card(LayoutBuilder build, Size size)
    {
        var card = new Rect(0, 0, size.Width, size.Height);
        build.Draw(new WashMark(card, Palette.CodeBg));

        var edge = new RectangleGeometry(card, Corner, Corner);
        edge.Freeze();
        build.Draw(new GeometryMark(edge, null, Palette.CodeBorder, 1));
    }

    /// <summary>
    /// What a run of words says, as one line of text — what a chip says it stands for.
    ///
    /// <para>
    /// Which pieces of it are which was settled when it was read (<see cref="WithWordPieces"/>), so this asks the tree and never
    /// the characters. While the reader is writing inside it, it says exactly what they typed — there is nothing to show them but that.
    /// </para>
    /// </summary>
    protected string Shown(ContentPart part) => string.Join(' ', Lines(part, breaks: true).Select(line => line.Says));

    /// <summary>One line of a run of words: what it says, what it stands for, and whether each of its characters is one written there.</summary>
    private readonly record struct Line(string Says, ISourcePart Part, bool Maps);

    /// <summary>What the diagram holds about what its words are made of — asked once, of the tree the builder was handed.</summary>
    private MermaidWords Pieces => this.pieces ??= MermaidWords.Held(Reading.Root.Node);

    private MermaidWords? pieces;

    /// <summary>
    /// The lines a run of words is set as: broken at each line break written in it where <paramref name="breaks"/>, and
    /// otherwise one, with any break in it shown as it was typed.
    ///
    /// <para>
    /// What each piece of the run is — its own characters, an entity code, a line break, a binding — was settled when it was
    /// read (<see cref="WithWordPieces"/>). A line is the pieces between two breaks set one after another, and stands for those
    /// pieces; a run that is only its own characters is one line, standing for the run.
    /// </para>
    /// </summary>
    private IReadOnlyList<Line> Lines(ContentPart part, bool breaks)
    {
        // A name or a label handed over whole is set as the words in it.
        var words = part.Kind == MermaidKinds.Words || part.Node.IsLeaf ? part
            : part.Words() is { Kind: MermaidKinds.Words } inner ? inner
            : part;

        if (Pieces.Of(words.Node) is not { } pieces)
            return [words.Node.IsLeaf ? new Line(words.Text, words, Maps: true) : new Line(string.Empty, words, Maps: false)];

        var writing = Writing(words);
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

    /// <summary>A run of diagram text: the face every diagram label is set in, at the standard size a layout is measured at.</summary>
    private FormattedText Text(string text, double size, Brush ink, FontWeight? weight = null, FontStyle? slant = null) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(BodyFont, weight, slant),
            size,
            ink,
            LayoutText.Density);

    /// <summary>
    /// A part's words, as they read rather than as they were written — entity codes decoded, bindings read.
    ///
    /// <para>
    /// Where the part is a whole block of another language, that content is laid out and handed back in their place:
    /// a builder measures and sets what it is given, so every diagram draws a tune on a node without knowing a tune
    /// from a molecule.
    /// </para>
    /// </summary>
    protected DiagramWords Written(ContentPart? part, ContentPart? hole, double size, Brush ink, FontWeight? weight = null, FontStyle? slant = null)
    {
        var letter = Text("x", size, ink);
        if (hole is not null || part is null) return new DiagramWords(letter, part, hole, letter, ink, maps: false, writes: false);

        if (Inset(part, Space) is { } inset)
            return new DiagramWords(letter, part, null, letter, ink, maps: false, writes: false, inset);

        // A line broken where it was written is set as the lines it was broken into, and pressed rather than typed into, since
        // what it shows is no longer the characters written.
        var lines = Lines(part, breaks: !Writing(part));

        // Words that are words share one letter to be measured against, a letter for each size and colour: every label in a
        // diagram asks how tall a line of it is, and a letter made for each would be shaped once for each.
        var shared = Letter(size, ink);

        return lines is [var line]
            ? new DiagramWords(Text(line.Says, size, ink, weight, slant), line.Part, null, shared, ink, line.Maps, writes: true)
            : new DiagramWords(Text(string.Join('\n', lines.Select(each => each.Says)), size, ink, weight, slant), part, null, shared,
                               ink, maps: false, writes: true);
    }

    /// <summary>The letter words of this size and colour are measured against, set once for the diagram.</summary>
    private FormattedText Letter(double size, Brush ink)
    {
        if (_letters.TryGetValue((size, ink), out var letter)) return letter;

        return _letters[(size, ink)] = Text("x", size, ink);
    }

    private readonly Dictionary<(double Size, Brush Ink), FormattedText> _letters = [];

    /// <summary>As <see cref="Written"/>, a line to each line break written in it, and each line set no wider than
    /// <paramref name="width"/> (<see cref="Fitted"/>).</summary>
    protected IReadOnlyList<DiagramWords> Wrapped(ContentPart? part, ContentPart? hole, double size, Brush ink, double width,
                                                  FontWeight? weight = null, FontStyle? slant = null)
    {
        var whole = Written(part, hole, size, ink, weight, slant);
        if (part is null || hole is not null) return [whole];

        // Another content is one thing, however wide it turned out: breaking it would be breaking a tune in half.
        if (whole.Nested) return [whole];

        var lines = Lines(part, breaks: true);
        if (lines.Count == 1 && whole.Width <= width) return [whole];

        var letter = Text("x", size, ink);
        return [.. lines.Select(line => new DiagramWords(Fitted(Text(line.Says, size, ink, weight, slant), width), line.Part, null, letter,
                                                         ink, line.Maps, writes: line.Maps))];
    }

    /// <summary>
    /// A line of words set no wider than <paramref name="width"/>: where it is wider, the type engine breaks it after the last
    /// space that fits, or inside a word longer than the width, into lines set in the middle of one another — as wide as the
    /// widest of them, so it is measured and placed as what it takes.
    /// </summary>
    private static FormattedText Fitted(FormattedText text, double width)
    {
        if (text.Width <= width) return text;

        text.MaxTextWidth = Math.Max(1, width);
        text.TextAlignment = TextAlignment.Center;
        text.MaxTextWidth = Math.Max(1, text.Width + Hair);

        return text;
    }

    /// <summary>How much wider than its widest line a broken line's room is set, so measuring it again breaks it the same way.</summary>
    private const double Hair = 0.01;

    /// <summary>Whether the reader is writing inside <paramref name="part"/>, where it is shown exactly as typed.</summary>
    private bool Writing(ContentPart part) => State.Raw is { } raw && raw.Start <= part.Start && raw.End >= part.End;

    /// <summary>Words the diagram works out rather than anybody writing (a share, a total): pressed as
    /// the <paramref name="part"/> they stand for, no caret. See <see cref="DiagramWords"/>.</summary>
    protected DiagramWords Worked(string says, ContentPart? part, double size, Brush ink, FontWeight? weight = null,
                                  FontStyle? slant = null) =>
        new(Text(says, size, ink, weight, slant), part, null, Text("x", size, ink), ink, maps: false, writes: false);

    /// <summary>
    /// The icon something names, set in the font its glyph is in and standing for what names it — or null where it names no
    /// icon the app draws (<see cref="WithIcons"/>), and what is drawn instead is the diagram's to say. What is asked about may
    /// be the icon's own words, or what holds it: a line naming an icon, or metadata setting one.
    /// </summary>
    protected DiagramWords? Iconed(ContentPart? part, double size, Brush ink)
    {
        var named = part;
        while (named is not null && named.Node is not RenderedIconNode) named = named.Parent;
        named ??= part?.SelfAndDescendants().FirstOrDefault(each => each.Node is RenderedIconNode);
        if (named?.Node is not RenderedIconNode icon) return null;

        var face = icon.Family is null ? Style.Face(BodyFont, null, null) : new Typeface(IconCatalog.FluentFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var glyph = new FormattedText(icon.Glyph, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, ink, LayoutText.Density);

        return new DiagramWords(glyph, named, null, Text("x", size, ink), ink, maps: false, writes: false);
    }

    /// <summary>
    /// The content written inside <paramref name="part"/>, laid out to fit <paramref name="room"/> — or null
    /// where nothing is written inside it, or it is being shown as the characters it was typed as.
    ///
    /// <para>
    /// Which language reads it was settled by a stage and is hanging on the node; all that is decided here is
    /// how much room it gets, which is this builder's to decide and nobody else's.
    /// </para>
    /// </summary>
    protected ContentInset? Inset(ContentPart? part, double room)
    {
        if (part is not { Length: > 0 }) return null;
        if (State.Raw is { } raw && raw.Start <= part.Start && raw.End >= part.End) return null;

        return Nested(part, room);
    }

    /// <summary>How a diagram sets the source it could not lay out at all: as the lines it was written as.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(SourceFont),
            SourceSize,
            Palette.Text,
            LayoutText.Density);
}

/// <summary>The shape every diagram's builder has: the block read into the diagram it describes once
/// (<see cref="Of"/>), then drawn (<see cref="Draw(TDiagram, LayoutBuilder)"/>). Everything else —
/// title, trouble text, card — is <see cref="MermaidBuilder"/>'s.</summary>
/// <typeparam name="TDiagram">The diagram as its model reads it, every part it was written in kept.</typeparam>
internal abstract class MermaidBuilder<TDiagram>(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
    : MermaidBuilder(reading, state, style, isReadOnly, nesting)
    where TDiagram : class
{
    /// <summary>The diagram as it was read — null until it has been.</summary>
    protected TDiagram? Diagram { get; private set; }

    /// <summary>Reads the block into the diagram it describes: <c>PieChart.Of</c>, <c>VennDiagram.Of</c>.</summary>
    protected abstract TDiagram Of(MermaidBlock block);

    /// <summary>Draws the diagram into <paramref name="build"/> at the origin and hands back the room it
    /// took. May throw — shown as written, with the reason, on failure.</summary>
    protected abstract Size Draw(TDiagram diagram, LayoutBuilder build);

    /// <summary>
    /// This diagram as a graph, for folding: its node ids, and the links between them. Null for a diagram that is not
    /// graph-shaped, which is never folded at all.
    /// </summary>
    protected virtual DiagramChart? Chart(TDiagram diagram) => null;

    /// <summary>What the front matter asked to be folded away, read once before the diagram is drawn.</summary>
    protected NexaflowConfig Folds { get; private set; } = NexaflowConfig.None;

    /// <summary>How much of the diagram is drawn, worked out once before it is.</summary>
    protected DiagramExpansion Folding { get; private set; } = DiagramExpansion.None;

    /// <summary>Whether a node is drawn at all. Everything is, unless something asked otherwise.</summary>
    protected bool Draws(string id) => Folding.Draws(id);

    /// <summary>The producer's name for a node — what an opening is remembered under, never the positional id.</summary>
    protected string KeyFor(string id) => Folds.KeyFor(id);

    /// <summary>
    /// Draws the chip a node carries, where it carries one: over the top right-hand corner of what it fills, as a piece
    /// of its own, so the node's body and its folding are two separate things to press.
    /// </summary>
    protected void Chipped(LayoutBuilder build, string id, Rect bounds, ISourcePart? part, string? label = null)
    {
        if (Folding.FoldOf(id) is not { } fold) return;

        DiagramChip.Draw(build, DiagramChip.On(bounds), part, fold, id, label,
                         Worked(DiagramChip.Says(fold), part as ContentPart, DiagramChip.TextSize, Palette.Text),
                         Ink.Surface, new DiagramStroke(Palette.CodeBorder, 1));
    }

    /// <summary>
    /// The nodes offering what is left of each over-wide set of children, ready to be laid out with the rest and drawn
    /// afterwards. Nothing, where nothing is over-wide.
    /// </summary>
    /// <param name="cells">The cell a node was given, or null for one this diagram did not lay out.</param>
    protected DiagramSpill Spilled(Func<string, DiagramCell?> cells) =>
        DiagramSpill.Of(Folding, cells, More, SpillPad);

    /// <summary>The room a node offering leftovers keeps round its words.</summary>
    private const double SpillPad = 10;

    /// <summary>What a node offering <paramref name="count"/> children says.</summary>
    private DiagramWords More(int count) =>
        Worked($"+{count} more", null, SpillSize, Palette.TextMuted);

    /// <summary>How big it says it.</summary>
    private const double SpillSize = 11;

    protected sealed override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        Diagram = Of(block);
        Folds = WithFolds.Of(block.Reading.Root.Node);

        // Worked out before anything is placed, so a node that is not drawn is never given a cell — rather than taken
        // out afterwards, which would leave a hole where it stood and a line running to nothing.
        Folding = Chart(Diagram) is { } chart
            ? DiagramExpansion.Of(Folds, chart, Style.Expansion?.Expansion)
            : DiagramExpansion.None;

        return Draw(Diagram, build);
    }
}
