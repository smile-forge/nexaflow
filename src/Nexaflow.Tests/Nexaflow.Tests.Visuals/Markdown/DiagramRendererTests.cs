using Nexaflow.Visuals.Text.Markdown;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What <see cref="DiagramRenderer"/> hands a block to, and that what comes back is a drawing rather than the
/// source-text fallback. Asserted here directly because the renderer swallows a failure into an error border, so a
/// diagram that stopped routing would otherwise look like one that simply drew nothing.
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
    public void Sequence_DrawsOnTheSharedLayoutTree() => UiThread.Run(() =>
    {
        var laid = MermaidBuilders.Lay(SequenceSrc, StyleFormat.Dark);

        Assert.IsNotNull(laid);
        Assert.IsTrue(laid!.Root.SelfAndDescendants().Any(piece => piece.Kind == SequencePiece.Lifeline));
    });

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        Assert.IsNotNull(Alone.Drawn("mermaid", SequenceSrc, StyleFormat.Dark));
    });

    [TestMethod]
    [CoversNode("c4-sequence")]
    public void C4Sequence_DrawsOnTheSharedLayoutTreeToo() => UiThread.Run(() =>
    {
        const string source = "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\", \"HTTPS\")";
        var laid = MermaidBuilders.Lay(source, StyleFormat.Dark);

        Assert.IsNotNull(laid);
        Assert.AreEqual(2, laid!.Root.SelfAndDescendants().Count(piece => piece.Kind == SequencePiece.Lifeline));
        Assert.IsNotNull(Alone.Drawn("mermaid", source, StyleFormat.Dark));
    });

    [TestMethod]
    public void Frontmatter_PieRoutesToChartNotSourceText() => UiThread.Run(() =>
    {
        // A config front-matter block used to defeat routing → the diagram rendered as raw text.
        const string src = "---\nconfig:\n  pie:\n    textPosition: 0.5\n---\npie title T\n  \"A\" : 1\n  \"B\" : 2\n";
        var content = Alone.Drawn("mermaid", src, StyleFormat.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

        Assert.IsNotNull(content, "a pie is drawn on the shared layout tree");
        content!.Measure(new Size(700, double.PositiveInfinity));

        Assert.AreEqual(2, content.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge),
                        "a wedge each, not the source-text fallback");
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
    public void Class_RendersOnTheSharedTreeNotSourceText() => UiThread.Run(() =>
    {
        var content = Alone.Drawn("mermaid", ClassSrc, StyleFormat.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

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
        var content = Alone.Drawn("mermaid", RequirementSrc, StyleFormat.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

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
        var content = Alone.Drawn("mermaid", ErSrc, StyleFormat.Dark) as Nexaflow.Visuals.Text.Editing.ContentElement;

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
        Assert.IsNotNull(Alone.Drawn("mermaid", src, StyleFormat.Dark));
    });

    [TestMethod]
    public void Er_EmptyDiagram_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        Assert.IsNotNull(Alone.Drawn("mermaid", "erDiagram\n", StyleFormat.Dark));
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
