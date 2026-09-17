using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Smoke tests for the legacy WPF diagram renderers — they must
/// produce a real element without throwing on the UI thread.  Renderer exceptions are
/// asserted here directly because <see cref="DiagramRenderer"/> swallows them into an
/// error border.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class DiagramRendererTests
{
    private const string SequenceSrc =
        """
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
            Alice-)John: See you later!
            Alice->>Alice: thinking
        """;

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_RendersBorder() => UiThread.Run(() =>
    {
        var diagram = new MermaidSequenceParser().Parse(SequenceSrc);
        var fe      = WpfSequenceDiagramRenderer.Render(diagram, MarkdownPalette.Dark);
        Assert.IsInstanceOfType(fe, typeof(Border));
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
        var content = DiagramRenderer.Render("mermaid", src, MarkdownPalette.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

        Assert.IsNotNull(content, "a pie is drawn on the shared layout tree");
        content!.Measure(new Size(700, double.PositiveInfinity));

        Assert.AreEqual(2, content.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge),
                        "a wedge each, not the source-text fallback");
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
