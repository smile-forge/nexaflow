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
    public void Class_RendersOnTheSharedTreeNotSourceText() => UiThread.Run(() =>
    {
        var content = DiagramRenderer.Render("mermaid", ClassSrc, MarkdownPalette.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

        Assert.IsNotNull(content, "a class diagram is drawn on the shared layout tree");
        content!.Measure(new Size(900, double.PositiveInfinity));

        Assert.IsTrue(content.Laid.Tree.Count > 0, "and it drew something");
        Assert.AreEqual(0, content.Diagnostics.Count, "with nothing wrong in it");
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
    public void Requirement_RendersOnTheSharedTreeNotSourceText() => UiThread.Run(() =>
    {
        var content = DiagramRenderer.Render("mermaid", RequirementSrc, MarkdownPalette.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

        Assert.IsNotNull(content, "a requirement diagram is drawn on the shared layout tree");
        content!.Measure(new Size(900, double.PositiveInfinity));

        Assert.IsTrue(content.Laid.Tree.Count > 0, "and it drew something");
        Assert.AreEqual(0, content.Diagnostics.Count, "with nothing wrong in it");
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
    public void Er_RendersOnTheSharedTreeNotSourceText() => UiThread.Run(() =>
    {
        var content = DiagramRenderer.Render("mermaid", ErSrc, MarkdownPalette.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

        Assert.IsNotNull(content, "an ER diagram is drawn on the shared layout tree");
        content!.Measure(new Size(900, double.PositiveInfinity));

        Assert.IsTrue(content.Laid.Tree.Count > 0, "and it drew something");
        Assert.AreEqual(0, content.Diagnostics.Count, "with nothing wrong in it");
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

    // ── Architecture diagram ──────────────────────────────────────────────

    private const string ArchitectureSrc =
        """
        architecture-beta
            group api(cloud)[API]
            service db(database)[Database] in api
            service server(server)[Server] in api
            db:R -- L:server
        """;



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
}
