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
}
