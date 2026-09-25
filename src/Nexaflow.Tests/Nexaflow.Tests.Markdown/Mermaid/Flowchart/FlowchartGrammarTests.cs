using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a <c>flowchart</c> block is read into: the way it is laid out, the nodes written in it, the links between them, the
/// subgraphs they are gathered into, and the lines that style, number and point them somewhere.
/// </summary>
[TestClass]
[CoversNode("flowchart-ast")]
public class FlowchartGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with, whose labels are written one to a line.</summary>
    public const string Intro =
        """
        flowchart LR
            markdown["`This **is** _Markdown_`"]
            newLines["Line 1<br/>Line 2<br/>Line 3"]
            markdown --> newLines
        """;

    /// <summary>The documentation's every shape written in brackets.</summary>
    public const string Shapes =
        """
        flowchart TD
            id1[This is the text in the box]
            id2(This is the text in the box)
            id3([This is the text in the box])
            id4[[This is the text in the box]]
            id5[(Database)]
            id6((This is the text in the circle))
            id7>This is the text in the box]
            id8{This is the text in the box}
            id9{{This is the text in the box}}
            id10[/This is the text in the box/]
            id11[\This is the text in the box\]
            id12[/Christmas\]
            id13[\Go shopping/]
            id14(((This is the text in the circle)))
        """;

    /// <summary>Every link the documentation writes.</summary>
    public const string Links =
        """
        flowchart LR
            A-->B
            C --- D
            E---|This is the text|F
            G-->|text|H
            I-- text ---J
            K---|text|L
            M-.->N
            O-. text .-> P
            Q ==> R
            S == text ==> T
            U ~~~ V
            W --o X
            Y --x Z
            AA <--> AB
            AC o--o AD
            AE x--x AF
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Flowchart;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Shapes,
        Links,
        "flowchart TD\n    Start --> Stop",
        "flowchart LR\n    Start --> Stop",
        "graph TD\n    Start --> Stop",
        "flowchart BT\n    Start --> Stop",
        "flowchart RL\n    Start --> Stop",
        "flowchart TB\n    Start --> Stop",
        "flowchart LR\n    id1(Start)-->id2(Stop)\n    style id1 fill:#f9f,stroke:#333,stroke-width:4px\n"
        + "    style id2 fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5",
        "flowchart TD\n    A-->B\n    B-->C\n    C-->D\n    D-->A",
        "flowchart LR\n    A --> B & C --> D",
        "flowchart TB\n    A & B--> C & D",
        "flowchart TB\n    a --> b\n    a --> c\n    b --> d\n    c --> d",
        "flowchart TD\n    A[Start] --> B{Is it?}\n    B -- Yes --> C[OK]\n    C --> D[Rethink]\n    D --> B\n    B -- No ----> E[End]",
        "flowchart TB\n    c1-->a2\n    subgraph one\n    a1-->a2\n    end\n    subgraph two\n    b1-->b2\n    end\n"
        + "    subgraph three\n    c1-->c2\n    end",
        "flowchart TB\n    c1-->a2\n    subgraph ide1 [one]\n    a1-->a2\n    end",
        "flowchart TB\n    c1-->a2\n    subgraph one\n    a1-->a2\n    end\n    subgraph two\n    b1-->b2\n    end\n"
        + "    subgraph three\n    c1-->c2\n    end\n    one --> two\n    three --> two\n    two --> c2",
        "flowchart LR\n  subgraph TOP\n    direction TB\n    subgraph B1\n        direction RL\n        i1 -->f1\n    end\n"
        + "    subgraph B2\n        direction BT\n        i2 -->f2\n    end\n  end\n  A --> TOP --> B\n  B1 --> B2",
        "flowchart TD\n    A-->B\n    B-->C\n    classDef className fill:#f9f,stroke:#333,stroke-width:4px\n    class A className",
        "flowchart TD\n    A:::someclass --> B\n    classDef someclass fill:#f96",
        "flowchart TD\n    A --> B\n    classDef default fill:#f9f,stroke:#333,stroke-width:4px",
        "flowchart TD\n    A --> B\n    classDef firstClass,secondClass font-size:12pt\n    class A firstClass\n    class B secondClass",
        "flowchart LR\n    A-->B\n    B-->C\n    C-->D\n    linkStyle 3 stroke:#ff3,stroke-width:4px,color:red;",
        "flowchart LR\n    A-->B\n    B-->C\n    C-->D\n    linkStyle 0,1 color:blue;",
        "flowchart LR\n    A-->B\n    linkStyle default stroke:#333",
        "flowchart LR\n    A-->B\n    linkStyle 0 interpolate basis stroke:#ff3",
        "flowchart LR\n    A-->B\n    click A \"https://www.github.com\" _blank",
        "flowchart LR\n    A-->B\n    click A href \"https://www.github.com\" \"Open this in a new tab\" _blank",
        "flowchart LR\n    A-->B\n    click A callback \"Tooltip for a callback\"",
        "flowchart LR\n    A-->B\n    click A call callback() \"Tooltip for a callback\"",
        "flowchart TD\n    A@{ shape: rect, label: \"This is a process\" }",
        "flowchart TD\n    A@{ shape: manual-input, label: \"User input\" }\n    B@{ shape: docs, label: \"Multiple documents\" }",
        "flowchart TD\n    A e1@--> B\n    e1@{ animate: true }",
        "flowchart TD\n    A e1@--> B\n    e1@{ curve: linear }",
        "flowchart TD\n    A[\"A double quote:#quot;\"] --> B[\"A dec char:#9829;\"]",
        "flowchart LR\n    id1[\"This is the (text) in the box\"]",
        "flowchart TD\n    A[\"Text with <br/> a break\"] --> B",
        "flowchart TD\n    %% this is a comment\n    A --> B",
        "flowchart TD;\n    A-->B;\n    B-->C;",
        "flowchart LR\n    A[fa:fa-twitter Twitter]",
        "---\ntitle: The way through\nconfig:\n  flowchart:\n    curve: linear\n    nodeSpacing: 60\n---\nflowchart LR\n  A --> B",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("nothing but the keyword", "flowchart"),
        ("a header that is only the other keyword", "graph"),
        ("an id a dash carries on", "flowchart LR\n  left-hand --> right-hand"),
        ("an id a dot carries on", "flowchart LR\n  a.one --> a.two"),
        ("a link written hard against its ids", "flowchart LR\n  a-->b-->c"),
        ("a long link", "flowchart LR\n  a ----> b"),
        ("a long dotted link", "flowchart LR\n  a -..-> b"),
        ("a long thick link", "flowchart LR\n  a ====> b"),
        ("a label in quotes holding a bracket", "flowchart LR\n  a[\"A ] in it\"] --> b"),
        ("a label on a link holding a dash", "flowchart LR\n  a -- \"a-b\" --> b"),
        ("a node written again to say more about it", "flowchart LR\n  a --> b\n  a[\"Said later\"]"),
        ("a subgraph with nothing after the word", "flowchart TB\n  subgraph\n    a\n  end"),
        ("a subgraph titled in quotes", "flowchart TB\n  subgraph id [\"The title\"]\n    a\n  end"),
        ("a subgraph named in words", "flowchart TB\n  subgraph The first part\n    a1 --> a2\n  end"),
        ("subgraphs nested in subgraphs", "flowchart TB\n  subgraph one\n    subgraph two\n      a\n    end\n  end"),
        ("a class given where the node is written", "flowchart LR\n  a:::hot --> b\n  classDef hot fill:#f00"),
        ("a class given to several nodes", "flowchart LR\n  a --> b\n  classDef hot fill:#f00\n  class a,b hot"),
        ("a style on a subgraph", "flowchart TB\n  subgraph one\n    a\n  end\n  style one fill:#eee"),
        ("a comment closing a line", "flowchart LR\n  a --> b %% and back again"),
        ("accessibility lines", "flowchart LR\n  accTitle: The way through\n  accDescr: How it runs\n  a --> b"),
        ("written on Windows", "flowchart LR\r\n  a --> b  \r\n"),
        ("a node shape named by metadata", "flowchart TD\n  a@{ shape: cyl, label: \"Store\" }"),
        ("metadata saying nothing yet", "flowchart TD\n  a@{}"),
        ("an icon and its form", "flowchart TD\n  a@{ icon: \"fa:fa-heart\", form: circle, h: 48 }"),
        // Half written.
        ("a link still to say what it joins", "flowchart LR\n  a --> "),
        ("a labelled link still to be closed", "flowchart LR\n  a -- yes "),
        ("a label still to say anything", "flowchart LR\n  a[\"\"]"),
        ("a label on a link still to say anything", "flowchart LR\n  a -->|| b"),
        ("a subgraph still to be ended", "flowchart TB\n  subgraph half\n    a"),
        ("a classDef still to say a style", "flowchart LR\n  a\n  classDef blue"),
        ("a class line still to name the class", "flowchart LR\n  a\n  classDef blue fill:#00f\n  class a"),
        ("a linkStyle still to say a style", "flowchart LR\n  a --> b\n  linkStyle 0"),
        ("a click still to say where it leads", "flowchart LR\n  a\n  click a"),
        ("a direction still to be written", "flowchart TB\n  subgraph one\n    direction "),
        ("metadata never closed", "flowchart TD\n  a@{ shape: cyl"),
        // What nobody means to write.
        ("a way nothing is laid out", "flowchart sideways\n  a --> b"),
        ("a header saying more than a way", "flowchart TD extra\n  a --> b"),
        ("a direction nothing is laid out", "flowchart TB\n  subgraph one\n    direction sideways\n    a\n  end"),
        ("an end closing nothing", "flowchart LR\n  a\n  end"),
        ("a link with nothing on one side", "flowchart LR\n  a\n  --> a"),
        ("a style naming a node nothing writes", "flowchart LR\n  a\n  style nowhere fill:#969"),
        ("a class no classDef writes", "flowchart LR\n  a\n  class a nowhere"),
        ("a style with no colon in it", "flowchart LR\n  a\n  style a fill#969"),
        ("a stroke width that is no width", "flowchart LR\n  a\n  style a stroke-width:thick"),
        ("a linkStyle numbering nothing", "flowchart LR\n  a --> b\n  linkStyle many stroke:#f00"),
        ("a click opening nowhere named", "flowchart LR\n  a\n  click a \"https://example.com\" _sideways"),
        ("metadata saying what nothing reads", "flowchart TD\n  a@{ wibble: 3 }"),
        ("a label never closed", "flowchart LR\n  a[\"Unclosed"),
        ("something no line of a flowchart is", "flowchart LR\n  ?!"),
    ];

    [TestMethod]
    public void EveryBracketSaysItsShape()
    {
        var nodes = Written(Shapes);

        Assert.AreEqual(14, nodes.Count, "the documentation shows fourteen shapes written in brackets");
        Assert.AreEqual(0, nodes.Count(node => MermaidShapes.Of(node) == MermaidShape.None), "and every one of them says a shape");
    }

    [TestMethod]
    public void AnIdEndsWhereALinkStarts_AndNowhereElse()
    {
        Assert.AreEqual("left-hand", Said("flowchart LR\n  left-hand --> right"));
        Assert.AreEqual("a.one", Said("flowchart LR\n  a.one --> b"));
        Assert.AreEqual("a", Said("flowchart LR\n  a-->b"));
        Assert.AreEqual("a", Said("flowchart LR\n  a-.->b"));
        Assert.AreEqual("a", Said("flowchart LR\n  a==>b"));
        Assert.AreEqual("a", Said("flowchart LR\n  a~~~b"));
        Assert.AreEqual("a-b", Said("flowchart LR\n  a-b --> c"));
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, said) in new[]
                 {
                     ("flowchart sideways\n  a --> b", "laid out TB, TD, BT, RL or LR"),
                     ("flowchart TB\n  subgraph one\n    direction sideways\n    a\n  end", "laid out TB, TD, BT, RL or LR"),
                     ("flowchart LR\n  a\n  end", "none is open here"),
                     ("flowchart TB\n  subgraph half\n    a", "never closed"),
                     ("flowchart LR\n  a\n  --> a", "one side of this one is empty"),
                     ("flowchart LR\n  a\n  style nowhere fill:#969", "No node nowhere is written"),
                     ("flowchart LR\n  a\n  class a nowhere", "No classDef nowhere is written"),
                     ("flowchart LR\n  a\n  click a \"https://example.com\" _sideways", "_self, _blank, _parent or _top"),
                     ("flowchart TD\n  a@{ wibble: 3 }", "Metadata sets"),
                     ("flowchart TD\n  a@{ shape: wibble }", "no shape called wibble"),
                 })
        {
            var trouble = MermaidParser.Read(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(trouble.Any(reason => reason.Contains(said, StringComparison.Ordinal)),
                          $"{source}\nsays {string.Join(" / ", trouble)}, and nothing about '{said}'");
        }
    }

    private static List<ContentPart> Written(string source) =>
        [.. ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants()
              .Where(part => part.Kind == FlowchartKinds.Node)];

    /// <summary>What the first node of a block is called.</summary>
    private static string? Said(string source) =>
        Written(source).First().Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;
}
