using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

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
    public void Quadrant_RendersBorder() => UiThread.Run(() =>
    {
        var chart = new MermaidQuadrantParser().Parse(QuadrantSrc);
        var fe    = WpfQuadrantChartRenderer.Render(chart, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
    });

    [TestMethod]
    public void Sequence_RendersBorder() => UiThread.Run(() =>
    {
        var diagram = new MermaidSequenceParser().Parse(SequenceSrc);
        var fe      = WpfSequenceDiagramRenderer.Render(diagram, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
    });

    [TestMethod]
    public void Quadrant_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", QuadrantSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
    public void Sequence_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", SequenceSrc, MarkdownPalette.Dark));
    });

    [TestMethod]
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

    [TestMethod]
    public void State_RendersGraphNotSourceText() => UiThread.Run(() =>
    {
        // A state diagram routes to the graph renderer: Border → ScrollViewer → Canvas.
        var fe = DiagramRenderer.Render("mermaid", StateSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "state diagram should route to the graph renderer (ScrollViewer/Canvas)");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

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
        var fe = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        Assert.IsInstanceOfType(((Border)fe).Child, typeof(ScrollViewer));
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
    {
        // A class diagram routes to the graph renderer: Border → ScrollViewer → Canvas.
        var fe = DiagramRenderer.Render("mermaid", ClassSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "class diagram should route to the graph renderer (ScrollViewer/Canvas)");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

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
    {
        var fe = DiagramRenderer.Render("mermaid", RequirementSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "requirement diagram should route to the graph renderer (ScrollViewer/Canvas)");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

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
    {
        // ER reuses the graph renderer: Border → ScrollViewer → Canvas (and is no longer raw source text).
        var fe = DiagramRenderer.Render("mermaid", ErSrc, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
        var sv = ((Border)fe).Child as ScrollViewer;
        Assert.IsNotNull(sv, "erDiagram should route to the graph renderer (ScrollViewer/Canvas)");
        Assert.IsInstanceOfType(sv!.Content, typeof(Canvas));
    });

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
}
