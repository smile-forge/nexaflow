using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Smoke tests for the WPF quadrant-chart and sequence-diagram renderers — they must
/// produce a real element without throwing on the UI thread.  Renderer exceptions are
/// asserted here directly because <see cref="DiagramRenderer"/> swallows them into an
/// error border.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class DiagramRendererTests
{
    private const string QuadrantSrc =
        """
        quadrantChart
            title Reach and engagement of campaigns
            x-axis Low Reach --> High Reach
            y-axis Low Engagement --> High Engagement
            quadrant-1 We should expand
            quadrant-2 Need to promote
            quadrant-3 Re-evaluate
            quadrant-4 May be improved
            Campaign A: [0.3, 0.6]
            Campaign B: [0.45, 0.23]
            Campaign C: [0.57, 0.69]
        """;

    private const string SequenceSrc =
        """
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
            Alice-)John: See you later!
            Alice->>Alice: thinking
        """;

    [TestMethod]
    [CoversNode("quadrant-graph")]
    public void Quadrant_RendersBorder() => UiThread.Run(() =>
    {
        var chart = new MermaidQuadrantParser().Parse(QuadrantSrc);
        var fe    = WpfQuadrantChartRenderer.Render(chart, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
    });

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_RendersBorder() => UiThread.Run(() =>
    {
        var diagram = new MermaidSequenceParser().Parse(SequenceSrc);
        var fe      = WpfSequenceDiagramRenderer.Render(diagram, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
    });

    [TestMethod]
    [CoversNode("quadrant-graph")]
    public void Quadrant_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", QuadrantSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", SequenceSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var diagram = new MermaidSequenceParser().Parse("sequenceDiagram\n");
        var fe      = WpfSequenceDiagramRenderer.Render(diagram, MarkdownPalette.Dark);
        Assert.IsNotNull(fe);
    });

    [TestMethod]
    public void Frontmatter_PieRoutesToChartNotSourceText() => UiThread.Run(() =>
    {
        // A config front-matter block used to defeat routing → the diagram rendered as raw text.
        const string src = "---\nconfig:\n  pie:\n    textPosition: 0.5\n---\npie title T\n  \"A\" : 1\n  \"B\" : 2\n";
        var fe = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(Canvas));   // pie chart, not the source-text fallback
    });

    // ── State diagram ──────────────────────────────────────────────────────

    private const string StateSrc =
        """
        stateDiagram-v2
            [*] --> First
            state First {
                [*] --> second
                second --> [*]
            }
            First --> Choice
            state Choice <<choice>>
            Choice --> Done: ok
            Done --> [*]
            note right of Done
                All finished
            end note
        """;

    /// <summary>
    /// Asserts the source reached the graph renderer and came back drawn, rather than falling
    /// through to the raw-text fallback. Deliberately not pinned to the exact chrome: which of a
    /// scroller and a pan/zoom viewport wraps the canvas depends on how big the diagram turned out,
    /// and that is not what these tests are about.
    /// </summary>
    private static void AssertGraphDiagram(FrameworkElement fe, string what)
    {
        Assert.IsInstanceOfType(fe, typeof(GraphDiagramView), $"{what} should route to the graph renderer");
        fe.Measure(new Size(900, 900));
        fe.Arrange(new Rect(0, 0, 900, 900));
        Assert.IsNotNull(FindCanvas(fe), $"{what} should have drawn a canvas");
    }

    private static Canvas? FindCanvas(DependencyObject root)
    {
        if (root is Canvas c && c.Children.Count > 0) return c;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindCanvas(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } hit) return hit;
        return null;
    }

    [TestMethod]
    public void State_RendersGraphNotSourceText() => UiThread.Run(() =>
        AssertGraphDiagram(DiagramRenderer.Render("mermaid", StateSrc, MarkdownPalette.Dark), "a state diagram"));

    [TestMethod]
    public void State_ConcurrencyAndForks_RenderWithoutThrowing() => UiThread.Run(() =>
    {
        const string src =
            """
            stateDiagram-v2
                state fork_state <<fork>>
                [*] --> fork_state
                fork_state --> A
                fork_state --> B
                state join_state <<join>>
                A --> join_state
                B --> join_state
                join_state --> [*]
            """;
        AssertGraphDiagram(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark), "forks and joins");
    });

    // ── Class diagram ──────────────────────────────────────────────────────

    private const string ClassSrc =
        """
        classDiagram
            class Animal {
                +int age
                +isMammal() bool
            }
            class Duck {
                +String beakColor
                +quack()
            }
            Animal <|-- Duck
            Customer "1" --> "*" Ticket : owns
            class Shape {
                <<interface>>
                +draw()
            }
            namespace Geometry {
                class Circle
                class Square
            }
        """;

    [TestMethod]
    public void Class_RendersGraphNotSourceText() => UiThread.Run(() =>
        AssertGraphDiagram(DiagramRenderer.Render("mermaid", ClassSrc, MarkdownPalette.Dark), "a class diagram"));

    [TestMethod]
    public void Class_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var graph  = new MermaidClassParser().Parse("classDiagram\n");
        var layout = Nexaflow.Visuals.Text.Markdown.Graphs.Layout.SugiyamaLayout.Compute(graph);
        Assert.IsNotNull(WpfGraphRenderer.Render(layout, MarkdownPalette.Dark));
    });

    // ── Requirement diagram ────────────────────────────────────────────────

    private const string RequirementSrc =
        """
        requirementDiagram
            requirement test_req {
                id: 1
                text: the test text.
                risk: high
                verifymethod: test
            }
            element test_entity {
                type: simulation
            }
            test_entity - satisfies -> test_req
        """;

    [TestMethod]
    public void Requirement_RendersGraphNotSourceText() => UiThread.Run(() =>
        AssertGraphDiagram(DiagramRenderer.Render("mermaid", RequirementSrc, MarkdownPalette.Dark),
                           "a requirement diagram"));

    // ── Kanban board ───────────────────────────────────────────────────────

    private const string KanbanSrc =
        """
        kanban
          Todo
            [Create Documentation]
            docs[Create Blog]@{ ticket: MC-2038, assigned: 'knsv', priority: 'High' }
          id11[Done]
            id5[define getData]
        """;

    [TestMethod]
    public void Kanban_RendersBoardNotSourceText() => UiThread.Run(() =>
    {
        // A kanban board routes to the kanban renderer: Border → ScrollViewer → (column panels).
        var fe = DiagramRenderer.Render("mermaid", KanbanSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "kanban should route to the kanban renderer (ScrollViewer content)");
        Assert.IsInstanceOfType(sv!.Content, typeof(StackPanel));
    });

    [TestMethod]
    public void Kanban_EmptyBoard_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var board = new MermaidKanbanParser().Parse("kanban\n");
        Assert.IsNotNull(WpfKanbanRenderer.Render(board, MarkdownPalette.Dark));
    });

    // ── XY chart ──────────────────────────────────────────────────────────

    private const string XySrc =
        """
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar  "actual" [5000, 6000, 7500, 8200]
            line "trend"  [5200, 6100, 7400, 8000]
        """;

    [TestMethod]
    public void XyChart_RendersBorder() => UiThread.Run(() =>
    {
        var chart = new MermaidXyChartParser().Parse(XySrc);
        var fe    = WpfXyChartRenderer.Render(chart, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
    });

    [TestMethod]
    public void XyChart_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", XySrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void XyChart_HorizontalRenders() => UiThread.Run(() =>
    {
        var chart = new MermaidXyChartParser().Parse(
            "xychart horizontal\n  x-axis [a, b, c]\n  y-axis 0 --> 10\n  bar [3, 7, 5]\n  line [2, 6, 4]\n");
        Assert.IsInstanceOfType(WpfXyChartRenderer.Render(chart, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void XyChart_WithFrontMatterConfig_RendersThroughDiagramRenderer() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              xyChart:
                showDataLabel: true
              themeVariables:
                xyChart:
                  plotColorPalette: '#000000, #0000FF'
            ---
            xychart
              x-axis [comedy, romance, mystery]
              y-axis "Number of Books" 0 --> 30
              bar [12, 2, 20]
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void XyChart_PerPointLabels_RenderWithoutThrowing() => UiThread.Run(() =>
    {
        var chart = new MermaidXyChartParser().Parse(
            "xychart\n  line [540 \"PaLM\", 65 \"LLaMA-65B\", 7 \"Mistral 7B\"]\n");
        Assert.IsInstanceOfType(WpfXyChartRenderer.Render(chart, MarkdownPalette.Dark), typeof(Border));
    });

    // ── Radar ─────────────────────────────────────────────────────────────

    private const string RadarSrc =
        """
        radar-beta
          title Restaurant Comparison
          axis food["Food Quality"], service["Service"], price["Price"]
          axis ambiance["Ambiance"]
          curve a["Restaurant A"]{4, 3, 2, 4}
          curve b["Restaurant B"]{3, 4, 3, 3}
          graticule polygon
          max 5
        """;

    [TestMethod]
    public void Radar_RendersBorder() => UiThread.Run(() =>
    {
        var chart = new MermaidRadarParser().Parse(RadarSrc);
        Assert.IsInstanceOfType(WpfRadarRenderer.Render(chart, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void Radar_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", RadarSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Radar_CircleGraticuleAndKeyedCurve_Render() => UiThread.Run(() =>
    {
        var chart = new MermaidRadarParser().Parse(
            "radar-beta\n  axis a, b, c\n  curve x{ c: 3, a: 1, b: 2 }\n  ticks 4\n");
        Assert.IsInstanceOfType(WpfRadarRenderer.Render(chart, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void Radar_WithFrontMatterConfig_RendersThroughDiagramRenderer() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              radar:
                axisScaleFactor: 0.5
                curveTension: 0.1
              themeVariables:
                cScale0: "#FF0000"
                cScale1: "#00FF00"
                radar:
                  curveOpacity: 0.4
            ---
            radar-beta
              axis A, B, C, D, E
              curve c1{1,2,3,4,5}
              curve c2{5,4,3,2,1}
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Radar_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var chart = new MermaidRadarParser().Parse("radar-beta\n");
        Assert.IsNotNull(WpfRadarRenderer.Render(chart, MarkdownPalette.Dark));
    });

    // ── Ishikawa (fishbone) ───────────────────────────────────────────────

    private const string IshikawaSrc =
        """
        ishikawa-beta
            Blurry Photo
            Process
                Out of focus
                Shutter speed too slow
            Equipment
                LENS
                    Inappropriate lens
                    Dirty lens
            Environment
                Too dark
        """;

    [TestMethod]
    public void Ishikawa_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidIshikawaParser().Parse(IshikawaSrc);
        Assert.IsInstanceOfType(WpfIshikawaRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void Ishikawa_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", IshikawaSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Ishikawa_WithFrontMatterConfig_RendersThroughDiagramRenderer() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              ishikawa:
                diagramPadding: 30
            ---
            ishikawa-beta
              Slow API Response
              Infrastructure
                No CDN
              Code
                N+1 queries
                Missing caching
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Ishikawa_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidIshikawaParser().Parse("ishikawa-beta\n");
        Assert.IsNotNull(WpfIshikawaRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Sankey ────────────────────────────────────────────────────────────

    private const string SankeySrc =
        """
        sankey

        Coal,Electricity,75
        Gas,Electricity,40
        Electricity,Industry,60
        Electricity,Homes,55
        """;

    [TestMethod]
    public void Sankey_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidSankeyParser().Parse(SankeySrc);
        Assert.IsInstanceOfType(WpfSankeyRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void Sankey_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", SankeySrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Sankey_WithFrontMatterConfig_RendersThroughDiagramRenderer() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              sankey:
                showValues: true
                linkColor: gradient
                nodeAlignment: left
                suffix: " TWh"
                nodeColors:
                  Electricity: "#4e79a7"
            ---
            sankey

            Coal,Electricity,75
            Gas,Electricity,40
            Electricity,Industry,60
            Electricity,Homes,55
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Sankey_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidSankeyParser().Parse("sankey\n");
        Assert.IsNotNull(WpfSankeyRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── ER diagram ────────────────────────────────────────────────────────

    private const string ErSrc =
        """
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER {
                string name
                string custNumber
            }
            ORDER ||--|{ LINE-ITEM : contains
            CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
        """;

    [TestMethod]
    public void Er_RoutesToGraphRenderer() => UiThread.Run(() =>
        // ER reuses the graph renderer, and is no longer raw source text.
        AssertGraphDiagram(DiagramRenderer.Render("mermaid", ErSrc, MarkdownPalette.Dark), "an erDiagram"));

    [TestMethod]
    public void Er_WordCardinalityAndConfig_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              er:
                layoutDirection: LR
                fill: honeydew
            ---
            erDiagram
                CAR 1 to zero or more NAMED-DRIVER : allows
                PERSON many(0) optionally to 0+ NAMED-DRIVER : is
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Er_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", "erDiagram\n", MarkdownPalette.Dark));
    });

    // ── Venn diagram ──────────────────────────────────────────────────────

    private const string VennSrc =
        """
        venn-beta
          title "Team overlap"
          set A["Frontend"]
            text A1["React"]
          set B["Backend"]
          union A,B["Shared"]
            text AB1["OpenAPI"]
        """;

    [TestMethod]
    public void Venn_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidVennParser().Parse(VennSrc);
        Assert.IsInstanceOfType(WpfVennRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    public void Venn_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", VennSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Venn_ThreeSetWithConfig_Renders() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              venn:
                width: 600
              themeVariables:
                venn1: "#FF0000"
                venn2: "#00FF00"
                venn3: "#0000FF"
            ---
            venn-beta
              set Desirable
              set Feasible
              set Viable
              union Desirable,Feasible,Viable["Innovation"]
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Venn_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidVennParser().Parse("venn-beta\n");
        Assert.IsNotNull(WpfVennRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Architecture diagram ──────────────────────────────────────────────

    private const string ArchitectureSrc =
        """
        architecture-beta
            group api(cloud)[API]
            service db(database)[Database] in api
            service server(server)[Server] in api
            db:R -- L:server
        """;

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidArchitectureParser().Parse(ArchitectureSrc);
        Assert.IsInstanceOfType(WpfArchitectureRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_DispatchesToGridRendererNotRawText() => UiThread.Run(() =>
    {
        // architecture-beta used to fall through to raw source text; it must now route to the grid
        // renderer: Border → ScrollViewer → Canvas (the raw fallback is Border → TextBlock).
        var fe = DiagramRenderer.Render("mermaid", ArchitectureSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "architecture-beta should route to the architecture renderer");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_GroupsIconsAndCrossGroupEdge_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            architecture-beta
                group public(cloud)[Public]
                group private(cloud)[Private]
                service gateway(internet)[Gateway] in public
                service app(server)[App] in private
                junction j1 in private
                gateway:R --> L:app
                app:B -- T:j1
                gateway{group}:B --> T:app{group}
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidArchitectureParser().Parse("architecture-beta\n");
        Assert.IsNotNull(WpfArchitectureRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Swimlane diagram ──────────────────────────────────────────────────

    private const string SwimlaneSrc =
        """
        swimlane-beta
            subgraph customer[Customer]
                start([Place order])
                pay[Pay]
            end
            subgraph fulfilment[Fulfilment]
                pick{In stock?}
                ship[Ship order]
            end
            start --> pay
            pay --> pick
            pick -->|Yes| ship
            pick -.->|No| pay
        """;

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_RendersBorder() => UiThread.Run(() =>
    {
        var g = new MermaidSwimlaneParser().Parse(SwimlaneSrc);
        Assert.IsInstanceOfType(WpfSwimlaneRenderer.Render(g, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_DispatchesToLaneRendererNotRawText() => UiThread.Run(() =>
    {
        var fe = DiagramRenderer.Render("mermaid", SwimlaneSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "swimlane-beta should route to the swimlane renderer");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_HorizontalDirection_Renders() => UiThread.Run(() =>
    {
        const string src =
            """
            swimlane-beta LR
                subgraph dev[Developer]
                    code[Write code]
                end
                subgraph ci[CI]
                    build[Build]
                    test{Tests pass?}
                end
                code ==> build
                build --> test
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var g = new MermaidSwimlaneParser().Parse("swimlane-beta\n");
        Assert.IsNotNull(WpfSwimlaneRenderer.Render(g, MarkdownPalette.Dark));
    });

    // ── Cynefin diagram ───────────────────────────────────────────────────

    private const string CynefinSrc =
        """
        cynefin-beta
            title Making sense
            complex
                "Investigate root cause"
            complicated
                "Consult an expert"
            clear
                "Apply the runbook"
            chaotic
                "Stop the bleeding"
            confusion
                "Incident A"
                "Incident B"
                "Incident C"
                "Incident D"
            chaotic --> complex : "Stabilised"
        """;

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidCynefinParser().Parse(CynefinSrc);
        Assert.IsInstanceOfType(WpfCynefinRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_DispatchesToDomainRendererNotRawText() => UiThread.Run(() =>
    {
        // cynefin-beta used to fall through to raw source text; it must now route to the domain
        // renderer: Border → Canvas (the raw fallback is Border → TextBlock).
        var fe = DiagramRenderer.Render("mermaid", CynefinSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(Canvas));
    });

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_ConfusionOverflowAndConfig_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              cynefin:
                showDomainDescriptions: true
              themeVariables:
                cynefin:
                  complexBg: "#4e79a7"
            ---
            cynefin-beta
                confusion
                    "One"
                    "Two"
                    "Three"
                    "Four"
                    "Five"
            """;
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark));
    });

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidCynefinParser().Parse("cynefin-beta\n");
        Assert.IsNotNull(WpfCynefinRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Timeline ──────────────────────────────────────────────────────────

    private const string TimelineSrc =
        """
        timeline
            title History of Social Media Platform
            2002 : LinkedIn
            2004 : Facebook
                 : Google
            2005 : YouTube
            2006 : Twitter
        """;

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidTimelineParser().Parse(TimelineSrc);
        Assert.IsInstanceOfType(WpfTimelineRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_DispatchesToTimelineRendererNotRawText() => UiThread.Run(() =>
    {
        var fe = DiagramRenderer.Render("mermaid", TimelineSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        // The raw-source fallback wraps a TextBlock; the timeline renderer wraps a scrolling canvas.
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_SectionsDisableMulticolorAndFrontmatterTitle_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            title: Ages of industry
            config:
              timeline:
                disableMulticolor: true
              themeVariables:
                cScale0: "#4e79a7"
                cScaleLabel0: "#ffffff"
            ---
            timeline
                section Stone Age
                    7000 BC : Stone tools
                section Bronze Age
                    2000 BC : Bronze tools<br>Wheel
            """;
        var fe = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Light);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_TopDownDirection_Renders() => UiThread.Run(() =>
    {
        var d = new MermaidTimelineParser().Parse("timeline\n  direction TD\n  title Down\n  section S\n  2020 : a : b\n  2021\n");
        Assert.IsInstanceOfType(WpfTimelineRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidTimelineParser().Parse("timeline\n");
        Assert.IsNotNull(WpfTimelineRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Journey ───────────────────────────────────────────────────────────

    private const string JourneySrc =
        """
        journey
            title My working day
            section Go to work
              Make tea: 5: Me
              Go upstairs: 3: Me
              Do work: 1: Me, Cat
            section Go home
              Go downstairs: 5: Me
              Sit down: 5: Me
        """;

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidJourneyParser().Parse(JourneySrc);
        Assert.IsInstanceOfType(WpfJourneyRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_DispatchesToJourneyRendererNotRawText() => UiThread.Run(() =>
    {
        var fe = DiagramRenderer.Render("mermaid", JourneySrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_AllFiveScoresAndConfig_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            config:
              journey:
                width: 120
                actorColours: ["#e15759", "#4e79a7"]
                sectionFills: ["#f28e2b"]
            ---
            journey
                title Every face
                Before: 3
                section Scores
                  One: 1: Me
                  Two: 2: Me, Cat
                  Three: 3: Cat
                  Four: 4: Me
                  Five: 5: Me, Cat, Dog
            """;
        var fe = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Light);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidJourneyParser().Parse("journey\n");
        Assert.IsNotNull(WpfJourneyRenderer.Render(d, MarkdownPalette.Dark));
    });

    // ── Block diagram ─────────────────────────────────────────────────────

    private const string BlockSrc =
        """
        block-beta
          columns 3
          Frontend blockArrowId6<[" "]>(right) Backend
          space:2 down<[" "]>(down)
          Disk left<[" "]>(left) Database[("Database")]

          classDef front fill:#696,stroke:#333;
          classDef back fill:#969,stroke:#333;
          class Frontend front
          class Backend,Database back
        """;

    [TestMethod]
    [CoversNode("block")]
    public void Block_RendersBorder() => UiThread.Run(() =>
    {
        var d = new MermaidBlockParser().Parse(BlockSrc);
        Assert.IsInstanceOfType(WpfBlockRenderer.Render(d, MarkdownPalette.Dark), typeof(Border));
    });

    [TestMethod]
    [CoversNode("block")]
    public void Block_DispatchesToBlockRendererNotRawText() => UiThread.Run(() =>
    {
        var fe = DiagramRenderer.Render("mermaid", BlockSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        // The raw-source fallback wraps a TextBlock; the block renderer wraps a scrolling canvas.
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("block")]
    public void Block_NestedGroupsEdgesAndEveryShape_Render() => UiThread.Run(() =>
    {
        const string src =
            """
            ---
            title: Everything at once
            config:
              block:
                padding: 12
            ---
            block-beta
              columns 4
              db(("DB")) blockArrowId6<["&nbsp;"]>(down) both<["x"]>(x) updown<["y"]>(y)
              block:ID:2
                A
                B["A wide one in the middle"]
                C
              end
              space D
              b("round") c(["stadium"]) d[["subroutine"]] e[("cylinder")]
              g>"flag"] h{"rhombus"} i{{"hexagon"}} n((("double circle")))
              j[/"parallelogram"/] k[\"alt"\] l[/"trapezoid"\] m[\"alt"/]
              ID --> D
              C -- "label" --> D
              A --- b
              style B fill:#969,stroke:#333,stroke-width:4px,color:#fff,stroke-dasharray: 5 5
            """;
        var fe = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Light);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
    });

    [TestMethod]
    [CoversNode("block")]
    public void Block_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        var d = new MermaidBlockParser().Parse("block-beta\n");
        Assert.IsNotNull(WpfBlockRenderer.Render(d, MarkdownPalette.Dark));
    });
}
