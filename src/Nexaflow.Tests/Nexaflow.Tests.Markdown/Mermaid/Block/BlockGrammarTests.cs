using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Block;

/// <summary>
/// What a <c>block-beta</c> block is read into: the grid its author laid out — its columns, its blocks and the cells left
/// empty — the composites nested in it, the links between the blocks, and the styling that colours them.
/// </summary>
[TestClass]
[CoversNode("block-ast")]
public class BlockGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Intro =
        """
        block-beta
        columns 1
          db(("DB"))
          blockArrowId6<["&nbsp;&nbsp;&nbsp;"]>(down)
          block:ID
            A
            B["A wide one in the middle"]
            C
          end
          space
          D
          ID --> D
          C --> D
          style B fill:#969,stroke:#333,stroke-width:4px
        """;

    /// <summary>The documentation's every shape, each in its own brackets.</summary>
    public const string Shapes =
        """
        block-beta
          columns 4
          id1["Square"] id2("Round") id3(["Stadium"]) id4[["Subroutine"]]
          id5[("Database")] id6(("Circle")) id7>"Asymmetric"] id8{"Rhombus"}
          id9{{"Hexagon"}} id10[/"Lean right"/] id11[\"Lean left"\] id12[/"Christmas"\]
          id13[\"Go shopping"/] id14((("Double circle")))
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Block;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Shapes,
        "block\n  a b c",
        "block\n  columns 3\n  a b c d",
        "block\n  columns 3\n  a[\"A label\"] b:2 c:2 d",
        "block\n    block\n      D\n    end\n    A[\"A: I am a wide one\"]",
        "block\n  columns 3\n  a:3\n  block:group1:2\n    columns 2\n    h i j k\n  end\n  g\n  block:group2:3\n    %% columns auto (default)\n    l m n o p q r\n  end",
        "block\n  block\n    columns 1\n    a[\"A label\"] b c d\n  end",
        "block\n  blockArrowId<[\"Label\"]>(right)\n  blockArrowId2<[\"Label\"]>(left)\n  blockArrowId3<[\"Label\"]>(up)\n"
        + "  blockArrowId4<[\"Label\"]>(down)\n  blockArrowId5<[\"Label\"]>(x)\n  blockArrowId6<[\"Label\"]>(y)\n  blockArrowId7<[\"Label\"]>(x, down)",
        "block\n  columns 3\n  a space b\n  c   d   e",
        "block\n  ida space:3 idb idc",
        "block\n  A space B\n  A-->B",
        "block\n  A space:2 B\n  A-- \"X\" -->B",
        "block\n  id1 space id2\n  id1(\"Start\")-->id2(\"Stop\")\n  style id1 fill:#636,stroke:#333,stroke-width:4px\n"
        + "  style id2 fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5",
        "block\n  A space B\n  A-->B\n  classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px;\n  class A blue\n"
        + "  style B fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5",
        "block\n  columns 3\n  Frontend blockArrowId6<[\" \"]>(right) Backend\n  space:2 down<[\" \"]>(down)\n"
        + "  Disk left<[\" \"]>(left) Database[(\"Database\")]\n\n  classDef front fill:#696,stroke:#333;\n  classDef back fill:#969,stroke:#333;\n"
        + "  class Frontend front\n  class Backend,Database back",
        "block\n  columns 3\n  Start((\"Start\")) space:2\n  down<[\" \"]>(down) space:2\n"
        + "  Decision{{\"Make Decision\"}} right<[\"Yes\"]>(right) Process1[\"Process A\"]\n"
        + "  downAgain<[\"No\"]>(down) space r3<[\"Done\"]>(down)\n  Process2[\"Process B\"] r2<[\"Done\"]>(right) End((\"End\"))\n\n"
        + "  style Start fill:#969;\n  style End fill:#696;",
        "block\n  A\n  style A fill:#969,stroke:#333;",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a front matter and a title", "---\ntitle: Where it runs\nconfig:\n  block:\n    padding: 12\n---\nblock-beta\n  a b"),
        ("the word title, which is a block like any other", "block-beta\n  title Where it runs"),
        ("every link a block draws", "block-beta\n  a b c d e f g h\n  a --- b\n  c --> d\n  e ==> f\n  g -.-> h"),
        ("links with heads at both ends", "block-beta\n  a b c d e f\n  a <--> b\n  c --x d\n  e o--o f"),
        ("a labelled link of every thickness", "block-beta\n  a b c d e f\n  a -- \"one\" --> b\n  c == \"two\" ==> d\n  e -. \"three\" .-> f"),
        ("a link drawn as nothing", "block-beta\n  a b\n  a ~~~ b"),
        ("a long link", "block-beta\n  a b\n  a ----> b"),
        ("no space round a link", "block-beta\n  a b\n  a-->b"),
        ("a block arrow with no label", "block-beta\n  onwards<[\"\"]>(right)"),
        ("a composite with a label and a width", "block-beta\n  columns 2\n  block:outer[\"Outer\"]:2\n    inner\n  end"),
        ("composites nested in composites", "block-beta\n  block:one\n    block:two\n      block:three\n        a\n      end\n    end\n  end"),
        ("an anonymous composite beside a named one", "block-beta\n  block\n    a\n  end\n  block:named\n    b\n  end"),
        ("a block written again to say more about it", "block-beta\n  a b\n  a[\"Said later\"] --> b((\"Round later\"))"),
        ("a class given to several blocks", "block-beta\n  a b\n  classDef hot fill:#f00\n  class a,b hot"),
        ("the class every block starts from", "block-beta\n  a\n  classDef default fill:#eee"),
        ("a comment closing a line", "block-beta\n  a b %% two of them"),
        ("accessibility lines", "block-beta\n  accTitle: The parts\n  accDescr: How they sit\n  a b"),
        ("written on Windows", "block-beta\r\n  columns 2\r\n  a b  \r\n"),
        // Half written.
        ("a width still to write", "block-beta\n  a:"),
        ("a column count still to write", "block-beta\n  columns "),
        ("a label still to say anything", "block-beta\n  a[\"\"]"),
        ("a link still to say what it joins", "block-beta\n  a --> "),
        ("a labelled link still to be closed", "block-beta\n  a -- \"X\" "),
        ("a composite still to be ended", "block-beta\n  block:half\n    a"),
        ("a classDef still to say a style", "block-beta\n  a\n  classDef blue"),
        ("a class line still to name the class", "block-beta\n  a\n  classDef blue fill:#00f\n  class a"),
        ("nothing but the keyword", "block-beta"),
        // What nobody means to write.
        ("a label never closed", "block-beta\n  a[\"Unclosed"),
        ("a block arrow pointing nowhere named", "block-beta\n  a<[\"Label\"]>(sideways)"),
        ("a block arrow never closed", "block-beta\n  a<[\"Label\"]>(right"),
        ("a width that is no number", "block-beta\n  a:many"),
        ("a width of nothing", "block-beta\n  a:0"),
        ("columns of nothing", "block-beta\n  columns 0"),
        ("an end closing nothing", "block-beta\n  a\n  end"),
        ("a link with nothing on one side", "block-beta\n  a\n  --> a"),
        ("a style with no colon in it", "block-beta\n  a\n  style a fill#969;"),
        ("a style naming a block nothing lays out", "block-beta\n  a\n  style nowhere fill:#969"),
        ("a class no classDef writes", "block-beta\n  a\n  class a nowhere"),
        ("a stroke width that is no width", "block-beta\n  a\n  style a stroke-width:thick"),
        ("something no line of a block diagram is", "block-beta\n  ?!"),
        ("a header saying more than the keyword", "block-beta LR\n  a"),
    ];

    [TestMethod]
    public void EveryBracketSaysItsShape()
    {
        var items = ContentReading.Of(MermaidStaged.Read(Shapes)).Root.SelfAndDescendants()
            .Where(part => part.Kind == BlockKinds.Item)
            .ToList();

        Assert.AreEqual(14, items.Count, "the documentation shows fourteen shapes");
        Assert.AreEqual(0, items.Count(item => MermaidShapes.Of(item) == MermaidShape.None), "and every one of them says a shape");
    }

    [TestMethod]
    public void ALabelEndsAtWhicheverOfItsBracketsClosesItFirst()
    {
        Assert.AreEqual(MermaidShape.Parallelogram, Shaped("block-beta\n  a[/\"Leaning\"/]"));
        Assert.AreEqual(MermaidShape.Trapezoid, Shaped("block-beta\n  a[/\"Leaning\"\\]"));
        Assert.AreEqual(MermaidShape.ParallelogramAlt, Shaped("block-beta\n  a[\\\"Leaning\"\\]"));
        Assert.AreEqual(MermaidShape.TrapezoidAlt, Shaped("block-beta\n  a[\\\"Leaning\"/]"));
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, said) in new[]
                 {
                     ("block-beta\n  a<[\"Label\"]>(sideways)", "A block arrow points"),
                     ("block-beta\n  a:0", "whole number of columns"),
                     ("block-beta\n  columns 0", "whole number of columns"),
                     ("block-beta\n  a\n  end", "none is open here"),
                     ("block-beta\n  block:half\n    a", "never closed"),
                     ("block-beta\n  a\n  --> a", "one side of this one is empty"),
                     ("block-beta\n  a\n  style nowhere fill:#969", "No block nowhere is laid out"),
                     ("block-beta\n  a\n  class a nowhere", "No classDef nowhere is written"),
                 })
        {
            var trouble = MermaidStaged.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(trouble.Any(reason => reason.Contains(said, StringComparison.Ordinal)),
                          $"{source}\nsays {string.Join(" / ", trouble)}, and nothing about '{said}'");
        }
    }

    private static MermaidShape Shaped(string source) =>
        MermaidShapes.Of(ContentReading.Of(MermaidStaged.Read(source)).Root
            .SelfAndDescendants().First(part => part.Kind == BlockKinds.Item));
}
