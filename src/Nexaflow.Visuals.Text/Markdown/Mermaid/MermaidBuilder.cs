using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

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

    /// <summary>What a shape draws — its outline, filled — which is what a press inside it lands on. See <see cref="DiagramShapes.Draw"/>.</summary>
    public const string Shape = "Shape";

    /// <summary>What a line draws — an axis's line and ticks — which is what a press near it lands on. See <see cref="DiagramAxis.Draw"/>.</summary>
    public const string Line = "Line";
}

/// <summary>
/// What every Mermaid diagram's builder shares: reading the block once, the title over the diagram, what to say
/// about what could not be read, and the element a diagram is shown in.
///
/// <para>
/// Each diagram type is its own builder, because each draws its own way — a pie is slices and a legend, a sequence
/// diagram lifelines and messages. What they have in common sits around that: the block is read by
/// <see cref="MermaidParser"/> for all of them, a front-matter title means the same thing over any of them, and
/// trouble anywhere in the block is set beneath whatever did draw.
/// </para>
/// <para>
/// <b>A diagram is drawn at the origin and framed here.</b> <see cref="Draw"/> draws into a layout of its own, and it
/// is grafted under the title once the title's height is known — so no diagram has to be told where its top is, and
/// the title can be set across the width the diagram turned out to be.
/// </para>
/// </summary>
internal abstract class MermaidBuilder : ContentBuilder
{
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

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

