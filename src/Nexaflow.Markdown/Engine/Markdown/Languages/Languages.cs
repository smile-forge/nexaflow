using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Markdown.Packet;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix.Aztec.Stages;
using Nexaflow.Markdown.Matrix.DataMatrix.Stages;
using Nexaflow.Markdown.Matrix.Pdf417.Stages;
using Nexaflow.Markdown.Matrix.Qr.Stages;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Packet;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using Nexaflow.Markdown.WordCloud.Stages;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Icons;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Localization;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Visuals.Text.Markdown.Treemap;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Visuals.Text.Markdown.TreeView;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Markdown.PlainText;
using Nexaflow.Markdown.Layout.PlainText;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Mermaid.Venn.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Packet.Stages;
using Nexaflow.Markdown.Treemap.Stages;
using Nexaflow.Markdown.TreeView.Stages;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Markdown.Mermaid.Quadrant.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Markdown.Mermaid.Timeline.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Timeline;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Markdown.Mermaid.Pie.Stages;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Markdown.Mermaid.Radar.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Ishikawa;
using Nexaflow.Markdown.Mermaid.Class;
using ClassDiagramStages = Nexaflow.Markdown.Mermaid.Class.Stages;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Markdown.Mermaid.Architecture.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;
using Nexaflow.Markdown.Mermaid.C4;
using C4Stages = Nexaflow.Markdown.Mermaid.C4.Stages;
using SequenceStages = Nexaflow.Markdown.Mermaid.Sequence.Stages;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Markdown.Mermaid.C4;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Markdown.Mermaid.Cynefin.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Journey;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Cynefin;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Markdown.Mermaid.Mindmap.Stages;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Markdown.Mermaid.Kanban.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Mindmap;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Kanban;
using Nexaflow.Markdown.Mermaid.Flowchart;
using FlowchartStages = Nexaflow.Markdown.Mermaid.Flowchart.Stages;
using Nexaflow.Markdown.Mermaid.Swimlane;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Mermaid.Er;
using ErStages = Nexaflow.Markdown.Mermaid.Er.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Er;
using Nexaflow.Markdown.Mermaid.Block;
using BlockStages = Nexaflow.Markdown.Mermaid.Block.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Block;
using Nexaflow.Markdown.Mermaid.State;
using StateStages = Nexaflow.Markdown.Mermaid.State.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.State;
using Nexaflow.Markdown.Mermaid.Requirement;
using RequirementStages = Nexaflow.Markdown.Mermaid.Requirement.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Requirement;
using Nexaflow.Markdown.Mermaid.Gantt;
using GanttStages = Nexaflow.Markdown.Mermaid.Gantt.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt;
using Nexaflow.Markdown.Mermaid.Git;
using GitStages = Nexaflow.Markdown.Mermaid.Git.Stages;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Git;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Markdown.EventModeling.Stages;
using Nexaflow.Visuals.Text.Markdown.EventModeling;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>
/// Every language that ships, each described: what parses it, what its parse is worked over by, and what lays it out. The
/// engine runs them (<see cref="ContentEngine"/>); nothing here runs anything.
/// </summary>
internal static class Shipped
{
    /// <summary>What every Mermaid block is worked over by once all else has been: what its words are made of.</summary>
    private static readonly WithWordPieces WordPieces = new();

    /// <summary>
    /// A document. Named by no fence: it is what content is written in where nothing says otherwise.
    /// </summary>
    public static readonly ContentLanguage Markdown = new(
        Reads: static _ => false,
        Parser: MarkdownParser.Parsing,
        Stages: static (_, show) =>
        [
            // First, because what a link by name means is written anywhere in the document, and the links after ask for it.
            new WithTargets(show.Targets),
            new WithReferences(show.Targets),

            // Every heading promoted into the outline, which its sections and the names its headings answer to are read from.
            new WithOutline(),

            new WithImages(show.Inputs.Pictures),
            new WithLinks(show.Inputs.Links),

            // Last of what is read, because a block is only the same as it was when everything worked out about it is too.
            show.Unchanged,

            // After it: a block shown as written is not what was read, and is never kept as if it were.
            show.Shown is { } zone ? new ShowBlocksAsWritten(zone, show.At, show.Reads) : null,
        ],
        Builder: ContentLayouts.Markdown)
        {
            Writable = true,
            Windowed = MarkdownParser.Windowed,
            Bounds = MarkdownParser.Bounds,
            Seam = MarkdownGaps.Between,
            Editing = MarkdownEdits.Instance,
            Transpile = Transpiles.By<MarkdownParser>(),
        };

    /// <summary>
    /// RTF: a document written as formatting rather than marks — what Outlook keeps a message's body in, and what a word
    /// processor saves as rich text. Read into the same paragraphs, tables, links and pictures a document is
    /// (<see cref="RtfParser"/>) and drawn by the same builder, with the fonts, sizes, colours, indents and alignment it spells
    /// out. Only read: nobody writes RTF by hand, so it is never written in here, and a reader reads it as what it draws.
    /// </summary>
    public static readonly ContentLanguage Rtf = new(
        Reads: static word => RtfParser.Language.Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: RtfParser.Parsing,
        Stages: static (_, show) =>
        [
            new WithLinks(show.Inputs.Links),

            // Last of what is read, because a block is only the same as it was when everything worked out about it is too.
            show.Unchanged,
        ],
        Builder: ContentLayouts.Markdown)
        {
            Windowed = RtfParser.Windowed,

            // Laid by the markdown builder, whose blocks say the room between them — at a seam between two units as anywhere.
            Seam = MarkdownGaps.Between,
            Editing = new ReadAsDrawn(),
        };

    /// <summary>
    /// <c>mermaid</c>: a block whose header names the diagram it is, held whole as written in that diagram's own language — which
    /// is what is drawn, nothing of this one being (<see cref="MermaidFenceParser"/>). A header naming no diagram is shown as
    /// written, the keyword marked.
    /// </summary>
    public static readonly ContentLanguage Mermaid = new(
        Reads: static word => "mermaid".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(MermaidFenceParser.Parse(source, MermaidFenced)),
        Stages: static (_, _) => [],
        Builder: ContentLayouts.MermaidFence)
        {
            // Whoever writes in the block writes in the diagram, which is laid out as this is — and whose own handler is told the edit.
            Writable = true,
        };

    /// <summary>
    /// Whether a <c>mermaid</c> block headed by <paramref name="word"/> is held in a language that reads it — one of the languages
    /// written in Mermaid's syntax, not any language at all, so a <c>mermaid</c> block headed <c>latex</c> is the parse error it is.
    /// </summary>
    private static bool MermaidFenced(string word) => WrittenInMermaid.Any(language => language.Reads(word));

    /// <summary>The languages written in Mermaid's syntax, any of which a <c>mermaid</c> block may be written in.</summary>
    private static IReadOnlyList<ContentLanguage> WrittenInMermaid => _writtenInMermaid ??= [Pie, Radar, XyChart, Ishikawa, Class, Architecture, C4, C4Sequence, Sequence, Journey, Cynefin, Mindmap, Kanban, Flowchart, Swimlane, Er, Block, State, Requirement, Gantt, GitGraph, EventModeling, Sankey, Venn, Quadrant, Timeline, Packet, Treemap, TreeView];

    private static IReadOnlyList<ContentLanguage>? _writtenInMermaid;

