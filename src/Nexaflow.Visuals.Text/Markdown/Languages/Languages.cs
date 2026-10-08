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
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Markdown.Plot;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using Nexaflow.Markdown.WordCloud.Stages;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Icons;
using Nexaflow.Markdown.Editing;

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
            new WithImages(show.Inputs.Pictures),
            new WithLinks(show.Inputs.Links),

            // Last of what is read, because a block is only the same as it was when everything worked out about it is too.
            show.Unchanged,

            // After it: a block shown as written is not what was read, and is never kept as if it were.
            show.Shown is { } zone ? new ShowBlocksAsWritten(zone, show.At, show.Reads) : null,
        ],
        Builder: typeof(MarkdownBuilder))
        {
            Writable = true,
            Editing = MarkdownEdits.Instance,
        };

    /// <summary>
    /// <c>mermaid</c>: a block whose header names the diagram it is, held whole as written in that diagram's own language — which
    /// is what is drawn, nothing of this one being (<see cref="MermaidFenceParser"/>). A header naming no diagram is shown as
    /// written, the keyword marked.
    /// </summary>
    public static readonly ContentLanguage Mermaid = new(
        Reads: static word => "mermaid".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(MermaidFenceParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: typeof(MermaidFenceBuilder))
        {
            // Whoever writes in the block writes in the diagram, which is laid out as this is — and whose own handler is told the edit.
            Writable = true,
        };

    /// <summary>
    /// Every diagram, each a language of its own, answering to the words its header is written with. They read their lines with the
    /// same parser and are worked over by the stages the header names — which is sharing, not being one language: each is drawn by
    /// its own builder, and says what an edit means through its own handler.
    /// </summary>
    public static readonly IReadOnlyList<ContentLanguage> Diagrams =
    [
        .. Enum.GetValues<MermaidDiagram>()
               .Where(diagram => MermaidBuilders.For(diagram) is not null)
               .Select(diagram => Diagram(diagram, MermaidBuilders.For(diagram)!)),
    ];

    /// <summary><paramref name="diagram"/>, drawn by <paramref name="builder"/>.</summary>
    private static ContentLanguage Diagram(MermaidDiagram diagram, Type builder)
    {
        var (name, icon, block) = Starting(diagram);

        // Which parser reads this diagram, and which writes back into it, are MermaidDiagrams' to say and nobody else's.
        var reads = MermaidDiagrams.ParserFor(diagram);

        return new(
            Reads: word => !string.IsNullOrWhiteSpace(word) && MermaidDiagrams.Named(word.Trim()) == diagram,
            Parser: () => source => ContentParse.Of(reads(source)),
            Stages: static (tree, show) => [.. MermaidPipeline.Of(tree, show.Writing), .. Hosted(show), WordPieces],
            Builder: builder)
            {
                Writable = true,
                Editing = new DiagramEditing(DiagramEdits.For(diagram)),
                Bind = MermaidParser.Bind,
                Transpile = MermaidDiagrams.TranspilerFor(diagram),
                DisplayName = name,
                Icon = icon,
                DefaultBlock = block,
            };
    }

    /// <summary>What a reader calls <paramref name="diagram"/>, what it is drawn as, and a block of it to start from.</summary>
    private static (string? Name, IconRef Icon, string? Block) Starting(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Flowchart => ("Flowchart", IconRef.Fluent("flowchart"), """
            ```mermaid
            flowchart TD
                A[Start] --> B[Done]
            ```
            """),
        MermaidDiagram.Pie => ("Pie chart", IconRef.Fluent("data_pie"), """
            ```mermaid
            pie
                title Pets
                "Dogs" : 40
            ```
            """),
        MermaidDiagram.Quadrant => ("Quadrant chart", IconRef.Fluent("sub_grid"), """
            ```mermaid
            quadrantChart
                title Priorities
                x-axis Low effort --> High effort
                y-axis Low value --> High value
                Idea: [0.3, 0.7]
            ```
            """),
        MermaidDiagram.Sequence => ("Sequence diagram", IconRef.Fluent("arrow_swap"), """
            ```mermaid
            sequenceDiagram
                Alice->>Bob: Hello
            ```
            """),
        MermaidDiagram.Gantt => ("Gantt chart", IconRef.Fluent("gantt_chart"), """
            ```mermaid
            gantt
                title Plan
                dateFormat YYYY-MM-DD
                section Work
                    First task :a1, 2026-01-05, 5d
            ```
            """),
        MermaidDiagram.GitGraph => ("Git graph", IconRef.Fluent("branch_fork"), """
            ```mermaid
            gitGraph
                commit
                branch feature
                commit
            ```
            """),
        MermaidDiagram.Mindmap => ("Mind map", IconRef.Fluent("text_bullet_list_tree"), """
            ```mermaid
            mindmap
              root((Idea))
                Thought
            ```
            """),
        MermaidDiagram.State => ("State diagram", IconRef.Fluent("flowchart_circle"), """
            ```mermaid
            stateDiagram-v2
                [*] --> Idle
                Idle --> [*]
            ```
            """),
        MermaidDiagram.Class => ("Class diagram", IconRef.Fluent("diagram"), """
            ```mermaid
            classDiagram
                class Animal {
                    +String name
                    +speak()
                }
            ```
            """),
        MermaidDiagram.Requirement => ("Requirement diagram", IconRef.Fluent("clipboard_task_list_ltr"), """
            ```mermaid
            requirementDiagram
                requirement first {
                    id: 1
                    text: The first requirement.
                    risk: low
                    verifymethod: test
                }
            ```
            """),
        MermaidDiagram.Kanban => ("Kanban board", IconRef.Fluent("grid_kanban"), """
            ```mermaid
            kanban
              todo[To do]
                task1[First task]
            ```
            """),
        MermaidDiagram.XyChart => ("XY chart", IconRef.Fluent("data_bar_vertical"), """
            ```mermaid
            xychart-beta
                title "Sales"
                x-axis [jan, feb, mar]
                y-axis "Units" 0 --> 10
                bar [3, 5, 4]
            ```
            """),
        MermaidDiagram.Radar => ("Radar chart", IconRef.Fluent("radar"), """
            ```mermaid
            radar-beta
                axis a["Speed"], b["Power"], c["Range"]
                curve x["Model X"]{3, 4, 2}
            ```
            """),
        MermaidDiagram.Ishikawa => ("Ishikawa diagram", IconRef.Fluent("food_fish"), """
            ```mermaid
            ishikawa-beta
                Problem
                Cause
                    Detail
            ```
            """),
        MermaidDiagram.Sankey => ("Sankey diagram", IconRef.Fluent("flow"), """
            ```mermaid
            sankey

            Source,Target,10
            ```
            """),
        MermaidDiagram.Er => ("Entity relationship diagram", IconRef.Fluent("database"), """
            ```mermaid
            erDiagram
                CUSTOMER ||--o{ ORDER : places
            ```
            """),
        MermaidDiagram.Venn => ("Venn diagram", IconRef.Fluent("shape_intersect"), """
            ```mermaid
            venn-beta
                set A
                set B
                union A,B["Both"]
            ```
            """),
        MermaidDiagram.Cynefin => ("Cynefin framework", IconRef.Fluent("shape_organic"), """
            ```mermaid
            cynefin-beta
                complex
                    "Run an experiment"
            ```
            """),
        MermaidDiagram.Architecture => ("Architecture diagram", IconRef.Fluent("server"), """
            ```mermaid
            architecture-beta
                service api(server)[API]
                service db(database)[Database]
                api:R -- L:db
            ```
            """),
        MermaidDiagram.Swimlane => ("Swimlane diagram", IconRef.Fluent("layout_column_three"), """
            ```mermaid
            swimlane-beta
                subgraph team[Team]
                    start([Start])
                    done[Done]
                end
                start --> done
            ```
            """),
        MermaidDiagram.Timeline => ("Timeline", IconRef.Fluent("timeline"), """
            ```mermaid
            timeline
                title History
                2026 : Started
            ```
            """),
        MermaidDiagram.Journey => ("User journey", IconRef.Fluent("person_walking"), """
            ```mermaid
            journey
                title My day
                section Morning
                  Make tea: 5: Me
            ```
            """),
        MermaidDiagram.Block => ("Block diagram", IconRef.Fluent("square_multiple"), """
            ```mermaid
            block-beta
              columns 2
              A B
            ```
            """),
        MermaidDiagram.C4 => ("C4 diagram", IconRef.Fluent("building"), """
            ```mermaid
            C4Context
            title System context
            Person(user, "User")
            System(app, "App")
            Rel(user, app, "Uses")
            ```
            """),
        MermaidDiagram.C4Sequence => ("C4 sequence", IconRef.Fluent("building_multiple"), """
            ```mermaid
            C4Sequence
            title Sequence
            Person(user, "User")
            System(app, "App")
            Rel(user, app, "Uses")
            ```
            """),
        _ => (null, default, null),
    };

    /// <summary>
    /// UML class notation written its own way — <see href="https://www.nomnoml.com/"/> — read by a parser of its own and drawn
    /// as the class diagram it describes. Nothing in it is worked out over the whole block: every line says what it means.
    /// </summary>
    public static readonly ContentLanguage Nomnoml = new(
        Reads: static word => "nomnoml".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(NomnomlParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: typeof(NomnomlBuilder))
            {
                DisplayName = "UML (nomnoml)",
                Icon = IconRef.Fluent("draw_shape"),
                DefaultBlock = """
                    ```nomnoml
                    [Order] -> [Customer]
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
            DisplayName = "QR code",
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
            DisplayName = "Aztec code",
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
            DisplayName = "Data Matrix",
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
            DisplayName = "PDF417",
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
        new(reads, () => source => ContentParse.Of(MatrixParser.Parse(source, named)), (_, _) => [encode], typeof(MatrixBuilder));

    /// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
    public static readonly ContentLanguage Barcode = new(
        Reads: static word => "barcode".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(BarcodeParser.Parse(source)),
        Stages: static (_, show) => show.Writing ? [new HoldValue(), new EncodeBarcode(writing: true)] : [new EncodeBarcode(writing: false)],
        Builder: typeof(BarcodeBuilder))
        {
            Writable = true,
            DisplayName = "Barcode",
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
        Builder: typeof(SmilesBuilder))
            {
                DisplayName = "Chemical structure",
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
        Builder: typeof(LatexBuilder))
        {
            Writable = true,
            Editing = new EditedBy(LatexEdits.Instance),
            DisplayName = "Formula",
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
        Builder: typeof(AbcBuilder))
            {
                Writable = true,
                Editing = AbcEdits.Instance,
                Transpile = Transpiles.By<AbcParser>(),
                DisplayName = "Tune (ABC)",
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
        Builder: typeof(LilyPondBuilder))
            {
                DisplayName = "Score (LilyPond)",
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
        var (name, icon, block) = Starting(fence);

        return new(
            Reads: word => PlotFences.Named(word ?? string.Empty) == fence,
            Parser: () => source => ContentParse.Of(PlotParser.Parse(source, fence.ToString().ToLowerInvariant())),
            Stages: (_, _) => PlotPipeline.Of(fence).Stages,
            Builder: typeof(PlotBuilder))
            {
                DisplayName = name,
                Icon = icon,
                DefaultBlock = block,
            };
    }

    /// <summary>What a reader calls a plot drawn the way <paramref name="fence"/> names, what it is drawn as, and a block of one to start from.</summary>
    private static (string? Name, IconRef Icon, string? Block) Starting(PlotFence fence) => fence switch
    {
        PlotFence.Scatter => ("Scatter plot", IconRef.Fluent("data_scatter"), """
            ```scatter
            x  y
            1  2
            2  4
            3  5
            ```
            """),
        PlotFence.Bubble => ("Bubble chart", IconRef.Fluent("bubble_multiple"), """
            ```bubble
            x  y  size
            1  2  10
            2  4  25
            3  3  15
            ```
            """),
        PlotFence.Heatmap => ("Heat map", IconRef.Fluent("grid"), """
            ```heatmap
            month  year  count
            Jan    2025  12
            Feb    2025  28
            Jan    2026  19
            Feb    2026  36
            ```
            """),
        PlotFence.Density2d => ("Density plot", IconRef.Fluent("data_area"), """
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
        Builder: typeof(WordCloudBuilder))
            {
                DisplayName = "Word cloud",
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

    /// <summary>Code in <paramref name="grammar"/>, or in none: read as written first, and by the grammar second (<see cref="CodeSpans"/>).</summary>
    private static ContentLanguage Coded(string? grammar) => new(
        Reads: word => grammar is not null && string.Equals(CodeGrammars.For(word), grammar, StringComparison.OrdinalIgnoreCase),
        Parser: () => source => ContentParse.Of(CodeParser.Parse(source, grammar ?? string.Empty)),
        Stages: static (_, _) => [new CodeLines()],
        Builder: typeof(CodeBuilder))
        {
            Editing = CodeEdits,
            SlowParser = grammar is null ? null : source => ContentParse.Of(CodeParser.Parse(source, grammar, CodeSpans.Read(grammar, source))),
        };
}

/// <summary>A language whose edits are told to <paramref name="onEdit"/> — or, where that is null, one with nothing of its own to say about an edit.</summary>
internal sealed class EditedBy(IOnEdit? onEdit) : IContentLanguage
{
    public IOnEdit? OnEdit => onEdit;

    /// <summary>And its moves too, where the same handler says anything about one.</summary>
    public IOnMove? OnMove => onEdit as IOnMove;
}

/// <summary>What code shows and offers: every character a writer typed, and no picture of it.</summary>
internal sealed class CodeEditing : IContentLanguage
{
    /// <summary>Colouring code is still showing it: every character a writer typed is on the page.</summary>
    public bool ShowsWhatWasWritten => true;

    /// <summary>
    /// A picture of code is a worse copy of the code — it cannot be searched, pasted or read by anything — so that is the one
    /// usual button this does not offer.
    /// </summary>
    public BlockCorner Corner(ContentAsk ask) => new(Saves: false);
}