    /// <param name="room">How wide the diagram may be. Infinity, or anything not a width, is as wide as it likes.</param>
    /// <param name="writing">Whether somebody is writing in the block — see <see cref="Writing"/>.</param>
    protected MermaidBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing = false)
        : base(state.Source)
    {
        State = state;
        Palette = palette;
        PixelsPerDip = pixelsPerDip;
        Room = double.IsNaN(room) || room <= 0 ? double.PositiveInfinity : room;
        Writing = writing;
        Ink = new DiagramInk(palette);
    }

    protected MarkdownPalette Palette { get; }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    protected DiagramInk Ink { get; }

    /// <summary>
    /// What is being written, and where: the source, the caret, and the stretch being shown as its own characters rather
    /// than as what it says — which is how a title set from front matter is written in.
    /// </summary>
    protected EditState State { get; }

    /// <summary>
    /// Whether somebody is writing in the block rather than only reading it. A diagram being written draws what is still to
    /// be written — a hole where a label or a value goes, a row for a slice with nothing yet to draw — and one only being
    /// read draws what there is.
    /// </summary>
    protected bool Writing { get; }

    protected double PixelsPerDip { get; }

    /// <summary>How wide the block may be laid out — infinity where nothing says.</summary>
    protected double Room { get; }

    /// <summary>
    /// How wide what a diagram draws may be: the room the block was given, less the card it is drawn on. What a builder
    /// fits its drawing into, and the whole of what the room means to it.
    /// </summary>
    protected double Space => double.IsInfinity(Room) ? Room : Math.Max(1, Room - (Pad * 2));

    /// <summary>
    /// The element a Mermaid block is shown in: read-only, because a diagram is not typed into — but selectable, since
    /// what a diagram draws carries the characters it was written as.
    /// </summary>
    /// <param name="build">Lays a block's source out for a palette, a pixel density, a width, and whether it is being written in.</param>
    /// <param name="readOnly">
    /// Whether the block is only looked at. A diagram with nothing in it anybody typed — a header naming no type, shown
    /// as its own source — is; one whose values are the numbers the reader wrote is not, and the host decides whether
    /// any keys reach it.
    /// </param>
    internal static Editing.ContentElement Host(string source, DiagramRenderOptions options,
                                                 Func<EditState, MarkdownPalette, double, double, bool, Laid> build,
                                                 bool readOnly = true) =>
        new(source, options.Palette,
            new MermaidContent((state, room, pixelsPerDip, looking) => build(state, options.Palette, pixelsPerDip, room, !looking)))
        {
            IsReadOnly = readOnly,

            // Where the diagram sits inside the fence, so the host never mistakes the block for one that is its
            // content and nothing else.
            SourceStart = options.SourceOffset,
            SourceLength = source.Length,

            // Where the pointer is over something that can be written in, the element says so itself.
            Cursor = Cursors.Arrow,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 10),
        };

    /// <summary>
    /// Draws the diagram into <paramref name="build"/> with its top left at the origin, and hands back how much room it
    /// took. May throw: whatever it was reading is shown as written, with the reason.
    /// </summary>
    protected abstract Size Draw(MermaidBlock block, LayoutBuilder build);

    /// <summary>
    /// Reads the block: parsed, and run through its type's stages with a hole wherever something is still to be written, where
    /// somebody is writing in it (<see cref="MermaidParser.Read"/>).
    /// </summary>
    protected ContentNode Reading(string source) => MermaidParser.Read(source, holes: Writing);

    /// <summary>
    /// The title to set over the diagram: the part it was written in, and what it says — the diagram's own where it writes
    /// one, and the front matter's otherwise (<see cref="MermaidBlock.Title"/>).
    /// </summary>
    protected virtual (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) => (block.Title, block.TitleText);

    /// <summary>The colour the diagram's front matter asks its title to be written in, or null for the theme's heading.</summary>
    protected virtual string? TitleColour => null;

    /// <summary>How big the diagram's front matter asks its title to be set, or null for the size every diagram's title is.</summary>
    protected virtual double? TitleTextSize => null;

    protected sealed override Laid Read()
    {
        var block = MermaidBlock.Of(Reading(Source));

        var diagram = new LayoutBuilder();
        var body = Draw(block, diagram);

        var trouble = block.Reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null && !part.Derived)
            .Select(part => new Diagnostic(part.Start, Math.Max(part.Length, 1), DiagnosticSeverity.Error, part.Trouble!))
            .ToList();

        var build = new LayoutBuilder();
        // The whole block, so choosing it chooses all of it — but no place for a caret of its own: what is written in a
        // diagram is its lines, and the card they are drawn on is not somewhere to write.
        build.Open(MermaidPiece.Diagram, block.Reading.Root, stops: Stops.None);

        var width = body.Width;
        var top = Pad;

        var (titlePart, titleText) = TitleOf(block);
        if (titlePart is not null && !string.IsNullOrWhiteSpace(titleText))
        {
            // What was written, where the reader is writing in it — a front-matter title says one thing and is written
            // as another, quotes and all, and only the characters they typed can be typed into.
            var written = State.Raw is { } raw && raw.Start <= titlePart.Start && raw.End >= titlePart.End();
            var says = written ? titlePart.Text : MermaidText.Decode(titleText!);

            var ink = Ink.Written(TitleColour) ?? Palette.Heading;
            var title = Text(says, TitleTextSize ?? TitleSize, ink, FontWeights.SemiBold);
            // The title is set no wider than the room there is; a diagram asked to be wider than that keeps its width.
            width = Math.Max(width, Math.Min(title.WidthIncludingTrailingWhitespace, Space));

            LayoutText.Words(build, title, new Point(Pad, top), width, TextAlignment.Center, titlePart,
                             MermaidPiece.Title, maps: says == titlePart.Text, writes: true, ink: ink);
            top += title.Height + TitleGap;
        }

        build.Graft(diagram.Seal(), new Point(Pad, top));
        top += body.Height;

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
        return new Laid(build.Seal(), size, trouble);
    }

    /// <summary>
    /// The block as it was written, laid out as its own characters — what a diagram with nothing it can draw shows,
    /// so a reader can see what they wrote and select any of it.
    /// </summary>
    protected Size AsWritten(LayoutBuilder build)
    {
        var shown = LayoutText.Shown(Source, Characters(Source.Length == 0 ? " " : Source), []);
        build.Graft(shown.Tree);
        return shown.Size;
    }

    /// <summary>
    /// The card a diagram sits on: the surface a code block is drawn on, with the same border round it.
    ///
    /// <para>
    /// Drawn last and painted first — a wash goes down before any ink in the tree, whoever drew it — so the card can be
    /// the size the diagram turned out to be rather than a guess made before anything was laid out.
    /// </para>
    /// </summary>
    private void Card(LayoutBuilder build, Size size)
    {
        var card = new Rect(0, 0, size.Width, size.Height);
        build.Draw(new WashMark(card, Palette.CodeBg));

        var edge = new RectangleGeometry(card, Corner, Corner);
        edge.Freeze();
        build.Draw(new GeometryMark(edge, null, Palette.CodeBorder, 1));
    }

    /// <summary>
    /// What a part of the block says on the page: its own characters where somebody is writing in them, and otherwise what
    /// they read as — an entity code as the character it stands for (<see cref="MermaidText"/>).
    /// </summary>
    protected string Shown(ContentPart part) =>
        State.Raw is { } raw && raw.Start <= part.Start && raw.End >= part.End ? part.Text : MermaidText.Decode(part.Text);

    /// <summary>A run of diagram text: the face every diagram label is set in, at this pixel density.</summary>
    private FormattedText Text(string text, double size, Brush ink, FontWeight? weight = null) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(DiagramText.BodyFont, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            ink,
            PixelsPerDip);

    /// <summary>
    /// What somebody wrote, as the diagram sets it: <paramref name="part"/>'s words — shown as what they say (<see cref="Shown"/>),
    /// and typed into where that is the characters written — or <paramref name="hole"/>, where one stands because nothing is
    /// written there yet. See <see cref="DiagramWords"/>.
    /// </summary>
    protected DiagramWords Written(ContentPart? part, ContentPart? hole, double size, Brush ink, FontWeight? weight = null)
    {
        var letter = Text("x", size, ink);
        if (hole is not null || part is null) return new DiagramWords(letter, part, hole, letter, ink, maps: false, writes: false);

        var says = Shown(part);
        return new DiagramWords(Text(says, size, ink, weight), part, null, letter, ink, maps: says == part.Text, writes: true);
    }

    /// <summary>
    /// What somebody wrote, as <see cref="Written"/> sets it, wrapped to <paramref name="width"/>: broken after a space where a line
    /// would run past it, or inside a word too long for a line of its own — each line typed into as the characters it holds.
    /// Words that read as something other than their characters are wrapped as they read, and only pressed.
    /// </summary>
    protected IReadOnlyList<DiagramWords> Wrapped(ContentPart? part, ContentPart? hole, double size, Brush ink, double width, FontWeight? weight = null)
    {
        var whole = Written(part, hole, size, ink, weight);
        if (part is null || hole is not null || whole.Width <= width) return [whole];

        var says = Shown(part);
        var maps = says == part.Text;
        var letter = Text("x", size, ink);
        var lines = new List<DiagramWords>();

        for (var start = 0; start < says.Length;)
        {
            var end = start + 1;
            var broken = -1;
            while (end < says.Length && Text(says[start..(end + 1)], size, ink, weight).Width <= width)
            {
                end++;
                if (says[end - 1] == ' ') broken = end;
            }

            if (end < says.Length && broken > start) end = broken;

            var line = says[start..end];
            lines.Add(new DiagramWords(Text(line, size, ink, weight), maps ? new SourceSpan(part.Start + start, end - start) : part, null, letter, ink, maps, writes: maps));
            start = end;
        }

        return lines;
    }

    /// <summary>
    /// Words the diagram works out rather than anybody writing them — a share, a total, the names a union overlaps — pressed as
    /// the <paramref name="part"/> they stand for, where they stand for one, and nowhere to put a caret. See <see cref="DiagramWords"/>.
    /// </summary>
    protected DiagramWords Worked(string says, ContentPart? part, double size, Brush ink, FontWeight? weight = null) =>
        new(Text(says, size, ink, weight), part, null, Text("x", size, ink), ink, maps: false, writes: false);

    /// <summary>How a diagram sets the source it could not lay out at all: as the lines it was written as.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SourceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            SourceSize,
            Palette.Text,
            PixelsPerDip);
}