    /// <summary>
    /// UML class notation written its own way — <see href="https://www.nomnoml.com/"/> — read by a parser of its own and drawn
    /// as the class diagram it describes. Nothing in it is worked out over the whole block: every line says what it means.
    /// </summary>
    public static readonly ContentLanguage Nomnoml = new(
        Reads: static word => "nomnoml".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(NomnomlParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: ContentLayouts.Nomnoml)
            {
                Name = "nomnoml",
                                DisplayName = static () => Str.Get("Markdown.Language.Nomnoml"),
                Icon = IconRef.Fluent("draw_shape"),
                DefaultBlock = """
                    ```nomnoml
                    [Order] -> [Customer]
                    ```
                    """,
            };

    /// <summary>
    /// A packet, or a register — its fields, each its bits and a label — read by a parser of its own, worked over by the stages it offers below, and drawn as rows of bits
    /// (<see cref="PacketParser"/>). Written Mermaid's way, so a <c>mermaid</c> block headed <c>packet</c> is handed here, and fenced as
    /// itself it needs no header.
    /// </summary>
    public static readonly ContentLanguage Packet = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && PacketParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(PacketParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = PacketConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new PlaceFields(config),
                new WithConfig<PacketConfig>(config),
                show.Writing ? new WithHoles(PacketParser.Holds) : null,
                .. Hosted(show),

                // What every run of words is made of, a label's escapes and line endings reading as what they stand for.
                new WithWordPieces(PacketStandIns.Decoded),
            ];
        },
        Builder: ContentLayouts.Packet)
        {
            Writable = true,
            Editing = new DiagramEditing(new PacketEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, PacketParser.Parse),
            Transpile = Transpiles.By<PacketParser>(),
            Name = PacketParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Packet"),
            Icon = IconRef.Fluent("number_row"),
            DefaultBlock = """
                ```packet
                0-15: "Source Port"
                16-31: "Destination Port"
                ```
                """,
        };

    /// <summary>
    /// A sankey diagram — flows between nodes, each a row of three fields — read by a parser of its own (<see cref="SankeyParser"/>)
    /// and drawn as ribbons as thick as they are worth. Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>sankey</c>
    /// is handed here, and fenced as itself it needs no header. What the engine works it over with: what the front matter asks
    /// for, hung on the block; a hole wherever something is still to be written while somebody writes in it; what a host binds
    /// into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Sankey = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && SankeyParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(SankeyParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new WithConfig<SankeyConfig>(SankeyConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(SankeyParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Sankey)
        {
            Writable = true,
            Editing = new DiagramEditing(new SankeyEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, SankeyParser.Parse),
            Transpile = Transpiles.By<SankeyParser>(),
            Name = SankeyParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Sankey"),
            Icon = IconRef.Fluent("flow"),
            DefaultBlock = """
                ```mermaid
                sankey

                Source,Target,10
                ```
                """,
        };

    /// <summary>
    /// A Venn diagram — sets, the unions where they overlap, the items written in either — read by a parser of its own
    /// (<see cref="VennParser"/>) and drawn as circles sized by area. Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>venn-beta</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: each set and
    /// union gathered with the items written in it; which region every part stands for; what styles each; what the front matter
    /// asks for, hung on the block; a hole wherever something is still to be written while somebody writes in it; what a host
    /// binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Venn = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && VennParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(VennParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new GroupRegions(),
            new ResolveRegions(),
            new Nexaflow.Markdown.Mermaid.Venn.Stages.ResolveStyles(),
            new WithConfig<VennConfig>(VennConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(VennParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Venn)
        {
            Writable = true,
            Editing = new DiagramEditing(new VennEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, VennParser.Parse),
            Transpile = Transpiles.By<VennParser>(),
            Name = VennParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Venn"),
            Icon = IconRef.Fluent("shape_intersect"),
            DefaultBlock = """
                ```mermaid
                venn-beta
                    set A
                    set B
                    union A,B["Both"]
                ```
                """,
        };

    /// <summary>
    /// A quadrant chart — four quadrants, two axes and the points standing between them — read by a parser of its own
    /// (<see cref="QuadrantParser"/>) and drawn as a grid of quadrants with its points where they stand. Written in Mermaid's syntax,
    /// so a <c>mermaid</c> block headed <c>quadrantChart</c> is handed here, and fenced as itself it needs no header. What the engine
    /// works it over with: where each point stands and what it is drawn with, and what the front matter asks for, hung on the
    /// block; a hole wherever something is still to be written while somebody writes in it; what a host binds into it; and what
    /// each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Quadrant = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && QuadrantParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(QuadrantParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolvePoints(QuadrantConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(QuadrantParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Quadrant)
        {
            Writable = true,
            Editing = new DiagramEditing(new QuadrantEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, QuadrantParser.Parse),
            Transpile = Transpiles.By<QuadrantParser>(),
            Name = QuadrantParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Quadrant"),
            Icon = IconRef.Fluent("sub_grid"),
            DefaultBlock = """
                ```mermaid
                quadrantChart
                    title Priorities
                    x-axis Low effort --> High effort
                    y-axis Low value --> High value
                    Idea: [0.3, 0.7]
                ```
                """,
        };

    /// <summary>
    /// A pie chart — a slice per line, each its label and its value — read by a parser of its own (<see cref="PieParser"/>) and drawn
    /// as wedges with a legend. Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>pie</c> is handed here, and fenced as
    /// itself it needs no header. What the engine works it over with: the colour each slice is drawn in and whether it is picked out;
    /// each slice's share of the whole; what the front matter asks for, hung on the block; a hole wherever something is still to be
    /// written while somebody writes in it; what a host binds into it; and what each run of words is made of, a label's escapes and
    /// line endings reading as what they stand for.
    /// </summary>
    public static readonly ContentLanguage Pie = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && PieParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(PieParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = PieConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new ResolveSlices(config),
                new ResolveShares(show.Writing),
                new WithConfig<PieConfig>(config),
                show.Writing ? new WithHoles(PieParser.Holds) : null,
                .. Hosted(show),
                new WithWordPieces(PieStandIns.Decoded),
            ];
        },
        Builder: ContentLayouts.Pie)
        {
            Writable = true,
            Editing = new DiagramEditing(new PieEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, PieParser.Parse),
            Transpile = Transpiles.By<PieParser>(),
            Name = PieParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Pie"),
            Icon = IconRef.Fluent("data_pie"),
            DefaultBlock = """
                ```mermaid
                pie
                    title Pets
                    "Dogs" : 40
                ```
                """,
        };

    /// <summary>
    /// A radar chart — axes as spokes, and curves reaching along them — read by a parser of its own (<see cref="RadarParser"/>) and
    /// drawn over a graticule. Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>radar-beta</c> is handed here, and
    /// fenced as itself it needs no header. What the engine works it over with: which axis each of a curve's values is for, and what
    /// is said where they do not fit; what the options set, hung on the block; a hole wherever something is still to be written while
    /// somebody writes in it; what a host binds into it; and what each run of words is made of, a label's escapes and line endings
    /// reading as what they stand for.
    /// </summary>
    public static readonly ContentLanguage Radar = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && RadarParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(RadarParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = RadarConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new ResolveCurves(config, show.Writing),
                new ResolveOptions(config),
                show.Writing ? new WithHoles(RadarParser.Holds) : null,
                .. Hosted(show),
                new WithWordPieces(RadarStandIns.Decoded),
            ];
        },
        Builder: ContentLayouts.Radar)
        {
            Writable = true,
            Editing = new DiagramEditing(new RadarEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, RadarParser.Parse),
            Transpile = Transpiles.By<RadarParser>(),
            Name = RadarParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Radar"),
            Icon = IconRef.Fluent("radar"),
            DefaultBlock = """
                ```mermaid
                radar-beta
                    axis a["Speed"], b["Power"], c["Range"]
                    curve x["Model X"]{3, 4, 2}
                ```
                """,
        };

    /// <summary>
    /// An XY chart — bars and lines over an x-axis of categories or a range — read by a parser of its own (<see cref="XyParser"/>) and
    /// drawn either way up. Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>xychart</c> is handed here, and fenced as
    /// itself it needs no header. What the engine works it over with: what the front matter asks for, hung on the block; a hole wherever
    /// something is still to be written while somebody writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage XyChart = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && XyParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(XyParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new WithConfig<XyConfig>(XyConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(XyParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.XyChart)
        {
            Writable = true,
            Editing = new DiagramEditing(new XyEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, XyParser.Parse),
            Transpile = Transpiles.By<XyParser>(),
            Name = XyParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.XyChart"),
            Icon = IconRef.Fluent("data_bar_vertical"),
            DefaultBlock = """
                ```mermaid
                xychart-beta
                    title "Sales"
                    x-axis [jan, feb, mar]
                    y-axis "Units" 0 --> 10
                    bar [3, 5, 4]
                ```
                """,
        };

    /// <summary>
    /// An Ishikawa diagram — an event and the causes branching into it, a fishbone — read by a parser of its own
    /// (<see cref="IshikawaParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>ishikawa</c> is handed here, and
    /// fenced as itself it needs no header. What the engine works it over with: what the front matter asks for, hung on the block; what
    /// a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Ishikawa = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && IshikawaParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(IshikawaParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new WithConfig<IshikawaConfig>(IshikawaConfig.Read(MermaidBlock.Of(tree).Config)),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Ishikawa)
        {
            Writable = true,
            Editing = new DiagramEditing(new IshikawaEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, IshikawaParser.Parse),
            Transpile = Transpiles.By<IshikawaParser>(),
            Name = IshikawaParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Ishikawa"),
            Icon = IconRef.Fluent("food_fish"),
            DefaultBlock = """
                ```mermaid
                ishikawa-beta
                    Problem
                    Cause
                        Detail
                ```
                """,
        };

    /// <summary>
    /// A class diagram — classes and their members, the relations between them, the namespaces boxing them and the notes beside them —
    /// read by a parser of its own (<see cref="ClassParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>classDiagram</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: each namespace
    /// gathered with what is written in it; what each member draws; what styles each class; what the front matter asks for, hung on
    /// the block; a hole wherever something is still to be written while somebody writes in it; what the front matter says about
    /// folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Class = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && ClassParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(ClassParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ClassDiagramStages.ResolveNamespaces(),
            new ClassDiagramStages.ResolveMembers(),
            new ClassDiagramStages.ResolveStyles(),
            new WithConfig<ClassConfig>(ClassConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(ClassParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Class)
        {
            Writable = true,
            Editing = new DiagramEditing(new ClassEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, ClassParser.Parse),
            Transpile = Transpiles.By<ClassParser>(),
            Name = ClassParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Class"),
            Icon = IconRef.Fluent("diagram"),
            DefaultBlock = """
                ```mermaid
                classDiagram
                    class Animal {
                        +String name
                        +speak()
                    }
                ```
                """,
        };

    /// <summary>
    /// An architecture diagram — services and junctions in groups, and the edges between them — read by a parser of its own
    /// (<see cref="ArchitectureParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>architecture-beta</c> is
    /// handed here, and fenced as itself it needs no header. What the engine works it over with: whether what a line names is declared,
    /// and what it is; what the front matter asks for, hung on the block; which icon everything naming one is; a hole wherever
    /// something is still to be written while somebody writes in it; what a host binds into it; and what each run of words is made of,
    /// a title's escapes and line endings reading as what they stand for.
    /// </summary>
    public static readonly ContentLanguage Architecture = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && ArchitectureParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(ArchitectureParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolveArchitecture(),
            new WithConfig<ArchitectureConfig>(ArchitectureConfig.Read(MermaidBlock.Of(tree).Config)),
            new WithIcons(),
            show.Writing ? new WithHoles(ArchitectureParser.Holds) : null,
            .. Hosted(show),
            new WithWordPieces(ArchitectureStandIns.Decoded),
        ],
        Builder: ContentLayouts.Architecture)
        {
            Writable = true,
            Editing = new DiagramEditing(new ArchitectureEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, ArchitectureParser.Parse),
            Transpile = Transpiles.By<ArchitectureParser>(),
            Name = ArchitectureParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Architecture"),
            Icon = IconRef.Fluent("server"),
            DefaultBlock = """
                ```mermaid
                architecture-beta
                    service api(server)[API]
                    service db(database)[Database]
                    api:R -- L:db
                ```
                """,
        };

    /// <summary>
    /// A structural C4 diagram — people, systems, containers and components in their boundaries, and the relationships between them —
    /// read by a parser of its own (<see cref="C4Parser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c> or <c>C4Deployment</c> is handed here, and fenced
    /// as itself it needs no header. What the engine works it over with: which boundary each line is written inside; what each macro
    /// means — a <c>C4Dynamic</c> numbering its relationships without being asked; what the front matter asks for, hung on the block;
    /// a hole wherever something is still to be written while somebody writes in it; what the front matter says about folding; what a
    /// host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage C4 = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && C4Parser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(C4Parser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var block = MermaidBlock.Of(tree);

            return
            [
                new C4Stages.ResolveBoundaries(),
                new C4Stages.ResolveMacros(string.Equals(block.Keyword?.Text, C4Parser.Dynamic, StringComparison.OrdinalIgnoreCase)),
                new WithConfig<C4Metrics>(C4Config.Laid(block.Config)),
                show.Writing ? new WithHoles(C4Parser.Holds) : null,
                new WithFolds(),
                .. Hosted(show),
                WordPieces,
            ];
        },
        Builder: ContentLayouts.C4)
        {
            Writable = true,
            Editing = new DiagramEditing(new C4Edits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, C4Parser.Parse),
            Transpile = Transpiles.By<C4Parser>(),
            Name = C4Parser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.C4"),
            Icon = IconRef.Fluent("building"),
            DefaultBlock = """
                ```mermaid
                C4Context
                title System context
                Person(user, "User")
                System(app, "App")
                Rel(user, app, "Uses")
                ```
                """,
        };

    /// <summary>
    /// A C4 sequence — C4's macros written on a timeline, among the sequence diagram's own lines — read by a parser of its own
    /// (<see cref="C4SequenceParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>C4Sequence</c> is handed here,
    /// and fenced as itself it needs no header. What the engine works it over with: the frames the sequence diagram's lines and the
    /// boundaries open, as one stack; how its messages are numbered; what each macro means; which participants are drawn at the foot as
    /// well as the head; which icon everything naming one is; a hole wherever something is still to be written while somebody writes in
    /// it; what the front matter says about
    /// folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage C4Sequence = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && C4SequenceParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(C4SequenceParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = MermaidBlock.Of(tree).Config;

            return
            [
                new SequenceStages.ResolveFrames([C4Kinds.Boundary], [C4Kinds.Ends]),
                new SequenceStages.ResolveNumbers(SequenceConfig.Read(config).Numbered),
                new C4Stages.ResolveMacros(numbered: false),
                new C4Stages.ResolveFootBoxes(C4Config.Read(config)),
                new WithIcons(),
                show.Writing ? new WithHoles(C4SequenceParser.Holds) : null,
                new WithFolds(),
                .. Hosted(show),
                WordPieces,
            ];
        },
        Builder: ContentLayouts.C4Sequence)
        {
            Writable = true,
            Editing = new DiagramEditing(new C4SequenceEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, C4SequenceParser.Parse),
            Transpile = Transpiles.By<C4SequenceParser>(),
            Name = C4SequenceParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.C4Sequence"),
            Icon = IconRef.Fluent("building_multiple"),
            DefaultBlock = """
                ```mermaid
                C4Sequence
                title Sequence
                Person(user, "User")
                System(app, "App")
                Rel(user, app, "Uses")
                ```
                """,
        };

    /// <summary>
    /// A sequence diagram — participants along the top, the messages between their lifelines, the notes, bars, boxes and frames — read by
    /// a parser of its own (<see cref="SequenceParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>sequenceDiagram</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: which box or
    /// frame each line is in; the number <c>autonumber</c> gives each message; what the front matter asks for, hung on the block; which
    /// icon everything naming one is; a hole wherever something is still to be written while somebody writes in it; what the front
    /// matter says about folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Sequence = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && SequenceParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(SequenceParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = SequenceConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new SequenceStages.ResolveFrames(),
                new SequenceStages.ResolveNumbers(config.Numbered),
                new WithConfig<SequenceConfig>(config),
                new WithIcons(),
                show.Writing ? new WithHoles(SequenceParser.Holds) : null,
                new WithFolds(),
                .. Hosted(show),
                WordPieces,
            ];
        },
        Builder: ContentLayouts.Sequence)
        {
            Writable = true,
            Editing = new DiagramEditing(new SequenceEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, SequenceParser.Parse),
            Transpile = Transpiles.By<SequenceParser>(),
            Name = SequenceParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Sequence"),
            Icon = IconRef.Fluent("arrow_swap"),
            DefaultBlock = """
                ```mermaid
                sequenceDiagram
                    Alice->>Bob: Hello
                ```
                """,
        };

    /// <summary>
    /// A user journey — tasks scored and shared out among actors, in sections — read by a parser of its own (<see cref="JourneyParser"/>).
    /// Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>journey</c> is handed here, and fenced as itself it needs no
    /// header. What the engine works it over with: what the front matter asks for, hung on the block; a hole wherever something is still
    /// to be written while somebody writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Journey = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && JourneyParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(JourneyParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new WithConfig<JourneyConfig>(JourneyConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(JourneyParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Journey)
        {
            Writable = true,
            Editing = new DiagramEditing(new JourneyEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, JourneyParser.Parse),
            Transpile = Transpiles.By<JourneyParser>(),
            Name = JourneyParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Journey"),
            Icon = IconRef.Fluent("person_walking"),
            DefaultBlock = """
                ```mermaid
                journey
                    title My day
                    section Morning
                      Make tea: 5: Me
                ```
                """,
        };

    /// <summary>
    /// A Cynefin framework — the five sense-making domains, the items placed in each and the transitions between them — read by a
    /// parser of its own (<see cref="CynefinParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>cynefin-beta</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: where an item has
    /// no domain above it; what the front matter asks for, hung on the block; a hole wherever something is still to be written while
    /// somebody writes in it; what a host binds into it; and what each run of words is made of, a string's escapes and line endings
    /// reading as what they stand for.
    /// </summary>
    public static readonly ContentLanguage Cynefin = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && CynefinParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(CynefinParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolveDomains(),
            new WithConfig<CynefinConfig>(CynefinConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(CynefinParser.Holds) : null,
            .. Hosted(show),
            new WithWordPieces(CynefinStandIns.Decoded),
        ],
        Builder: ContentLayouts.Cynefin)
        {
            Writable = true,
            Editing = new DiagramEditing(new CynefinEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, CynefinParser.Parse),
            Transpile = Transpiles.By<CynefinParser>(),
            Name = CynefinParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Cynefin"),
            Icon = IconRef.Fluent("shape_organic"),
            DefaultBlock = """
                ```mermaid
                cynefin-beta
                    complex
                        "Run an experiment"
                ```
                """,
        };

    /// <summary>
    /// A mindmap — a root and the ideas branching off it, each in the shape its brackets say — read by a parser of its own
    /// (<see cref="MindmapParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>mindmap</c> is handed here, and
    /// fenced as itself it needs no header. What the engine works it over with: whether every node hangs off the root; what the front
    /// matter asks for, hung on the block; which icon each node names; a hole wherever something is still to be written while somebody
    /// writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Mindmap = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && MindmapParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(MindmapParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolveRoot(),
            new WithConfig<MindmapConfig>(MindmapConfig.Read(MermaidBlock.Of(tree).Config)),
            new WithIcons(),
            show.Writing ? new WithHoles(MindmapParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Mindmap)
        {
            Writable = true,
            Editing = new DiagramEditing(new MindmapEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, MindmapParser.Parse),
            Transpile = Transpiles.By<MindmapParser>(),
            Name = MindmapParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Mindmap"),
            Icon = IconRef.Fluent("text_bullet_list_tree"),
            DefaultBlock = """
                ```mermaid
                mindmap
                  root((Idea))
                    Thought
                ```
                """,
        };

    /// <summary>
    /// A kanban board — columns of cards, each card with what its metadata says — read by a parser of its own
    /// (<see cref="KanbanParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>kanban</c> is handed here, and
    /// fenced as itself it needs no header. What the engine works it over with: whether each node is a column or a card; what the front
    /// matter asks for, hung on the block; which icon each node names; a hole wherever something is still to be written while somebody
    /// writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Kanban = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && KanbanParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(KanbanParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = KanbanConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new ResolveColumns(config),
                new WithConfig<KanbanConfig>(config),
                new WithIcons(),
                show.Writing ? new WithHoles(KanbanParser.Holds) : null,
                .. Hosted(show),
                WordPieces,
            ];
        },
        Builder: ContentLayouts.Kanban)
        {
            Writable = true,
            Editing = new DiagramEditing(new KanbanEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, KanbanParser.Parse),
            Transpile = Transpiles.By<KanbanParser>(),
            Name = KanbanParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Kanban"),
            Icon = IconRef.Fluent("grid_kanban"),
            DefaultBlock = """
                ```mermaid
                kanban
                  todo[To do]
                    task1[First task]
                ```
                """,
        };

    /// <summary>
    /// A flowchart — nodes in the shapes their brackets say, the links between them and the subgraphs gathering them — read by a parser
    /// of its own (<see cref="FlowchartParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>flowchart</c> or
    /// <c>graph</c> is handed here, as is one with no header at all, and fenced as itself it needs no header. What the engine works it
    /// over with: which subgraph each line is in; what each link joins and is styled with; what each <c>id@{ … }</c> line means; what
    /// styles each node and subgraph; whether a shape named is one; what the chart amounts to as a graph; what the front matter asks for,
    /// hung on the block; which icon everything naming one is; a hole wherever something is still to be written while somebody writes in
    /// it; what the front matter says about folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Flowchart = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && FlowchartParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(FlowchartParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new FlowchartStages.ResolveSubgraphs(),
            new FlowchartStages.ResolveJoins(),
            new FlowchartStages.ResolveLinks(),
            new FlowchartStages.ResolveMetadata(),
            new FlowchartStages.ResolveStyles(),
            new FlowchartStages.ResolveShapes(),
            new FlowchartStages.ResolveChart(),
            new WithConfig<FlowchartConfig>(FlowchartConfig.Read(MermaidBlock.Of(tree).Config)),
            new WithIcons(),
            show.Writing ? new WithHoles(FlowchartParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Flowchart)
        {
            Writable = true,
            Editing = new DiagramEditing(new FlowchartEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, FlowchartParser.Parse),
            Transpile = Transpiles.By<FlowchartParser>(),
            Name = FlowchartParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Flowchart"),
            Icon = IconRef.Fluent("flowchart"),
            DefaultBlock = """
                ```mermaid
                flowchart TD
                    A[Start] --> B[Done]
                ```
                """,
        };

    /// <summary>
    /// A swimlane — a flowchart laid out in lanes, its outermost subgraphs the lanes — read by a parser of its own
    /// (<see cref="SwimlaneParser"/>) with the flowchart's pieces, since Mermaid reads the two alike. Written in Mermaid's syntax, so a
    /// <c>mermaid</c> block headed <c>swimlane-beta</c> is handed here, and fenced as itself it needs no header. What the engine works it
    /// over with: everything a flowchart is worked over with, but what the front matter asks for is a swimlane's, the chart's own inside
    /// it; and its words are written as a flowchart's are.
    /// </summary>
    public static readonly ContentLanguage Swimlane = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && SwimlaneParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(SwimlaneParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new FlowchartStages.ResolveSubgraphs(),
            new FlowchartStages.ResolveJoins(),
            new FlowchartStages.ResolveLinks(),
            new FlowchartStages.ResolveMetadata(),
            new FlowchartStages.ResolveStyles(),
            new FlowchartStages.ResolveShapes(),
            new FlowchartStages.ResolveChart(),
            new WithConfig<SwimlaneConfig>(SwimlaneConfig.Read(MermaidBlock.Of(tree).Config)),
            new WithIcons(),
            show.Writing ? new WithHoles(SwimlaneParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Swimlane)
        {
            Writable = true,
            Editing = new DiagramEditing(new FlowchartEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, SwimlaneParser.Parse),
            Transpile = Transpiles.By<SwimlaneParser>(),
            Name = SwimlaneParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Swimlane"),
            Icon = IconRef.Fluent("layout_column_three"),
            DefaultBlock = """
                ```mermaid
                swimlane-beta
                    subgraph team[Team]
                        start([Start])
                        done[Done]
                    end
                    start --> done
                ```
                """,
        };

    /// <summary>
    /// An entity relationship diagram — entities and their attributes, how many of each the other has at either end of a relationship,
    /// and the subgraphs boxing them — read by a parser of its own (<see cref="ErParser"/>). Written in Mermaid's syntax, so a
    /// <c>mermaid</c> block headed <c>erDiagram</c> is handed here, and fenced as itself it needs no header. What the engine works it
    /// over with: each subgraph gathered with what is written in it; which ends of a relationship name a subgraph; what styles each
    /// entity; what the front matter asks for, hung on the block; a hole wherever something is still to be written while somebody
    /// writes in it; what the front matter says about folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Er = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && ErParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(ErParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ErStages.ResolveGroups(),
            new ErStages.ResolveJoins(),
            new ErStages.ResolveStyles(),
            new WithConfig<ErConfig>(ErConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(ErParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Er)
        {
            Writable = true,
            Editing = new DiagramEditing(new ErEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, ErParser.Parse),
            Transpile = Transpiles.By<ErParser>(),
            Name = ErParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Er"),
            Icon = IconRef.Fluent("database"),
            DefaultBlock = """
                ```mermaid
                erDiagram
                    CUSTOMER ||--o{ ORDER : places
                ```
                """,
        };

    /// <summary>
    /// A block diagram — the grid its author laid out, the blocks in it, the composites nesting a grid of their own and the links between
    /// blocks — read by a parser of its own (<see cref="BlockParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>block-beta</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: each composite
    /// gathered with what is written in it; whether a link has blocks to join; what styles each block; what the front matter asks for,
    /// hung on the block; a hole wherever something is still to be written while somebody writes in it; what a host binds into it; and
    /// what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Block = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && BlockParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(BlockParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new BlockStages.ResolveBlocks(),
            new BlockStages.ResolveLinks(),
            new BlockStages.ResolveStyles(),
            new WithConfig<BlockConfig>(BlockConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(BlockParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Block)
        {
            Writable = true,
            Editing = new DiagramEditing(new BlockEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, BlockParser.Parse),
            Transpile = Transpiles.By<BlockParser>(),
            Name = BlockParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Block"),
            Icon = IconRef.Fluent("square_multiple"),
            DefaultBlock = """
                ```mermaid
                block-beta
                  columns 2
                  A B
                ```
                """,
        };

    /// <summary>
    /// A state diagram — states and the transitions between them, composite states holding their own, forks, choices and the notes
    /// beside them — read by a parser of its own (<see cref="StateParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block
    /// headed <c>stateDiagram</c> or <c>stateDiagram-v2</c> is handed here, and fenced as itself it needs no header. What the engine works
    /// it over with: each composite state gathered with what is written in it; the dot each <c>[*]</c> is and which names are a
    /// composite's; what styles each state; what the front matter asks for, hung on the block; a hole wherever something is still to be
    /// written while somebody writes in it; what the front matter says about folding; what a host binds into it; and what each run of
    /// words is made of.
    /// </summary>
    public static readonly ContentLanguage State = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && StateParser.HeaderWords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(StateParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new StateStages.ResolveComposites(),
            new StateStages.ResolveStates(),
            new StateStages.ResolveStyles(),
            new WithConfig<StateConfig>(StateConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(StateParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.State)
        {
            Writable = true,
            Editing = new DiagramEditing(new StateEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, StateParser.Parse),
            Transpile = Transpiles.By<StateParser>(),
            Name = StateParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.State"),
            Icon = IconRef.Fluent("flowchart_circle"),
            DefaultBlock = """
                ```mermaid
                stateDiagram-v2
                    [*] --> Idle
                    Idle --> [*]
                ```
                """,
        };

    /// <summary>
    /// A requirement diagram — requirements and the elements meeting them, each with its fields, and the SysML relations between them —
    /// read by a parser of its own (<see cref="RequirementParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>requirementDiagram</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: what styles
    /// each requirement; what the front matter asks for, hung on the block; a hole wherever something is still to be written while
    /// somebody writes in it; what the front matter says about folding; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Requirement = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && RequirementParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(RequirementParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new RequirementStages.ResolveStyles(),
            new WithConfig<RequirementConfig>(RequirementConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(RequirementParser.Holds) : null,
            new WithFolds(),
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Requirement)
        {
            Writable = true,
            Editing = new DiagramEditing(new RequirementEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, RequirementParser.Parse),
            Transpile = Transpiles.By<RequirementParser>(),
            Name = RequirementParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Requirement"),
            Icon = IconRef.Fluent("clipboard_task_list_ltr"),
            DefaultBlock = """
                ```mermaid
                requirementDiagram
                    requirement first {
                        id: 1
                        text: The first requirement.
                        risk: low
                        verifymethod: test
                    }
                ```
                """,
        };

    /// <summary>
    /// A gantt chart — tasks in sections, each when it starts and how long it runs, under the settings that say how dates are read and
    /// drawn — read by a parser of its own (<see cref="GanttParser"/>). Written in Mermaid's syntax, so a <c>mermaid</c> block headed
    /// <c>gantt</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: whether each date is one
    /// and each id names a task; when each task runs, on the chart and the day it is read; a hole wherever something is still to be
    /// written while somebody writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Gantt = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && GanttParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(GanttParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new GanttStages.ResolveSchedule(),
            new GanttStages.ResolveTasks(GanttConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(GanttParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Gantt)
        {
            Writable = true,
            Editing = new DiagramEditing(new GanttEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, GanttParser.Parse),
            Transpile = Transpiles.By<GanttParser>(),
            Name = GanttParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Gantt"),
            Icon = IconRef.Fluent("gantt_chart"),
            DefaultBlock = """
                ```mermaid
                gantt
                    title Plan
                    dateFormat YYYY-MM-DD
                    section Work
                        First task :a1, 2026-01-05, 5d
                ```
                """,
        };

    /// <summary>
    /// A git graph — commits, branches, checkouts, merges and cherry-picks — read by a parser of its own (<see cref="GitParser"/>).
    /// Written in Mermaid's syntax, so a <c>mermaid</c> block headed <c>gitGraph</c> is handed here, and fenced as itself it needs no
    /// header. What the engine works it over with: what the history means together — the branch each commit is on, what it follows, where
    /// it stands and each branch's lane — and what is not there to check out, merge or pick; a hole wherever something is still to be
    /// written while somebody writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage GitGraph = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && GitParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(GitParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new GitStages.ResolveGraph(GitConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(GitParser.Holds) : null,
            .. Hosted(show),
            new WithWordPieces(GitStandIns.Decoded),
        ],
        Builder: ContentLayouts.GitGraph)
        {
            Writable = true,
            Editing = new DiagramEditing(new GitEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, GitParser.Parse),
            Transpile = Transpiles.By<GitParser>(),
            Name = GitParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.GitGraph"),
            Icon = IconRef.Fluent("branch_fork"),
            DefaultBlock = """
                ```mermaid
                gitGraph
                    commit
                    branch feature
                    commit
                ```
                """,
        };

    /// <summary>
    /// An event model — the frames of a system's flow along time, each a ui, a processor, a command, a read model or an event in the
    /// swimlane its type belongs to, with what feeds each, the data it carries, and notes and specifications under it — read by a parser
    /// of its own (<see cref="EventModelingParser"/>) and drawn as Mermaid draws one. Written in Mermaid's syntax, so a <c>mermaid</c>
    /// block headed <c>eventmodeling</c> is handed here, and fenced as itself it needs no header. What the engine works it over with:
    /// the lanes the frames fall in and what feeds each, by Mermaid's rules; a hole wherever a name or data is still to be written while
    /// somebody writes in it; and what a host binds into it.
    /// </summary>
    public static readonly ContentLanguage EventModeling = new(
        Reads: static word => string.Equals(word?.Trim(), EventModelingParser.Language, StringComparison.Ordinal),
        Parser: static () => static source => ContentParse.Of(EventModelingParser.Parse(source)),
        Stages: static (_, show) =>
        [
            new ResolveTimeline(),
            show.Writing ? new WithHoles(EventModelingParser.Holds) : null,
            show.Inputs.Data is { } data ? new WithBindings(data) : null,
        ],
        Builder: ContentLayouts.EventModeling)
        {
            Writable = true,
            Editing = EventModelingEdits.Instance,
            Transpile = Transpiles.By<EventModelingParser>(),
            Name = EventModelingParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.EventModeling"),
            Icon = IconRef.Fluent("board_split"),
            DefaultBlock = """
                ```mermaid
                eventmodeling
                tf 01 ui Cart
                tf 02 cmd AddItem
                tf 03 evt ItemAdded
                tf 04 rmo CartItems
                ```
                """,
        };

    /// <summary>
    /// A timeline — periods along a spine, each with its events, grouped into sections — read by a parser of its own
    /// (<see cref="TimelineParser"/>) and drawn across the page or down it. Written in Mermaid's syntax, so a <c>mermaid</c> block
    /// headed <c>timeline</c> is handed here, and fenced as itself it needs no header. What the engine works it over with: where a
    /// line of events has no period above it; what the front matter asks for, hung on the block; a hole wherever something is still
    /// to be written while somebody writes in it; what a host binds into it; and what each run of words is made of.
    /// </summary>
    public static readonly ContentLanguage Timeline = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && TimelineParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(TimelineParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolveEvents(),
            new WithConfig<TimelineConfig>(TimelineConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(TimelineParser.Holds) : null,
            .. Hosted(show),
            WordPieces,
        ],
        Builder: ContentLayouts.Timeline)
        {
            Writable = true,
            Editing = new DiagramEditing(new TimelineEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, TimelineParser.Parse),
            Transpile = Transpiles.By<TimelineParser>(),
            Name = TimelineParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Timeline"),
            Icon = IconRef.Fluent("timeline"),
            DefaultBlock = """
                ```mermaid
                timeline
                    title History
                    2026 : Started
                ```
                """,
        };

    /// <summary>
    /// A treemap — items nested in sections, each drawn as big as its share — read by a parser of its own (<see cref="TreemapParser"/>).
    /// Written Mermaid's way, so a <c>mermaid</c> block headed <c>treemap-beta</c> is handed here, and fenced as itself it needs no
    /// header.
    /// </summary>
    public static readonly ContentLanguage Treemap = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && TreemapParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase)),
        Parser: static () => static source => ContentParse.Of(TreemapParser.Parse(source)),
        Stages: static (tree, show) =>
        [
            new ResolveItems(),
            new WithConfig<TreemapConfig>(TreemapConfig.Read(MermaidBlock.Of(tree).Config)),
            show.Writing ? new WithHoles(TreemapParser.Holds) : null,
            .. Hosted(show),
            new WithWordPieces(TreemapStandIns.Decoded),
        ],
        Builder: ContentLayouts.Treemap)
        {
            Writable = true,
            Editing = new DiagramEditing(new TreemapEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, TreemapParser.Parse),
            Transpile = Transpiles.By<TreemapParser>(),
            Name = TreemapParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.Treemap"),
            Icon = IconRef.Fluent("data_treemap"),
            DefaultBlock = """
                ```treemap
                "Products"
                    "Phones": 50
                    "Computers": 30
                "Services"
                    "Support": 20
                ```
                """,
        };

    /// <summary>
    /// A tree view — a folder tree, or any outline drawn as one — read by a parser of its own (<see cref="TreeViewParser"/>). Written
    /// Mermaid's way, so a <c>mermaid</c> block headed <c>treeView-beta</c> is handed here, and fenced as itself it needs no header.
    /// </summary>
    public static readonly ContentLanguage TreeView = new(
        Reads: static word => word?.Trim() is { Length: > 0 } said
                              && (said.Equals(TreeViewParser.Language, StringComparison.OrdinalIgnoreCase)
                                  || TreeViewParser.Keywords.Any(keyword => keyword.Equals(said, StringComparison.OrdinalIgnoreCase))),
        Parser: static () => static source => ContentParse.Of(TreeViewParser.Parse(source)),
        Stages: static (tree, show) =>
        {
            var config = TreeViewConfig.Read(MermaidBlock.Of(tree).Config);

            return
            [
                new ResolveNodes(),

                // Which icon each icon a node names is — a bare name in the pack the front matter makes its default.
                new WithIcons(config.DefaultIconPack),
                new WithConfig<TreeViewConfig>(config),
                show.Writing ? new WithHoles(TreeViewParser.Holds) : null,
                .. Hosted(show),
            ];
        },
        Builder: ContentLayouts.TreeView)
        {
            Writable = true,
            Editing = new DiagramEditing(new TreeViewEdits()),
            Bind = static (block, supplied) => MermaidParser.Bind(block, supplied, TreeViewParser.Parse),
            Transpile = Transpiles.By<TreeViewParser>(),
            Name = TreeViewParser.Language,
            DisplayName = static () => Str.Get("Markdown.Language.TreeView"),
            Icon = IconRef.Fluent("text_bullet_list_tree"),
            DefaultBlock = """
                ```treeview
                my-project/
                    src/
                        index.ts
                    package.json
                ```
                """,
        };

    /// <summary>What a host puts between reading a diagram and drawing it: what its words are bound against, and the pictures it names.</summary>
    private static IEnumerable<IAstStage?> Hosted(ContentShowing show) =>
    [
        show.Inputs.Data is { } data ? new WithBindings(data) : null,
        new WithDiagramPictures(show.Inputs.Pictures),
    ];

    /// <summary>A QR symbol — <see href="https://markdown.org/tools/diagrams/qr/"/>.</summary>
    public static readonly ContentLanguage Qr = Symbol("qr",
        static word => "qr".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        new EncodeQr()) with
        {
            Name = "qr",
                        DisplayName = static () => Str.Get("Markdown.Language.Qr"),
            Icon = IconRef.Fluent("qr_code"),
            DefaultBlock = """
                ```qr
                type: text
                text: Hello
                ```
                """,
        };

    /// <summary>An Aztec symbol.</summary>
    public static readonly ContentLanguage Aztec = Symbol("aztec",
        static word => word?.Trim().ToLowerInvariant() is "aztec" or "aztec-code",
        new EncodeAztec()) with
        {
            Name = "aztec",
                        DisplayName = static () => Str.Get("Markdown.Language.Aztec"),
            Icon = IconRef.Fluent("scan_dash"),
            DefaultBlock = """
                ```aztec
                type: text
                text: Hello
                ```
                """,
        };

    /// <summary>A Data Matrix symbol.</summary>
    public static readonly ContentLanguage DataMatrix = Symbol("datamatrix",
        static word => word?.Trim().ToLowerInvariant() is "datamatrix" or "data-matrix",
        new EncodeDataMatrix()) with
        {
            Name = "datamatrix",
                        DisplayName = static () => Str.Get("Markdown.Language.DataMatrix"),
            Icon = IconRef.Fluent("scan_type"),
            DefaultBlock = """
                ```datamatrix
                type: text
                text: Hello
                ```
                """,
        };

    /// <summary>A PDF417 symbol.</summary>
    public static readonly ContentLanguage Pdf417 = Symbol("pdf417",
        static word => "pdf417".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        new EncodePdf417()) with
        {
            Name = "pdf417",
                        DisplayName = static () => Str.Get("Markdown.Language.Pdf417"),
            Icon = IconRef.Fluent("scan_table"),
            DefaultBlock = """
                ```pdf417
                type: text
                text: Hello
                ```
                """,
        };

    /// <summary>A 2D code, <paramref name="named"/>: read as fields, encoded by its own stage, and drawn as the symbol that comes to.</summary>
    private static ContentLanguage Symbol(string named, Func<string?, bool> reads, IAstStage encode) =>
        new(reads, () => source => ContentParse.Of(MatrixParser.Parse(source, named)), (_, _) => [encode], ContentLayouts.Matrix);

    /// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
    public static readonly ContentLanguage Barcode = new(
        Reads: static word => "barcode".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(BarcodeParser.Parse(source)),
        Stages: static (_, show) => show.Writing ? [new HoldValue(), new EncodeBarcode(writing: true)] : [new EncodeBarcode(writing: false)],
        Builder: ContentLayouts.Barcode)
        {
            Writable = true,
            Name = "barcode",
                        DisplayName = static () => Str.Get("Markdown.Language.Barcode"),
            Icon = IconRef.Fluent("barcode_scanner"),
            DefaultBlock = """
                ```barcode
                format: CODE128
                value: HELLO-128
                ```
                """,
        };

    /// <summary>A chemical structure written as SMILES.</summary>
    public static readonly ContentLanguage Smiles = new(
        Reads: static word => "smiles".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(SmilesParser.Parse(source)),
        Stages: static (_, _) => SmilesPipeline.Of().Stages,
        Builder: ContentLayouts.Smiles)
            {
                Name = "smiles",
                                DisplayName = static () => Str.Get("Markdown.Language.Smiles"),
                Icon = IconRef.Fluent("molecule"),
                DefaultBlock = """
                    ```smiles
                    CCO "Ethanol"
                    ```
                    """,
            };

    /// <summary>A formula, written in LaTeX — on a line of its own, or in the middle of a sentence.</summary>
    public static readonly ContentLanguage Latex = new(
        Reads: static word => word?.Trim().ToLowerInvariant() is "latex" or "math" or "tex",
        Parser: static () => static source => ContentParse.Of(TexParser.Parse(source)),
        Stages: static (tree, show) => TexPipeline.Of(Editing(show.Own(tree.Width)), holes: show.Writing).Stages,
        Builder: ContentLayouts.Latex)
        {
            Writable = true,
            Editing = new EditedBy(LatexEdits.Instance),
            Transpile = Transpiles.By<TexParser>(),
            Name = "latex",
                        DisplayName = static () => Str.Get("Markdown.Language.Latex"),
            Icon = IconRef.Fluent("math_formula"),
            DefaultBlock = """
                ```latex
                E = mc^2
                ```
                """,
        };

    /// <summary>A tune written in ABC.</summary>
    public static readonly ContentLanguage Abc = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.Abc,
        Parser: static () => static source => ContentParse.Of(AbcParser.Parse(source)),
        Stages: static (tree, show) => AbcPipeline.Of(Editing(show.Own(tree.Width))).Stages,
        Builder: ContentLayouts.Abc)
            {
                Writable = true,
                Editing = AbcEdits.Instance,
                Transpile = Transpiles.By<AbcParser>(),
                Name = "abc",
                                DisplayName = static () => Str.Get("Markdown.Language.Abc"),
                Icon = IconRef.Fluent("music_note_1"),
                DefaultBlock = """
                    ```abc
                    X:1
                    T:Tune
                    M:4/4
                    L:1/4
                    K:C
                    C D E F | G A B c |]
                    ```
                    """,
            };

    /// <summary>A tune written in LilyPond.</summary>
    public static readonly ContentLanguage LilyPond = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.LilyPond,
        Parser: static () => static source => ContentParse.Of(LilyPondParser.Parse(source)),
        Stages: static (tree, show) => LilyPondPipeline.Of(Editing(show.Own(tree.Width))).Stages,
        Builder: ContentLayouts.LilyPond)
            {
                Name = "lilypond",
                                DisplayName = static () => Str.Get("Markdown.Language.LilyPond"),
                Icon = IconRef.Fluent("music_note_2"),
                DefaultBlock = """
                    ```lilypond
                    \relative c' { c4 d e f | g1 }
                    ```
                    """,
            };

    /// <summary>The stretch being written in, as a pipeline that shows it as typed is told it — or null where there is none to show.</summary>
    private static (int Start, int Length)? Editing(RawZone? zone) =>
        zone is { Length: > 0 } shown ? (shown.Start, shown.Length) : null;

    /// <summary>A table of values against a pair of axes, drawn the way its fence names.</summary>
    public static ContentLanguage Plot(PlotFence fence)
    {
        var (shown, icon, block) = Starting(fence);

        return new(
            Reads: word => PlotFences.Named(word ?? string.Empty) == fence,
            Parser: () => source => ContentParse.Of(PlotParser.Parse(source, fence.ToString().ToLowerInvariant())),
            Stages: (_, _) => PlotPipeline.Of(fence).Stages,
            Builder: ContentLayouts.Plot)
            {
                Name = shown is null ? null : "plot." + fence.ToString().ToLowerInvariant(),
                DisplayName = shown,
                Icon = icon,
                DefaultBlock = block,
            };
    }

    /// <summary>What a reader calls a plot drawn the way <paramref name="fence"/> names, what it is drawn as, and a block of one to start from.</summary>
    private static (Func<string>? Shown, IconRef Icon, string? Block) Starting(PlotFence fence) => fence switch
    {
        PlotFence.Scatter => (static () => Str.Get("Markdown.Language.Scatter"), IconRef.Fluent("data_scatter"), """
            ```scatter
            x  y
            1  2
            2  4
            3  5
            ```
            """),
        PlotFence.Bubble => (static () => Str.Get("Markdown.Language.Bubble"), IconRef.Fluent("bubble_multiple"), """
            ```bubble
            x  y  size
            1  2  10
            2  4  25
            3  3  15
            ```
            """),
        PlotFence.Heatmap => (static () => Str.Get("Markdown.Language.Heatmap"), IconRef.Fluent("grid"), """
            ```heatmap
            month  year  count
            Jan    2025  12
            Feb    2025  28
            Jan    2026  19
            Feb    2026  36
            ```
            """),
        PlotFence.Density2d => (static () => Str.Get("Markdown.Language.Density2d"), IconRef.Fluent("data_area"), """
            ```density2d
            x     y
            -1.2  -0.9
            -0.8  -1.4
            -0.3  -0.2
            0.1   0.4
            0.4   -0.1
            0.9   1.1
            1.3   0.7
            1.6   1.5
            ```
            """),
        _ => (null, default, null),
    };

    /// <summary>A cloud of words sized by how often each is said.</summary>
    public static readonly ContentLanguage WordCloud = new(
        Reads: static word => "wordcloud".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(WordCloudParser.Parse(source)),
        Stages: static (_, show) => [new WordCloud.Stages.WithPictures(show.Inputs.Pictures), new ResolveCloud(), new ResolveWords()],
        Builder: ContentLayouts.WordCloud)
            {
                Name = "wordcloud",
                                DisplayName = static () => Str.Get("Markdown.Language.WordCloud"),
                Icon = IconRef.Fluent("cloud_words"),
                DefaultBlock = """
                    ```wordcloud
                    markdown: 10
                    diagram: 6
                    words: 4
                    ```
                    """,
            };

    /// <summary>What code shows and offers, in any grammar.</summary>
    private static readonly IContentLanguage CodeEdits = new CodeEditing();

    /// <summary>Code in a language no grammar reads: the same drawing, with nothing named — what a fence nothing reads is shown as.</summary>
    public static readonly ContentLanguage Code = Coded(null);

    /// <summary>
    /// Code in every grammar there is, each a language of its own, answering to the words that name it. Drawn at once as written,
    /// and coloured once its grammar has read it, which is never on the way to drawing.
    /// </summary>
    public static readonly IReadOnlyList<ContentLanguage> Codes = [.. TreeSitterLanguages.Grammars.Select(grammar => Coded(grammar))];

    /// <summary>What plain text answers when written in: the keys of everything written line by line.</summary>
    private static readonly IContentLanguage TextEdits = new TextEditing();

    /// <summary>
    /// Plain text: its lines, the words in them, and nothing they draw — what a page's source is shown as beside the page
    /// (<see cref="ContentLanguages.AsTyped"/>), and what a fence calling itself text holds, which a page sets in the box it shows what is
    /// written as written in, its lines wrapped to fit inside it. Read a stretch at a time, cut into lines and words for the stretches being
    /// laid, and set as typed.
    /// </summary>
    public static readonly ContentLanguage Text = new(
        Reads: word => word is not null && TextNames.Contains(word.Trim()),
        Parser: () => source => ContentParse.Of(TextParser.Parse(source)),
        Stages: static (_, show) =>
        [
            new TextIntoLines(),

            // Last, because a line is only the same as it was when what it is cut into is too.
            show.Unchanged,
        ],
        Builder: ContentLayouts.Text)
    {
        Editing = TextEdits,
        BoxedAsWritten = true,
        WrapsLines = true,
        Windowed = TextParser.Windowed,
        ReadsFromLineStarts = true,
        LineHeight = TextSetting.LineHeight,
        UnitHeight = TextSetting.HeightOf,
    };

    /// <summary>The words a fence of plain text is written under.</summary>
    private static readonly HashSet<string> TextNames = new([TextParser.Language, "txt", "plain", "plaintext"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Code in <paramref name="grammar"/>, or in none: read as written first, and by the grammar second (<see cref="CodeSpans"/>) — set, where
    /// a page holds it, in the box the page shows what is written as written in, its lines kept whole.
    /// </summary>
    private static ContentLanguage Coded(string? grammar) => new(
        Reads: word => grammar is not null && string.Equals(CodeGrammars.For(word), grammar, StringComparison.OrdinalIgnoreCase),
        Parser: () => source => ContentParse.Of(CodeParser.Parse(source, grammar ?? string.Empty)),
        Stages: static (_, show) =>
        [
            show.Syntax is { Syntax: CodeSyntax syntax } read ? new CodeHighlights(syntax, read.At) : null,
            new CodeLines(),

            // Last, because a line is only the same as it was when what it is coloured as is too.
            show.Unchanged,
        ],
        Builder: ContentLayouts.Code)
        {
        Editing = CodeEdits,
            BoxedAsWritten = true,
            Windowed = () => CodeParser.Windowed(grammar ?? string.Empty),
            ReadsFromLineStarts = true,
            LineHeight = CodeSetting.LineHeight,
            UnitHeight = CodeSetting.HeightOf,
            Syntax = grammar is null ? null : characters => characters.Length > CodeSyntax.ColoursUpTo ? null : CodeSyntax.Of(grammar, characters),
        };
}
