using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Pure parser tests for the Mermaid quadrant-chart and sequence-diagram parsers.
/// WPF-free — exercises parsing only, not rendering.
/// </summary>
[TestClass]
public class DiagramParsersTests
{
    // ── Sequence diagram ──────────────────────────────────────────────────

    private const string SequenceSrc =
        """
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
            Alice-)John: See you later!
        """;

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_AutoCreatesParticipantsInOrder()
    {
        var d = new MermaidSequenceParser().Parse(SequenceSrc);

        CollectionAssert.AreEqual(
            new[] { "Alice", "John" },
            d.Participants.Select(p => p.Id).ToArray());
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ParsesMessagesWithLineAndHeadStyles()
    {
        var d = new MermaidSequenceParser().Parse(SequenceSrc);

        Assert.AreEqual(3, d.Messages.Count);

        var m0 = d.Messages[0];
        Assert.AreEqual("Alice", m0.FromId);
        Assert.AreEqual("John", m0.ToId);
        Assert.AreEqual("Hello John, how are you?", m0.Text);
        Assert.AreEqual(SequenceLineStyle.Solid, m0.Line);
        Assert.AreEqual(SequenceArrowHead.Filled, m0.Head);

        var m1 = d.Messages[1];
        Assert.AreEqual(SequenceLineStyle.Dashed, m1.Line);
        Assert.AreEqual(SequenceArrowHead.Filled, m1.Head);

        var m2 = d.Messages[2];
        Assert.AreEqual(SequenceLineStyle.Solid, m2.Line);
        Assert.AreEqual(SequenceArrowHead.Open, m2.Head);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ParticipantAliasSetsLabel()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant A as Alice\n    A->>B: hi\n");

        var a = d.Participants.Single(p => p.Id == "A");
        Assert.AreEqual("Alice", a.Label);
        // B is implicit → label defaults to its id.
        Assert.AreEqual("B", d.Participants.Single(p => p.Id == "B").Label);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_CrossAndOpenHeads()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    A-xB: lost\n    A--)B: async\n");

        Assert.AreEqual(SequenceArrowHead.Cross, d.Messages[0].Head);
        Assert.AreEqual(SequenceLineStyle.Solid, d.Messages[0].Line);
        Assert.AreEqual(SequenceArrowHead.Open, d.Messages[1].Head);
        Assert.AreEqual(SequenceLineStyle.Dashed, d.Messages[1].Line);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_SelfMessageHasMatchingEndpoints()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>Alice: thinking\n");

        Assert.AreEqual(1, d.Messages.Count);
        Assert.AreEqual(d.Messages[0].FromId, d.Messages[0].ToId);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_SkipsControlFlowKeywords()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    loop Every minute\n    Alice->>John: ping\n    end\n");

        Assert.AreEqual(1, d.Messages.Count);
        Assert.AreEqual("ping", d.Messages[0].Text);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_PlainArrowHasNoHead()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    A->B: note\n");
        Assert.AreEqual(SequenceArrowHead.None, d.Messages[0].Head);
        Assert.AreEqual(SequenceLineStyle.Solid, d.Messages[0].Line);
    }