/// <summary>
/// The shape every diagram's builder has: the block read into the diagram it describes, once (<see cref="Of"/>), and that
/// drawn (<see cref="Draw(TDiagram, LayoutBuilder)"/>) — <c>PieChart</c> by <c>PieBuilder</c>, <c>VennDiagram</c> by
/// <c>VennBuilder</c>. Everything round the drawing — the title, what could not be read, the card — is
/// <see cref="MermaidBuilder"/>'s.
/// </summary>
/// <typeparam name="TDiagram">The diagram as its model reads it, every part it was written in kept.</typeparam>
internal abstract class MermaidBuilder<TDiagram>(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
    : MermaidBuilder(state, palette, pixelsPerDip, room, writing)
    where TDiagram : class
{
    /// <summary>The diagram as it was read — null until it has been.</summary>
    protected TDiagram? Diagram { get; private set; }

    /// <summary>Reads the block into the diagram it describes: <c>PieChart.Of</c>, <c>VennDiagram.Of</c>.</summary>
    protected abstract TDiagram Of(MermaidBlock block);

    /// <summary>
    /// Draws the diagram into <paramref name="build"/> with its top left at the origin, and hands back how much room it took.
    /// May throw: whatever it was reading is shown as written, with the reason.
    /// </summary>
    protected abstract Size Draw(TDiagram diagram, LayoutBuilder build);

    protected sealed override Size Draw(MermaidBlock block, LayoutBuilder build) => Draw(Diagram = Of(block), build);
}
