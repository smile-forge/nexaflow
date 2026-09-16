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
    }

    protected MarkdownPalette Palette { get; }

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
    protected static Editing.ContentElement Host(string source, DiagramRenderOptions options,
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
    /// Reads the block. The parse every diagram shares by default; a type with stages of its own — a pie, whose slices
    /// are given their colours by one — runs its own pipeline instead.
    /// </summary>
    protected virtual ContentNode Reading(string source) => MermaidParser.Parse(source);

    /// <summary>
    /// The title to set over the diagram: the part it was written in, and what it says. The front matter's by default;
    /// a diagram that names its own title on its header or in a statement says which wins.
    /// </summary>
    protected virtual (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) => (block.Title, block.TitleText);

    /// <summary>The ink the title is set in: the theme's heading, unless the diagram's front matter asks for another.</summary>
    protected virtual Brush TitleInk => Palette.Heading;

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

            var ink = TitleInk;
            var title = Text(says, TitleSize, ink, FontWeights.SemiBold);
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

    /// <summary>A colour as the front matter or a style wrote it, or null where it wrote none this understands.</summary>
    protected static Brush? Colour(string? written)
    {
        if (DiagramBrushes.ParseCss(written) is not { } colour) return null;

        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>A run of diagram text: the face every diagram label is set in, at this pixel density.</summary>
    protected FormattedText Text(string text, double size, Brush ink, FontWeight? weight = null) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(DiagramText.BodyFont, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            ink,
            PixelsPerDip);

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