    // ── Sequence: participant metadata, notes, activations, fragments ──────

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ParticipantTypeMetadata_SetsKind()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant DB@{ \"type\": \"database\" }\n    A->>DB: q\n");
        Assert.AreEqual(ParticipantKind.Database, d.Find("DB")!.Kind);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ActorKeyword_SetsActorKind()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    actor Alice\n    Alice->>Bob: hi\n");
        Assert.AreEqual(ParticipantKind.Actor, d.Find("Alice")!.Kind);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_InlineAlias_BecomesLabel()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant API@{ \"type\": \"boundary\", \"alias\": \"Public API\" }\n    API->>API: x\n");
        Assert.AreEqual("Public API", d.Find("API")!.Label);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_AsLabelWinsOverInlineAlias()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant API@{ \"alias\": \"Internal Name\" } as External Name\n    API->>API: x\n");
        Assert.AreEqual("External Name", d.Find("API")!.Label);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_CreateActorWithAlias()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    create actor D as Donald\n    A->>D: hi\n");
        var donald = d.Find("D")!;
        Assert.AreEqual("Donald", donald.Label);
        Assert.AreEqual(ParticipantKind.Actor, donald.Kind);
        Assert.IsTrue(donald.Created);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_Destroy_MarksAndEmitsItem()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    A->>Carl: hi\n    destroy Carl\n    A-xCarl: bye\n");
        Assert.IsTrue(d.Find("Carl")!.Destroyed);
        Assert.AreEqual(1, d.Items.OfType<SequenceDestroy>().Count());
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ActivationShorthand_OnMessage()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>+John: hi\n    John-->>-Alice: bye\n");
        var msgs = d.Messages;
        Assert.IsTrue(msgs[0].ActivateTarget);
        Assert.IsFalse(msgs[0].DeactivateSource);
        Assert.IsTrue(msgs[1].DeactivateSource);
        // Endpoints keep their bare ids (no '+'/'-').
        Assert.AreEqual("John", msgs[0].ToId);
        Assert.AreEqual("Alice", msgs[1].ToId);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_ExplicitActivateDeactivate()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    activate John\n    deactivate John\n");
        var acts = d.Items.OfType<SequenceActivation>().ToList();
        Assert.AreEqual(2, acts.Count);
        Assert.IsTrue(acts[0].Activate);
        Assert.IsFalse(acts[1].Activate);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_Autonumber_NumbersMessages()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    autonumber\n    A->>B: one\n    B->>A: two\n");
        Assert.AreEqual(1, d.Messages[0].Number);
        Assert.AreEqual(2, d.Messages[1].Number);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_NoteOverTwoParticipants()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Note over Alice,John: A typical interaction\n");
        var note = d.Items.OfType<SequenceNote>().Single();
        Assert.AreEqual(NotePlacement.Over, note.Placement);
        CollectionAssert.AreEqual(new[] { "Alice", "John" }, note.ParticipantIds.ToArray());
        Assert.AreEqual("A typical interaction", note.Text);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_NoteRightOf()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    Note right of John: hello\n");
        Assert.AreEqual(NotePlacement.RightOf, d.Items.OfType<SequenceNote>().Single().Placement);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_AltFragment_EmitsBoundaries()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    alt is sick\n    B->>A: bad\n    else is well\n    B->>A: good\n    end\n");
        var frags = d.Items.OfType<SequenceFragment>().ToList();
        Assert.AreEqual(FragmentBoundary.Begin, frags[0].Boundary);
        Assert.AreEqual(FragmentKind.Alt, frags[0].Kind);
        Assert.AreEqual("is sick", frags[0].Label);
        Assert.AreEqual(FragmentBoundary.Section, frags[1].Boundary);
        Assert.AreEqual("is well", frags[1].Label);
        Assert.AreEqual(FragmentBoundary.End, frags[2].Boundary);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_NestedFragments_BalanceBeginAndEnd()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    par a\n    A->>B: x\n    par b\n    A->>B: y\n    end\n    end\n");
        var frags = d.Items.OfType<SequenceFragment>().ToList();
        Assert.AreEqual(2, frags.Count(f => f.Boundary == FragmentBoundary.Begin));
        Assert.AreEqual(2, frags.Count(f => f.Boundary == FragmentBoundary.End));
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_Box_GroupsParticipants()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    box Purple Group\n    participant A\n    participant J\n    end\n    A->>J: hi\n");
        var box = d.Boxes.Single();
        CollectionAssert.AreEqual(new[] { "A", "J" }, box.ParticipantIds.ToArray());
        Assert.AreEqual("Group", box.Label);   // leading colour word stripped
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_BrBecomesNewline()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant A as Alice<br/>Johnson\n    A->>A: x\n");
        StringAssert.Contains(d.Find("A")!.Label, "\n");
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_CentralConnectionMarkersStripped()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>()John: hi\n    Alice()->>John: yo\n");
        CollectionAssert.AreEqual(new[] { "Alice", "John" }, d.Participants.Select(p => p.Id).ToArray());
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_CentralConnectionDotFlags()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>()John: a\n    Alice()->>John: b\n    John()->>()Alice: c\n");
        var m = d.Messages;
        Assert.IsTrue(m[0].DotTarget);  Assert.IsFalse(m[0].DotSource);   // ()John
        Assert.IsTrue(m[1].DotSource);  Assert.IsFalse(m[1].DotTarget);   // Alice()
        Assert.IsTrue(m[2].DotSource);  Assert.IsTrue(m[2].DotTarget);    // John()…()Alice
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_BidirectionalArrows()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    A<<->>B: solid\n    A<<-->>B: dotted\n");
        var m = d.Messages;
        Assert.IsTrue(m[0].Bidirectional);
        Assert.AreEqual(SequenceLineStyle.Solid, m[0].Line);
        Assert.AreEqual(SequenceArrowHead.Filled, m[0].Head);
        Assert.IsTrue(m[1].Bidirectional);
        Assert.AreEqual(SequenceLineStyle.Dashed, m[1].Line);
        Assert.AreEqual("A", m[0].FromId);
        Assert.AreEqual("B", m[0].ToId);
    }

    [TestMethod]
    [CoversNode("sequence-diagram")]
    public void Sequence_AsyncAndDottedArrowVariants()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    A-)B: one\n    A--)B: two\n    A--xB: three\n");
        var m = d.Messages;
        Assert.AreEqual(SequenceArrowHead.Open,  m[0].Head); Assert.AreEqual(SequenceLineStyle.Solid,  m[0].Line);
        Assert.AreEqual(SequenceArrowHead.Open,  m[1].Head); Assert.AreEqual(SequenceLineStyle.Dashed, m[1].Line);
        Assert.AreEqual(SequenceArrowHead.Cross, m[2].Head); Assert.AreEqual(SequenceLineStyle.Dashed, m[2].Line);
    }

    // ── Flowchart: @{ shape }, multidirection arrows, edge ids ────────────

    [TestMethod]
    public void Flowchart_ShapeMetadata_SetsLabelAndShape()
    {
        var g = new MermaidFlowchartParser().Parse(
            "flowchart RL\n    A@{ shape: manual-file, label: \"File Handling\" }\n");
        var a = g.FindNode("A")!;
        Assert.AreEqual("File Handling", a.Label);
        Assert.AreEqual(NodeShape.TrapezoidAlt, a.Shape);
    }

    [TestMethod]
    public void Flowchart_ShapeAliases_MapToDocument()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TD\n    A@{ shape: docs, label: \"Docs\" }\n");
        Assert.AreEqual(NodeShape.Document, g.FindNode("A")!.Shape);
    }

    [TestMethod]
    public void Flowchart_EdgeMetadata_IsNotANode()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    e1@{ curve: linear }\n    A-->B\n");
        Assert.IsNull(g.FindNode("e1"));
        Assert.AreEqual(2, g.Nodes.Count);
    }

    [TestMethod]
    public void Flowchart_MultidirectionArrows()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    A o--o B\n    B <--> C\n    C x--x D\n");
        var e = g.Edges;
        Assert.AreEqual(EdgeArrow.Circle, e[0].StartArrow); Assert.AreEqual(EdgeArrow.Circle, e[0].Arrow);
        Assert.AreEqual(EdgeArrow.Normal, e[1].StartArrow); Assert.AreEqual(EdgeArrow.Normal, e[1].Arrow);
        Assert.AreEqual(EdgeArrow.Cross,  e[2].StartArrow); Assert.AreEqual(EdgeArrow.Cross,  e[2].Arrow);
    }

    [TestMethod]
    public void Flowchart_ExtraDashes_ParseAsArrows()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TD\n    A ----> B\n    C -- lbl ----> D\n");
        Assert.AreEqual(1, g.Edges.Count(e => e.SourceId == "A" && e.TargetId == "B"));
        var labelled = g.Edges.Single(e => e.SourceId == "C");
        Assert.AreEqual("D", labelled.TargetId);
        Assert.AreEqual("lbl", labelled.Label);
    }

    [TestMethod]
    public void Flowchart_InlineEdgeId_IsStripped()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    A e1@==> B\n");
        Assert.IsNull(g.FindNode("e1"));
        var e = g.Edges.Single();
        Assert.AreEqual("A", e.SourceId);
        Assert.AreEqual("B", e.TargetId);
        Assert.AreEqual(EdgeStyle.Thick, e.Style);
    }

    [TestMethod]
    public void Flowchart_HyphenIsArrowNotNodeId()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TB\n    c1-->a2\n");
        Assert.IsNotNull(g.FindNode("c1"));
        Assert.IsNotNull(g.FindNode("a2"));
        Assert.IsNull(g.FindNode("c1--"));
        Assert.AreEqual(1, g.Edges.Count(e => e.SourceId == "c1" && e.TargetId == "a2"));
    }

    [TestMethod]
    public void Flowchart_DirectionKeyword_IsNotANode()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    subgraph S\n    direction TB\n    a-->b\n    end\n");
        Assert.IsNull(g.FindNode("direction"));
    }

    [TestMethod]
    public void Flowchart_StadiumAndCylinderShapes()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    A([Done]) --> B[(Store)]\n");
        var a = g.FindNode("A")!;
        Assert.AreEqual(NodeShape.Stadium, a.Shape);
        Assert.AreEqual("Done", a.Label);
        var b = g.FindNode("B")!;
        Assert.AreEqual(NodeShape.Cylinder, b.Shape);
        Assert.AreEqual("Store", b.Label);
    }

    [TestMethod]
    public void Flowchart_CardShapeMetadata()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TD\n    A@{ shape: card, label: \"Note\" }\n");
        var a = g.FindNode("A")!;
        Assert.AreEqual(NodeShape.Card, a.Shape);
        Assert.AreEqual("Note", a.Label);
    }

    [TestMethod]
    public void Flowchart_Subgraph_TracksBothEndpointsAcrossArrow()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TB\n    subgraph one\n    a1-->a2\n    end\n");
        var sg = g.Subgraphs.Single();
        CollectionAssert.Contains(sg.NodeIds, "a1");   // source was previously missed (followed by '-')
        CollectionAssert.Contains(sg.NodeIds, "a2");
    }

    [TestMethod]
    public void Flowchart_ChainedEdges_BecomeSeparateHops()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart LR\n    A --> TOP --> B\n");
        Assert.AreEqual(2, g.Edges.Count);
        Assert.IsTrue(g.Edges.Any(e => e is { SourceId: "A",   TargetId: "TOP" }));
        Assert.IsTrue(g.Edges.Any(e => e is { SourceId: "TOP", TargetId: "B" }));
        Assert.IsNotNull(g.FindNode("B"));   // the chain's tail node used to be dropped
    }

    [TestMethod]
    public void Flowchart_ChainCarriesArrowStyleAndLabel()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TD\n    A -- yes --> B -.-> C\n");
        var ab = g.Edges.Single(e => e.SourceId == "A");
        Assert.AreEqual("yes", ab.Label);
        Assert.AreEqual(EdgeStyle.Solid, ab.Style);
        Assert.AreEqual(EdgeStyle.Dotted, g.Edges.Single(e => e.SourceId == "B").Style);
    }

    [TestMethod]
    public void Flowchart_FanOut_AndChainCompose()
    {
        var g = new MermaidFlowchartParser().Parse("flowchart TD\n    A --> B & C --> D\n");
        foreach (var (s, t) in new[] { ("A", "B"), ("A", "C"), ("B", "D"), ("C", "D") })
            Assert.IsTrue(g.Edges.Any(e => e.SourceId == s && e.TargetId == t), $"missing {s}->{t}");
        Assert.AreEqual(4, g.Edges.Count);
    }

    [TestMethod]
    public void Flowchart_NestedSubgraphs_CarryParentLinks()
    {
        var g = new MermaidFlowchartParser().Parse(
            """
            flowchart LR
              subgraph TOP
                subgraph B1
                    i1 --> f1
                end
                subgraph B2
                    i2 --> f2
                end
              end
            """);

        Subgraph Sg(string id) => g.Subgraphs.Single(s => s.Id == id);
        Assert.IsNull(Sg("TOP").ParentId);                 // outer subgraph is top level
        Assert.AreEqual("TOP", Sg("B1").ParentId);         // inner subgraphs nest under it
        Assert.AreEqual("TOP", Sg("B2").ParentId);
    }



    // ── Class diagram ──────────────────────────────────────────────────────

    private static Graph Class(string src) => new MermaidClassParser().Parse(src);

    [TestMethod]
    public void Class_BlockMembers_SplitIntoAttributesAndMethods()
    {
        var g = Class(
            """
            classDiagram
            class BankAccount {
                +String owner
                +BigDecimal balance
                +deposit(amount)
                +withdrawal(amount)
            }
            """);

        var n = g.FindNode("BankAccount")!;
        Assert.AreEqual(NodeShape.ClassBox, n.Shape);
        Assert.AreEqual(2, n.Class!.Attributes.Count);
        Assert.AreEqual(2, n.Class!.Methods.Count);
        Assert.AreEqual("+String owner", n.Class.Attributes[0].Text);
        Assert.IsTrue(n.Class.Methods.Any(m => m.Text == "+deposit(amount)"));
    }

    [TestMethod]
    public void Class_ShorthandMembers_AccumulateOnClass()
    {
        var g = Class(
            """
            classDiagram
            class Animal
            Animal : +int age
            Animal: +isMammal()
            """);

        var n = g.FindNode("Animal")!;
        Assert.AreEqual("+int age", n.Class!.Attributes.Single().Text);
        Assert.AreEqual("+isMammal()", n.Class.Methods.Single().Text);
    }

    [TestMethod]
    public void Class_Generics_RenderAsAngleBrackets()
    {
        var g = Class(
            """
            classDiagram
            class Square~Shape~
            Square : List~int~ position
            """);

        Assert.AreEqual("Square<Shape>", g.FindNode("Square")!.Label);
        Assert.AreEqual("List<int> position", g.FindNode("Square")!.Class!.Attributes.Single().Text);
    }

    [TestMethod]
    public void Class_Classifiers_FlagStaticAndAbstract()
    {
        var g = Class(
            """
            classDiagram
            class C {
                +int count$
                +staticMethod()$
                +abstractMethod()*
            }
            """);

        var c = g.FindNode("C")!.Class!;
        Assert.IsTrue(c.Attributes.Single(a => a.Text == "+int count").IsStatic);
        Assert.IsTrue(c.Methods.Single(m => m.Text == "+staticMethod()").IsStatic);
        Assert.IsTrue(c.Methods.Single(m => m.Text == "+abstractMethod()").IsAbstract);
    }

    [TestMethod]
    public void Class_Inheritance_HollowTriangleAtParentSource()
    {
        var g = Class("classDiagram\n    Animal <|-- Duck");
        var e = g.Edges.Single();
        // Left operand is the parent (layout source / top); the hollow triangle sits at that end.
        Assert.AreEqual("Animal", e.SourceId);
        Assert.AreEqual("Duck",   e.TargetId);
        Assert.AreEqual(EdgeArrow.TriangleHollow, e.StartArrow);
        Assert.AreEqual(EdgeArrow.None,           e.Arrow);
    }

    [TestMethod]
    public void Class_RelationshipOperators_MapToHeadsAndStyle()
    {
        var g = Class(
            """
            classDiagram
            classC *-- classD
            classE o-- classF
            classG <-- classH
            classI -- classJ
            classK <.. classL
            classM <|.. classN
            classO .. classP
            """);

        Edge E(string s, string t) => g.Edges.Single(e => e.SourceId == s && e.TargetId == t);

        Assert.AreEqual(EdgeArrow.DiamondFilled, E("classC", "classD").StartArrow);   // composition
        Assert.AreEqual(EdgeArrow.DiamondHollow, E("classE", "classF").StartArrow);   // aggregation
        Assert.AreEqual(EdgeArrow.Open,          E("classG", "classH").StartArrow);   // directed association
        Assert.AreEqual(EdgeArrow.None,          E("classI", "classJ").Arrow);        // plain solid link
        Assert.AreEqual(EdgeStyle.Solid,         E("classI", "classJ").Style);
        Assert.AreEqual(EdgeStyle.Dashed,        E("classK", "classL").Style);        // dependency line
        Assert.AreEqual(EdgeArrow.TriangleHollow, E("classM", "classN").StartArrow);  // realization head
        Assert.AreEqual(EdgeStyle.Dashed,         E("classM", "classN").Style);
        Assert.AreEqual(EdgeStyle.Dashed,         E("classO", "classP").Style);       // dashed link
    }

    [TestMethod]
    public void Class_DirectedAssociation_ArrowAtTarget()
    {
        var g = Class("classDiagram\n    Teacher --> Course");
        var e = g.Edges.Single();
        Assert.AreEqual(EdgeArrow.Open, e.Arrow);
        Assert.AreEqual(EdgeArrow.None, e.StartArrow);
    }

    [TestMethod]
    public void Class_Multiplicity_AndLabel_AttachToEnds()
    {
        var g = Class("classDiagram\n    Customer \"1\" --> \"*\" Ticket : owns");
        var e = g.Edges.Single();
        Assert.AreEqual("Customer", e.SourceId);
        Assert.AreEqual("Ticket",   e.TargetId);
        Assert.AreEqual("1",    e.StartLabel);
        Assert.AreEqual("*",    e.EndLabel);
        Assert.AreEqual("owns", e.Label);
        // The "0..*" form must not be mistaken for a dashed-link operator inside the quotes.
        var g2 = Class("classDiagram\n    Student \"1\" --o \"1..*\" Course");
        var e2 = g2.Edges.Single();
        Assert.AreEqual("1",    e2.StartLabel);
        Assert.AreEqual("1..*", e2.EndLabel);
        Assert.AreEqual(EdgeArrow.DiamondHollow, e2.Arrow);   // --o head at the target end
        Assert.AreEqual(EdgeStyle.Solid, e2.Style);            // the ".." is inside quotes, not an operator
    }

    [TestMethod]
    public void Class_Annotation_SetsStereotype_BlockAndStandalone()
    {
        var block = Class("classDiagram\n    class Shape {\n        <<interface>>\n        draw()\n    }");
        Assert.AreEqual("interface", block.FindNode("Shape")!.Class!.Stereotype);

        var standalone = Class("classDiagram\n    class Shape\n    <<interface>> Shape");
        Assert.AreEqual("interface", standalone.FindNode("Shape")!.Class!.Stereotype);
    }

    [TestMethod]
    public void Class_Namespace_BecomesSubgraphWithMembers()
    {
        var g = Class(
            """
            classDiagram
            namespace BaseShapes {
                class Triangle
                class Rectangle {
                    +double width
                }
            }
            """);

        var sg = g.Subgraphs.SingleOrDefault(s => s.Id == "BaseShapes");
        Assert.IsNotNull(sg);
        CollectionAssert.AreEquivalent(new[] { "Triangle", "Rectangle" }, sg!.NodeIds);
        Assert.AreEqual("+double width", g.FindNode("Rectangle")!.Class!.Attributes.Single().Text);
    }

    [TestMethod]
    public void Class_Note_GeneralAndForTarget()
    {
        var g = Class(
            """
            classDiagram
            class Duck
            note "A general note"
            note for Duck "Can fly\nCan swim"
            """);

        Assert.AreEqual(2, g.Nodes.Count(n => n.Shape == NodeShape.Note));
        // "note for Duck" attaches a dotted, head-less edge from the class to its note.
        var note = g.Nodes.First(n => n.Shape == NodeShape.Note && n.Label.Contains("Can fly"));
        Assert.IsTrue(note.Label.Contains('\n'), "\\n should become a real newline");
        Assert.IsTrue(g.Edges.Any(e => e.SourceId == "Duck" && e.TargetId == note.Id && e.Style == EdgeStyle.Dotted));
    }

    [TestMethod]
    public void Class_Styling_ClassDefInlineAndStyleApply()
    {
        var g = Class(
            """
            classDiagram
            class Student
            class Course
            classDef highlight fill:#f9f,stroke:#333,color:#000
            class Student:::highlight
            style Course fill:#bbf,stroke:#338
            """);

        var s = g.FindNode("Student")!;
        Assert.AreEqual("#f9f", s.FillColor);
        Assert.AreEqual("#000", s.TextColor);
        var c = g.FindNode("Course")!;
        Assert.AreEqual("#bbf", c.FillColor);
        Assert.AreEqual("#338", c.StrokeColor);
    }

    [TestMethod]
    public void Class_Direction_IsParsed()
    {
        var g = Class("classDiagram\n    direction LR\n    class A\n    A --> B");
        Assert.AreEqual(GraphDirection.LeftRight, g.Direction);
    }

    [TestMethod]
    public void Class_NestedGenerics_CloseProperly()
    {
        var g = Class("classDiagram\n    Square : +getDistanceMatrix() List~List~int~~");
        Assert.AreEqual("+getDistanceMatrix() : List<List<int>>",
            g.FindNode("Square")!.Class!.Methods.Single().Text);
    }

    [TestMethod]
    public void Class_MethodReturnType_FormattedWithColon()
    {
        var g = Class("classDiagram\n    Square : getId() int\n    Square : setId(int id)");
        var c = g.FindNode("Square")!.Class!;
        Assert.AreEqual("getId() : int", c.Methods.Single(m => m.Text.StartsWith("getId")).Text);
        Assert.AreEqual("setId(int id)", c.Methods.Single(m => m.Text.StartsWith("setId")).Text);  // no return → no colon
    }

    [TestMethod]
    public void Class_PackageVisibilityTilde_IsNotAGeneric()
    {
        var g = Class("classDiagram\n    class C {\n        ~packagePrivateMethod()\n    }");
        Assert.AreEqual("~packagePrivateMethod()", g.FindNode("C")!.Class!.Methods.Single().Text);
    }

    [TestMethod]
    public void Class_TwoWayRelation_HeadsAtBothEnds()
    {
        var g = Class("classDiagram\n    Animal <|--|> Zebra");
        var e = g.Edges.Single();
        Assert.AreEqual("Animal", e.SourceId);
        Assert.AreEqual("Zebra",  e.TargetId);
        Assert.AreEqual(EdgeArrow.TriangleHollow, e.StartArrow);
        Assert.AreEqual(EdgeArrow.TriangleHollow, e.Arrow);
    }

    [TestMethod]
    public void Class_Lollipop_AttachesAsDecorationNotNodeOrEdge()
    {
        var g = Class("classDiagram\n    Class01 --() bar\n    foo ()-- Class01");
        // Interface names are decorations on the class — neither graph nodes nor routed edges.
        Assert.IsNull(g.FindNode("bar"));
        Assert.IsNull(g.FindNode("foo"));
        Assert.AreEqual(0, g.Edges.Count);

        var lollipops = g.FindNode("Class01")!.Class!.Lollipops;
        Assert.AreEqual(2, lollipops.Count);
        Assert.IsTrue(lollipops.Any(l => l is { Name: "bar", Below: true  }));  // A --() bar : hangs below
        Assert.IsTrue(lollipops.Any(l => l is { Name: "foo", Below: false }));  // foo ()-- A : sits above
    }

    [TestMethod]
    public void Class_HierarchicalNamespaces_NestByDottedName()
    {
        var g = Class(
            """
            classDiagram
            namespace Company.Engineering.Backend {
                class Developer
            }
            namespace Company.Engineering {
                class TechLead
            }
            """);

        Subgraph Sg(string id) => g.Subgraphs.Single(s => s.Id == id);
        Assert.IsNull(Sg("Company").ParentId);                                              // implicit root ancestor
        Assert.AreEqual("Company",             Sg("Company.Engineering").ParentId);
        Assert.AreEqual("Company.Engineering", Sg("Company.Engineering.Backend").ParentId);
        Assert.AreEqual("Backend", Sg("Company.Engineering.Backend").Label);                // label is the leaf segment
        Assert.IsTrue(Sg("Company.Engineering.Backend").NodeIds.Contains("Developer"));     // class lands in its leaf only
        Assert.IsTrue(Sg("Company.Engineering").NodeIds.Contains("TechLead"));
        Assert.AreEqual(1, g.Subgraphs.Count(s => s.Id == "Company"));                       // shared ancestor created once
    }

    [TestMethod]
    public void Class_NoteBr_BecomesNewline()
    {
        var g = Class("classDiagram\n    note for Duck \"can fly<br>can swim\"");
        var note = g.Nodes.Single(n => n.Shape == NodeShape.Note);
        Assert.AreEqual("can fly\ncan swim", note.Label);
    }

    // ── Requirement diagram ────────────────────────────────────────────────

    private static Graph Requirement(string src) => new MermaidRequirementParser().Parse(src);

    [TestMethod]
    public void Requirement_Block_BecomesSingleCompartmentBoxWithFields()
    {
        var g = Requirement(
            """
            requirementDiagram
            functionalRequirement test_req {
                id: 1.1
                text: the test text.
                risk: high
                verifymethod: test
            }
            """);

        var n = g.FindNode("test_req")!;
        Assert.AreEqual(NodeShape.ClassBox, n.Shape);
        Assert.IsTrue(n.Class!.SingleCompartment);
        Assert.AreEqual("Functional Requirement", n.Class.Stereotype);   // camelCase → spaced title case
        Assert.AreEqual(0, n.Class.Methods.Count);
        CollectionAssert.AreEqual(
            new[] { "Id: 1.1", "Text: the test text.", "Risk: High", "Verification: Test" },
            n.Class.Attributes.Select(a => a.Text).ToArray());                 // risk/method values title-cased
    }

    [TestMethod]
    public void Requirement_Element_HasElementStereotype()
    {
        var g = Requirement(
            """
            requirementDiagram
            element test_entity {
                type: "test suite"
                docref: github.com/all_the_tests
            }
            """);

        var n = g.FindNode("test_entity")!;
        Assert.AreEqual("Element", n.Class!.Stereotype);
        CollectionAssert.AreEqual(
            new[] { "Type: test suite", "Doc Ref: github.com/all_the_tests" },
            n.Class.Attributes.Select(a => a.Text).ToArray());
    }

    [TestMethod]
    public void Requirement_Relationship_IsDashedOpenArrowLabelledWithType()
    {
        var g = Requirement("requirementDiagram\n    test_entity - satisfies -> test_req2");
        var e = g.Edges.Single();
        Assert.AreEqual("test_entity", e.SourceId);
        Assert.AreEqual("test_req2",   e.TargetId);
        Assert.AreEqual(EdgeStyle.Dashed, e.Style);
        Assert.AreEqual(EdgeArrow.Open,   e.Arrow);
        Assert.AreEqual("«satisfies»",    e.Label);
        // Endpoints referenced by a relationship still materialise as boxes.
        Assert.AreEqual(NodeShape.ClassBox, g.FindNode("test_req2")!.Shape);
    }

    [TestMethod]
    public void Requirement_Contains_IsSolidCrosshairAtContainer()
    {
        var g = Requirement("requirementDiagram\n    test_req - contains -> test_req3");
        var e = g.Edges.Single();
        Assert.AreEqual("test_req",  e.SourceId);
        Assert.AreEqual("test_req3", e.TargetId);
        Assert.AreEqual(EdgeStyle.Solid,       e.Style);          // contains is a solid composite link…
        Assert.AreEqual(EdgeArrow.CrossCircle, e.StartArrow);     // …with the crosshair at the container
        Assert.AreEqual(EdgeArrow.None,        e.Arrow);          // …and no head at the contained end
        Assert.AreEqual("«contains»",          e.Label);
    }

    [TestMethod]
    public void Requirement_ReverseArrowForm_KeepsSourceToTargetDirection()
    {
        var g = Requirement("requirementDiagram\n    database <- satisfies - db_req");
        var e = g.Edges.Single();
        Assert.AreEqual("db_req",   e.SourceId);   // "{dst} <- type - {src}" still flows src → dst
        Assert.AreEqual("database", e.TargetId);
        Assert.AreEqual("«satisfies»", e.Label);
    }

    [TestMethod]
    public void Requirement_DirectionAndStyling_Apply()
    {
        var g = Requirement(
            """
            requirementDiagram
                direction LR
                requirement r {
                    id: 1
                }
                element e {
                    type: x
                }
                classDef important fill:#f9f,color:#000
                class r important
                style e fill:#bbf,stroke:#338
            """);

        Assert.AreEqual(GraphDirection.LeftRight, g.Direction);
        Assert.AreEqual("#f9f", g.FindNode("r")!.FillColor);
        Assert.AreEqual("#bbf", g.FindNode("e")!.FillColor);
        Assert.AreEqual("#338", g.FindNode("e")!.StrokeColor);
    }

    // ── Sankey — parser ───────────────────────────────────────────────────

    // ── Sankey — config ───────────────────────────────────────────────────

    // ── ER diagram — parser ───────────────────────────────────────────────

    private const string ErSrc =
        """
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            ORDER ||--|{ LINE-ITEM : contains
            CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
        """;

    [TestMethod]
    public void Er_SymbolCardinalityAndIdentification()
    {
        var g = new MermaidErParser().Parse(ErSrc);

        CollectionAssert.AreEquivalent(
            new[] { "CUSTOMER", "ORDER", "LINE-ITEM", "DELIVERY-ADDRESS" },
            g.Nodes.Select(n => n.Id).ToArray());
        Assert.IsTrue(g.Nodes.All(n => n.Shape == NodeShape.ClassBox));

        var places = g.Edges[0];
        Assert.AreEqual("CUSTOMER", places.SourceId);
        Assert.AreEqual("ORDER", places.TargetId);
        Assert.AreEqual("places", places.Label);
        Assert.AreEqual(EdgeStyle.Solid, places.Style);                 // -- identifying
        Assert.AreEqual(EdgeArrow.ErExactlyOne, places.StartArrow);     // ||
        Assert.AreEqual(EdgeArrow.ErZeroMany,  places.Arrow);          // o{

        var uses = g.Edges[2];
        Assert.AreEqual(EdgeStyle.Dashed, uses.Style);                  // .. non-identifying
        Assert.AreEqual(EdgeArrow.ErOneMany, uses.StartArrow);         // }|
        Assert.AreEqual(EdgeArrow.ErOneMany, uses.Arrow);             // |{
    }

    [TestMethod]
    public void Er_NoSpaceSymbol()
    {
        var g = new MermaidErParser().Parse("erDiagram\n    id1||--o| id2 : label\n");
        var e = g.Edges.Single();
        Assert.AreEqual("id1", e.SourceId);
        Assert.AreEqual("id2", e.TargetId);
        Assert.AreEqual(EdgeArrow.ErExactlyOne, e.StartArrow);
        Assert.AreEqual(EdgeArrow.ErZeroOne, e.Arrow);
    }

    [TestMethod]
    public void Er_WordAliasCardinality()
    {
        var g = new MermaidErParser().Parse(
            "erDiagram\n    CAR 1 to zero or more NAMED-DRIVER : allows\n    PERSON many(0) optionally to 0+ NAMED-DRIVER : is\n");

        var allows = g.Edges[0];
        Assert.AreEqual("CAR", allows.SourceId);
        Assert.AreEqual("NAMED-DRIVER", allows.TargetId);
        Assert.AreEqual(EdgeStyle.Solid, allows.Style);                 // "to"
        Assert.AreEqual(EdgeArrow.ErExactlyOne, allows.StartArrow);     // 1
        Assert.AreEqual(EdgeArrow.ErZeroMany, allows.Arrow);          // zero or more

        var isRel = g.Edges[1];
        Assert.AreEqual(EdgeStyle.Dashed, isRel.Style);                 // "optionally to"
        Assert.AreEqual(EdgeArrow.ErZeroMany, isRel.StartArrow);       // many(0)
        Assert.AreEqual(EdgeArrow.ErZeroMany, isRel.Arrow);          // 0+
    }

    [TestMethod]
    public void Er_AttributesWithKeysAndComment()
    {
        var g = new MermaidErParser().Parse(
            "erDiagram\n    PERSON {\n        string driversLicense PK \"The license #\"\n        string[] parts\n        string code PK, FK\n    }\n");

        var attrs = g.FindNode("PERSON")!.Class!.Attributes;
        Assert.AreEqual(3, attrs.Count);
        StringAssert.Contains(attrs[0].Text, "string driversLicense");
        StringAssert.Contains(attrs[0].Text, "PK");
        StringAssert.Contains(attrs[0].Text, "The license #");
        StringAssert.Contains(attrs[1].Text, "string[] parts");
        StringAssert.Contains(attrs[2].Text, "PK, FK");
    }

    [TestMethod]
    public void Er_EntityAliases()
    {
        var g = new MermaidErParser().Parse(
            "erDiagram\n    p[Person] {\n        string firstName\n    }\n    a[\"Customer Account\"] {\n        string email\n    }\n    p ||--o| a : has\n");

        Assert.AreEqual("Person", g.FindNode("p")!.Label);
        Assert.AreEqual("Customer Account", g.FindNode("a")!.Label);
        Assert.AreEqual(1, g.Edges.Count);
        Assert.AreEqual("p", g.Edges[0].SourceId);
        Assert.AreEqual("a", g.Edges[0].TargetId);
    }

    [TestMethod]
    public void Er_BareEntityAndDirection()
    {
        var g = new MermaidErParser().Parse("erDiagram\n    direction LR\n    CUSTOMER\n");
        Assert.AreEqual(GraphDirection.LeftRight, g.Direction);
        Assert.IsNotNull(g.FindNode("CUSTOMER"));
        Assert.AreEqual(0, g.Edges.Count);
    }

    [TestMethod]
    public void ErConfig_ParsesKeys()
    {
        var cfg = ErConfigParser.Parse(
            """
            config:
              er:
                layoutDirection: LR
                fill: honeydew
                stroke: gray
                minEntityWidth: 120
            """);
        Assert.AreEqual(GraphDirection.LeftRight, cfg.LayoutDirection);
        Assert.AreEqual("honeydew", cfg.Fill);
        Assert.AreEqual("gray", cfg.Stroke);
        Assert.AreEqual(120, cfg.MinEntityWidth);
    }

    // ── Architecture diagram — parser ─────────────────────────────────────



    // ── config: nexaflow: (expansion) ─────────────────────────────────────

    private static string FrontMatter(string body) =>
        MermaidBlock.Read("---\n" + body + "\n---\ngraph LR\n  a --> b\n").Config!;

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void Nexaflow_ExpandDepthAndFanOutAreRead()
    {
        var cfg = NexaflowConfigParser.Parse(FrontMatter(
            """
            config:
              nexaflow:
                expandDepth: 2
                maxFanOut: 30
            """));

        Assert.AreEqual(2,  cfg.ExpandDepth);
        Assert.AreEqual(30, cfg.MaxFanOut);
    }

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void Nexaflow_CollapsedAcceptsBothAListAndAKeyedBlock()
    {
        // A producer that only needs ids uses the list; one that wants its own name back — the PE
        // inspector thinks in module names, not in "n7" — uses the keyed form.
        var list = NexaflowConfigParser.Parse(FrontMatter(
            """
            config:
              nexaflow:
                collapsed: [n1, n2]
            """));
        CollectionAssert.AreEquivalent(new[] { "n1", "n2" }, list.Collapsed.Keys.ToArray());
        Assert.AreEqual("n1", list.Collapsed["n1"], "Without a key, a node answers with its own id.");

        var keyed = NexaflowConfigParser.Parse(FrontMatter(
            """
            config:
              nexaflow:
                collapsed:
                  n3: KERNEL32.dll
                expanded:
                  n0: "app.exe"
            """));
        Assert.AreEqual("KERNEL32.dll", keyed.Collapsed["n3"]);
        Assert.AreEqual("app.exe",      keyed.Expanded["n0"]);
    }

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void Nexaflow_KeysOutsideTheNamespaceAreNotMistakenForIt()
    {
        // The whole point of the namespace: another diagram's config can use these words freely.
        var cfg = NexaflowConfigParser.Parse(FrontMatter(
            """
            config:
              er:
                expandDepth: 9
              themeVariables:
                collapsed:
                  n1: nope
            """));

        Assert.IsNull(cfg.ExpandDepth);
        Assert.AreEqual(0, cfg.Collapsed.Count);
        Assert.IsTrue(cfg.IsEmpty, "Nothing outside config.nexaflow may switch expansion on.");
    }

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void Nexaflow_UnknownKeysAndMalformedValuesAreIgnored()
    {
        var cfg = NexaflowConfigParser.Parse(FrontMatter(
            """
            config:
              nexaflow:
                expandDepth: soon
                somethingNew: 4
                maxFanOut: -3
            """));

        Assert.IsNull(cfg.ExpandDepth, "A value that isn't a number leaves the default alone.");
        Assert.AreEqual(0, cfg.MaxFanOut, "A negative cap is not a cap.");
        Assert.IsTrue(cfg.IsEmpty);
    }

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void Nexaflow_NoFrontMatterMeansNoExpansion()
    {
        Assert.IsTrue(NexaflowConfigParser.Parse(null).IsEmpty);
        Assert.IsTrue(NexaflowConfigParser.Parse("").IsEmpty);
        Assert.IsTrue(NexaflowConfigParser.Parse("title: Just a title").IsEmpty);
    }

    // ── Block diagram — parser ────────────────────────────────────────────
}
