using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// Pure parser tests for the Mermaid quadrant-chart and sequence-diagram parsers.
/// WPF-free — exercises parsing only, not rendering.
/// </summary>
[TestClass]
public class DiagramParsersTests
{
    // ── Quadrant chart ────────────────────────────────────────────────────

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
            Campaign D: [0.78, 0.34]
            Campaign E: [0.40, 0.34]
            Campaign F: [0.35, 0.78]
        """;

    [TestMethod]
    public void Quadrant_ParsesTitleAxesAndQuadrants()
    {
        var c = new MermaidQuadrantParser().Parse(QuadrantSrc);

        Assert.AreEqual("Reach and engagement of campaigns", c.Title);
        Assert.AreEqual("Low Reach", c.XAxisLeft);
        Assert.AreEqual("High Reach", c.XAxisRight);
        Assert.AreEqual("Low Engagement", c.YAxisBottom);
        Assert.AreEqual("High Engagement", c.YAxisTop);
        Assert.AreEqual("We should expand", c.Quadrant1);
        Assert.AreEqual("Need to promote", c.Quadrant2);
        Assert.AreEqual("Re-evaluate", c.Quadrant3);
        Assert.AreEqual("May be improved", c.Quadrant4);
    }

    [TestMethod]
    public void Quadrant_ParsesAllPoints()
    {
        var c = new MermaidQuadrantParser().Parse(QuadrantSrc);

        Assert.AreEqual(6, c.Points.Count);
        Assert.AreEqual("Campaign A", c.Points[0].Label);
        Assert.AreEqual(0.3, c.Points[0].X, 1e-9);
        Assert.AreEqual(0.6, c.Points[0].Y, 1e-9);
    }

    [TestMethod]
    public void Quadrant_AxisWithoutArrow_FillsLowEndOnly()
    {
        var c = new MermaidQuadrantParser().Parse("quadrantChart\n    x-axis Low Reach\n");
        Assert.AreEqual("Low Reach", c.XAxisLeft);
        Assert.AreEqual("", c.XAxisRight);
    }

    [TestMethod]
    public void Quadrant_StripsClassStylingFromPointLabel()
    {
        var c = new MermaidQuadrantParser().Parse("quadrantChart\n    Campaign A:::class1: [0.3, 0.6]\n");
        Assert.AreEqual(1, c.Points.Count);
        Assert.AreEqual("Campaign A", c.Points[0].Label);
    }

    [TestMethod]
    public void Quadrant_ClampsOutOfRangeCoordinates()
    {
        var c = new MermaidQuadrantParser().Parse("quadrantChart\n    P: [1.4, 0.2]\n");
        Assert.AreEqual(1.0, c.Points[0].X, 1e-9);
    }

    // ── Quadrant point styling ────────────────────────────────────────────

    private const string StyledQuadrantSrc =
        """
        quadrantChart
          Campaign A: [0.9, 0.0] radius: 12
          Campaign B:::class1: [0.8, 0.1] color: #ff3300, radius: 10
          Campaign C: [0.7, 0.2] radius: 25, color: #00ff33, stroke-color: #10f0f0
          Campaign D: [0.6, 0.3] stroke-width: 5px ,color: #ff33f0
          Campaign E:::class2: [0.5, 0.4]
          classDef class1 color: #109060
          classDef class2 color: #908342, radius : 10, stroke-color: #310085, stroke-width: 10px
        """;

    [TestMethod]
    public void Quadrant_StyledPoints_AreNotDropped()
    {
        var c = new MermaidQuadrantParser().Parse(StyledQuadrantSrc);
        Assert.AreEqual(5, c.Points.Count);
        Assert.AreEqual("Campaign A", c.Points[0].Label);
    }

    [TestMethod]
    public void Quadrant_ParsesInlineStyleKeys()
    {
        var c = new MermaidQuadrantParser().Parse(StyledQuadrantSrc);
        var d = c.Points.Single(p => p.Label == "Campaign C").Style!;
        Assert.AreEqual(25, d.Radius);
        Assert.AreEqual("#00ff33", d.FillColor);
        Assert.AreEqual("#10f0f0", d.StrokeColor);
    }

    [TestMethod]
    public void Quadrant_StrokeWidthStripsPx_AndToleratesSpacing()
    {
        var c = new MermaidQuadrantParser().Parse(StyledQuadrantSrc);
        var d = c.Points.Single(p => p.Label == "Campaign D").Style!;
        Assert.AreEqual(5, d.StrokeWidth);       // "5px" with a stray space before the comma
        Assert.AreEqual("#ff33f0", d.FillColor);
    }

    [TestMethod]
    public void Quadrant_ClassDef_AppliesToReferencingPoint()
    {
        // class2 is declared *after* Campaign E, so resolution must be deferred.
        var c = new MermaidQuadrantParser().Parse(StyledQuadrantSrc);
        var e = c.Points.Single(p => p.Label == "Campaign E").Style!;
        Assert.AreEqual("#908342", e.FillColor);
        Assert.AreEqual(10, e.Radius);           // "radius : 10" with spaces around the colon
        Assert.AreEqual("#310085", e.StrokeColor);
        Assert.AreEqual(10, e.StrokeWidth);
    }

    [TestMethod]
    public void Quadrant_InlineStyleWinsOverClass()
    {
        // Campaign B references class1 (color #109060) but sets color #ff3300 inline → inline wins.
        var c = new MermaidQuadrantParser().Parse(StyledQuadrantSrc);
        var b = c.Points.Single(p => p.Label == "Campaign B").Style!;
        Assert.AreEqual("#ff3300", b.FillColor);
        Assert.AreEqual(10, b.Radius);
    }

    [TestMethod]
    public void Quadrant_UnstyledPoint_HasNoStyle()
    {
        var c = new MermaidQuadrantParser().Parse("quadrantChart\n    P: [0.3, 0.6]\n");
        Assert.IsNull(c.Points[0].Style);
    }

    // ── Sequence diagram ──────────────────────────────────────────────────

    private const string SequenceSrc =
        """
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
            Alice-)John: See you later!
        """;

    [TestMethod]
    public void Sequence_AutoCreatesParticipantsInOrder()
    {
        var d = new MermaidSequenceParser().Parse(SequenceSrc);

        CollectionAssert.AreEqual(
            new[] { "Alice", "John" },
            d.Participants.Select(p => p.Id).ToArray());
    }

    [TestMethod]
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
    public void Sequence_SelfMessageHasMatchingEndpoints()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>Alice: thinking\n");

        Assert.AreEqual(1, d.Messages.Count);
        Assert.AreEqual(d.Messages[0].FromId, d.Messages[0].ToId);
    }

    [TestMethod]
    public void Sequence_SkipsControlFlowKeywords()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    loop Every minute\n    Alice->>John: ping\n    end\n");

        Assert.AreEqual(1, d.Messages.Count);
        Assert.AreEqual("ping", d.Messages[0].Text);
    }

    [TestMethod]
    public void Sequence_PlainArrowHasNoHead()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    A->B: note\n");
        Assert.AreEqual(SequenceArrowHead.None, d.Messages[0].Head);
        Assert.AreEqual(SequenceLineStyle.Solid, d.Messages[0].Line);
    }

    // ── Sequence: participant metadata, notes, activations, fragments ──────

    [TestMethod]
    public void Sequence_ParticipantTypeMetadata_SetsKind()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant DB@{ \"type\": \"database\" }\n    A->>DB: q\n");
        Assert.AreEqual(ParticipantKind.Database, d.Find("DB")!.Kind);
    }

    [TestMethod]
    public void Sequence_ActorKeyword_SetsActorKind()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    actor Alice\n    Alice->>Bob: hi\n");
        Assert.AreEqual(ParticipantKind.Actor, d.Find("Alice")!.Kind);
    }

    [TestMethod]
    public void Sequence_InlineAlias_BecomesLabel()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant API@{ \"type\": \"boundary\", \"alias\": \"Public API\" }\n    API->>API: x\n");
        Assert.AreEqual("Public API", d.Find("API")!.Label);
    }

    [TestMethod]
    public void Sequence_AsLabelWinsOverInlineAlias()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant API@{ \"alias\": \"Internal Name\" } as External Name\n    API->>API: x\n");
        Assert.AreEqual("External Name", d.Find("API")!.Label);
    }

    [TestMethod]
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
    public void Sequence_Destroy_MarksAndEmitsItem()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    A->>Carl: hi\n    destroy Carl\n    A-xCarl: bye\n");
        Assert.IsTrue(d.Find("Carl")!.Destroyed);
        Assert.AreEqual(1, d.Items.OfType<SequenceDestroy>().Count());
    }

    [TestMethod]
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
    public void Sequence_Autonumber_NumbersMessages()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    autonumber\n    A->>B: one\n    B->>A: two\n");
        Assert.AreEqual(1, d.Messages[0].Number);
        Assert.AreEqual(2, d.Messages[1].Number);
    }

    [TestMethod]
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
    public void Sequence_NoteRightOf()
    {
        var d = new MermaidSequenceParser().Parse("sequenceDiagram\n    Note right of John: hello\n");
        Assert.AreEqual(NotePlacement.RightOf, d.Items.OfType<SequenceNote>().Single().Placement);
    }

    [TestMethod]
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
    public void Sequence_NestedFragments_BalanceBeginAndEnd()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    par a\n    A->>B: x\n    par b\n    A->>B: y\n    end\n    end\n");
        var frags = d.Items.OfType<SequenceFragment>().ToList();
        Assert.AreEqual(2, frags.Count(f => f.Boundary == FragmentBoundary.Begin));
        Assert.AreEqual(2, frags.Count(f => f.Boundary == FragmentBoundary.End));
    }

    [TestMethod]
    public void Sequence_Box_GroupsParticipants()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    box Purple Group\n    participant A\n    participant J\n    end\n    A->>J: hi\n");
        var box = d.Boxes.Single();
        CollectionAssert.AreEqual(new[] { "A", "J" }, box.ParticipantIds.ToArray());
        Assert.AreEqual("Group", box.Label);   // leading colour word stripped
    }

    [TestMethod]
    public void Sequence_BrBecomesNewline()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    participant A as Alice<br/>Johnson\n    A->>A: x\n");
        StringAssert.Contains(d.Find("A")!.Label, "\n");
    }

    [TestMethod]
    public void Sequence_CentralConnectionMarkersStripped()
    {
        var d = new MermaidSequenceParser().Parse(
            "sequenceDiagram\n    Alice->>()John: hi\n    Alice()->>John: yo\n");
        CollectionAssert.AreEqual(new[] { "Alice", "John" }, d.Participants.Select(p => p.Id).ToArray());
    }

    [TestMethod]
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
        var g = new MermaidParser().Parse(
            "flowchart RL\n    A@{ shape: manual-file, label: \"File Handling\" }\n");
        var a = g.FindNode("A")!;
        Assert.AreEqual("File Handling", a.Label);
        Assert.AreEqual(NodeShape.TrapezoidAlt, a.Shape);
    }

    [TestMethod]
    public void Flowchart_ShapeAliases_MapToDocument()
    {
        var g = new MermaidParser().Parse("flowchart TD\n    A@{ shape: docs, label: \"Docs\" }\n");
        Assert.AreEqual(NodeShape.Document, g.FindNode("A")!.Shape);
    }

    [TestMethod]
    public void Flowchart_EdgeMetadata_IsNotANode()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    e1@{ curve: linear }\n    A-->B\n");
        Assert.IsNull(g.FindNode("e1"));
        Assert.AreEqual(2, g.Nodes.Count);
    }

    [TestMethod]
    public void Flowchart_MultidirectionArrows()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    A o--o B\n    B <--> C\n    C x--x D\n");
        var e = g.Edges;
        Assert.AreEqual(EdgeArrow.Circle, e[0].StartArrow); Assert.AreEqual(EdgeArrow.Circle, e[0].Arrow);
        Assert.AreEqual(EdgeArrow.Normal, e[1].StartArrow); Assert.AreEqual(EdgeArrow.Normal, e[1].Arrow);
        Assert.AreEqual(EdgeArrow.Cross,  e[2].StartArrow); Assert.AreEqual(EdgeArrow.Cross,  e[2].Arrow);
    }

    [TestMethod]
    public void Flowchart_ExtraDashes_ParseAsArrows()
    {
        var g = new MermaidParser().Parse("flowchart TD\n    A ----> B\n    C -- lbl ----> D\n");
        Assert.AreEqual(1, g.Edges.Count(e => e.SourceId == "A" && e.TargetId == "B"));
        var labelled = g.Edges.Single(e => e.SourceId == "C");
        Assert.AreEqual("D", labelled.TargetId);
        Assert.AreEqual("lbl", labelled.Label);
    }

    [TestMethod]
    public void Flowchart_InlineEdgeId_IsStripped()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    A e1@==> B\n");
        Assert.IsNull(g.FindNode("e1"));
        var e = g.Edges.Single();
        Assert.AreEqual("A", e.SourceId);
        Assert.AreEqual("B", e.TargetId);
        Assert.AreEqual(EdgeStyle.Thick, e.Style);
    }

    [TestMethod]
    public void Flowchart_HyphenIsArrowNotNodeId()
    {
        var g = new MermaidParser().Parse("flowchart TB\n    c1-->a2\n");
        Assert.IsNotNull(g.FindNode("c1"));
        Assert.IsNotNull(g.FindNode("a2"));
        Assert.IsNull(g.FindNode("c1--"));
        Assert.AreEqual(1, g.Edges.Count(e => e.SourceId == "c1" && e.TargetId == "a2"));
    }

    [TestMethod]
    public void Flowchart_DirectionKeyword_IsNotANode()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    subgraph S\n    direction TB\n    a-->b\n    end\n");
        Assert.IsNull(g.FindNode("direction"));
    }

    [TestMethod]
    public void Flowchart_StadiumAndCylinderShapes()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    A([Done]) --> B[(Store)]\n");
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
        var g = new MermaidParser().Parse("flowchart TD\n    A@{ shape: card, label: \"Note\" }\n");
        var a = g.FindNode("A")!;
        Assert.AreEqual(NodeShape.Card, a.Shape);
        Assert.AreEqual("Note", a.Label);
    }

    [TestMethod]
    public void Flowchart_Subgraph_TracksBothEndpointsAcrossArrow()
    {
        var g = new MermaidParser().Parse("flowchart TB\n    subgraph one\n    a1-->a2\n    end\n");
        var sg = g.Subgraphs.Single();
        CollectionAssert.Contains(sg.NodeIds, "a1");   // source was previously missed (followed by '-')
        CollectionAssert.Contains(sg.NodeIds, "a2");
    }

    [TestMethod]
    public void Flowchart_ChainedEdges_BecomeSeparateHops()
    {
        var g = new MermaidParser().Parse("flowchart LR\n    A --> TOP --> B\n");
        Assert.AreEqual(2, g.Edges.Count);
        Assert.IsTrue(g.Edges.Any(e => e is { SourceId: "A",   TargetId: "TOP" }));
        Assert.IsTrue(g.Edges.Any(e => e is { SourceId: "TOP", TargetId: "B" }));
        Assert.IsNotNull(g.FindNode("B"));   // the chain's tail node used to be dropped
    }

    [TestMethod]
    public void Flowchart_ChainCarriesArrowStyleAndLabel()
    {
        var g = new MermaidParser().Parse("flowchart TD\n    A -- yes --> B -.-> C\n");
        var ab = g.Edges.Single(e => e.SourceId == "A");
        Assert.AreEqual("yes", ab.Label);
        Assert.AreEqual(EdgeStyle.Solid, ab.Style);
        Assert.AreEqual(EdgeStyle.Dotted, g.Edges.Single(e => e.SourceId == "B").Style);
    }

    [TestMethod]
    public void Flowchart_FanOut_AndChainCompose()
    {
        var g = new MermaidParser().Parse("flowchart TD\n    A --> B & C --> D\n");
        foreach (var (s, t) in new[] { ("A", "B"), ("A", "C"), ("B", "D"), ("C", "D") })
            Assert.IsTrue(g.Edges.Any(e => e.SourceId == s && e.TargetId == t), $"missing {s}->{t}");
        Assert.AreEqual(4, g.Edges.Count);
    }

    [TestMethod]
    public void Flowchart_NestedSubgraphs_CarryParentLinks()
    {
        var g = new MermaidParser().Parse(
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

    // ── Gantt ─────────────────────────────────────────────────────────────

    private const string GanttSrc =
        """
        gantt
            title A Gantt Diagram
            dateFormat YYYY-MM-DD
            section Section
                A task          :a1, 2014-01-01, 30d
                Another task    :after a1, 20d
            section Another
                Task in Another :2014-01-12, 12d
                another task    :24d
        """;

    [TestMethod]
    public void Gantt_ParsesSectionsAndTasks()
    {
        var g = new MermaidGanttParser().Parse(GanttSrc);
        Assert.AreEqual("A Gantt Diagram", g.Title);
        Assert.AreEqual(2, g.Sections.Count);
        Assert.AreEqual(4, g.TaskCount);
    }

    [TestMethod]
    public void Gantt_ResolvesExplicitDateAndDuration()
    {
        var a = new MermaidGanttParser().Parse(GanttSrc).Tasks.Single(t => t.Name == "A task");
        Assert.AreEqual(new DateTime(2014, 1, 1),  a.Start);
        Assert.AreEqual(new DateTime(2014, 1, 31), a.End);   // + 30d
    }

    [TestMethod]
    public void Gantt_ResolvesAfterDependency()
    {
        var b = new MermaidGanttParser().Parse(GanttSrc).Tasks.Single(t => t.Name == "Another task");
        Assert.AreEqual(new DateTime(2014, 1, 31), b.Start); // after a1 (ends 1-31)
        Assert.AreEqual(new DateTime(2014, 2, 20), b.End);   // + 20d
    }

    [TestMethod]
    public void Gantt_ImplicitStartFollowsPreviousTask()
    {
        var t = new MermaidGanttParser().Parse(GanttSrc).Tasks.Single(x => x.Name == "another task");
        Assert.AreEqual(new DateTime(2014, 1, 24), t.Start); // after "Task in Another" (1-12 + 12d)
    }

    [TestMethod]
    public void Gantt_TagsAndMilestone()
    {
        var g = new MermaidGanttParser().Parse(
            "gantt\n  dateFormat YYYY-MM-DD\n  section S\n  a :done, 2024-01-01, 2d\n  b :active, 2024-01-03, 2d\n  c :crit, 2024-01-05, 2d\n  m :milestone, 2024-01-07, 0d\n");
        Assert.AreEqual(GanttTaskState.Done,   g.Tasks.First(t => t.Name == "a").State);
        Assert.AreEqual(GanttTaskState.Active, g.Tasks.First(t => t.Name == "b").State);
        Assert.IsTrue(g.Tasks.First(t => t.Name == "c").Critical);
        var m = g.Tasks.First(t => t.Name == "m");
        Assert.IsTrue(m.IsMilestone);
        Assert.AreEqual(m.Start, m.End);
    }

    [TestMethod]
    public void Gantt_WeekDurationUnit()
    {
        var t = new MermaidGanttParser().Parse(
            "gantt\n  dateFormat YYYY-MM-DD\n  section S\n  t :2024-01-01, 2w\n").Tasks.Single();
        Assert.AreEqual(new DateTime(2024, 1, 15), t.End);   // 2 weeks = 14 days
    }

    // ── Git graph ─────────────────────────────────────────────────────────

    private const string GitSrc =
        """
        gitGraph
           commit
           commit
           branch develop
           checkout develop
           commit
           commit
           checkout main
           merge develop
           commit
        """;

    [TestMethod]
    public void Git_BranchesAndCommitStream()
    {
        var g = new MermaidGitGraphParser().Parse(GitSrc);
        CollectionAssert.AreEquivalent(new[] { "main", "develop" }, g.Branches.Select(b => b.Name).ToArray());
        Assert.AreEqual(6, g.Commits.Count);                                  // 2 + 2 + merge + 1
        Assert.AreEqual(2, g.Commits.Count(c => c.Branch == "develop" && !c.IsMerge));
    }

    [TestMethod]
    public void Git_MergeCommitHasTwoParents()
    {
        var g = new MermaidGitGraphParser().Parse(GitSrc);
        var merge = g.Commits.Single(c => c.IsMerge);
        Assert.AreEqual("main", merge.Branch);
        Assert.AreEqual(2, merge.Parents.Count);
    }

    [TestMethod]
    public void Git_CommitOptions()
    {
        var g = new MermaidGitGraphParser().Parse(
            "gitGraph\n   commit id: \"a\" tag: \"v1.0\" type: HIGHLIGHT\n");
        var c = g.Commits.Single();
        Assert.AreEqual("a", c.Id);
        Assert.AreEqual("v1.0", c.Tag);
        Assert.AreEqual(GitCommitType.Highlight, c.Type);
        Assert.IsTrue(c.ShowLabel);
    }

    [TestMethod]
    public void Git_Orientation()
    {
        Assert.AreEqual(GitOrientation.TopBottom, new MermaidGitGraphParser().Parse("gitGraph TB:\n   commit\n").Orientation);
        Assert.AreEqual(GitOrientation.BottomTop, new MermaidGitGraphParser().Parse("gitGraph BT:\n   commit\n").Orientation);
        Assert.AreEqual(GitOrientation.LeftRight, new MermaidGitGraphParser().Parse("gitGraph\n   commit\n").Orientation);
    }

    [TestMethod]
    public void Git_CherryPickReferencesSource()
    {
        var g = new MermaidGitGraphParser().Parse(
            "gitGraph\n   commit id: \"a\"\n   branch dev\n   commit id: \"b\"\n   checkout main\n   cherry-pick id: \"b\"\n");
        var cp = g.Commits.Single(c => c.IsCherryPick);
        Assert.AreEqual("main", cp.Branch);
        CollectionAssert.Contains(cp.Parents, "b");
    }

    [TestMethod]
    public void Git_BranchOrderAssignsLanes()
    {
        var g = new MermaidGitGraphParser().Parse(
            "gitGraph\n   commit\n   branch z order: 1\n   branch a order: 5\n");
        // main(order 0 by creation) < z(1) < a(5)  →  lanes 0,1,2
        Assert.AreEqual(0, g.FindBranch("main")!.Lane);
        Assert.AreEqual(1, g.FindBranch("z")!.Lane);
        Assert.AreEqual(2, g.FindBranch("a")!.Lane);
    }

    // ── Front-matter ──────────────────────────────────────────────────────

    [TestMethod]
    public void Frontmatter_StripsBlockAndKeepsBody()
    {
        var (body, title) = MermaidFrontmatter.Strip("---\nconfig:\n  theme: forest\n---\npie\n  \"A\" : 1\n");
        StringAssert.StartsWith(body.TrimStart(), "pie");
        Assert.IsNull(title);
    }

    [TestMethod]
    public void Frontmatter_LiftsTopLevelTitle()
    {
        var (body, title) = MermaidFrontmatter.Strip("---\ntitle: My Chart\nconfig:\n  theme: dark\n---\npie\n");
        Assert.AreEqual("My Chart", title);
        StringAssert.StartsWith(body.TrimStart(), "pie");
    }

    [TestMethod]
    public void Frontmatter_IgnoresNestedTitle()
    {
        var (_, title) = MermaidFrontmatter.Strip("---\nconfig:\n  title: nested\n---\npie\n");
        Assert.IsNull(title);
    }

    [TestMethod]
    public void Frontmatter_NoBlock_PassesThrough()
    {
        var (body, title) = MermaidFrontmatter.Strip("pie\n  \"A\" : 1\n");
        Assert.AreEqual("pie\n  \"A\" : 1\n", body);
        Assert.IsNull(title);
    }

    [TestMethod]
    public void Frontmatter_Unterminated_PassesThrough()
    {
        const string src = "---\nconfig:\npie\n";
        var (body, title) = MermaidFrontmatter.Strip(src);
        Assert.AreEqual(src, body);
        Assert.IsNull(title);
    }

    // ── Mindmap ───────────────────────────────────────────────────────────

    private const string MindmapSrc =
        """
        mindmap
          root((mindmap))
            Origins
              Long history
              ::icon(fa fa-book)
              Popularisation
                Tony Buzan
            Research
              On effectiveness<br/>and features
            Tools
              Mermaid
        """;

    [TestMethod]
    public void Mindmap_IndentationBuildsHierarchy()
    {
        var m = new MermaidMindmapParser().Parse(MindmapSrc);
        Assert.AreEqual("mindmap", m.Root!.Text);
        CollectionAssert.AreEqual(new[] { "Origins", "Research", "Tools" }, m.Root.Children.Select(c => c.Text).ToArray());

        var origins = m.Root.Children[0];
        var pop = origins.Children.Single(c => c.Text == "Popularisation");
        Assert.AreEqual(1, origins.Depth);
        Assert.AreEqual(2, pop.Depth);
        Assert.AreEqual("Tony Buzan", pop.Children.Single().Text);
        Assert.AreEqual(3, pop.Children.Single().Depth);
    }

    [TestMethod]
    public void Mindmap_IconLineIsSkipped()
    {
        var m = new MermaidMindmapParser().Parse(MindmapSrc);
        Assert.IsFalse(m.All().Any(n => n.Text.Contains("icon", StringComparison.OrdinalIgnoreCase)));
        // "Long history" is a leaf; the icon line did not become its child.
        Assert.AreEqual(0, m.Root!.Children[0].Children.Single(c => c.Text == "Long history").Children.Count);
    }

    [TestMethod]
    public void Mindmap_BranchIndexInheritedFromTopLevel()
    {
        var m = new MermaidMindmapParser().Parse(MindmapSrc);
        var research = m.Root!.Children[1];
        Assert.AreEqual(1, research.BranchIndex);                 // 2nd top-level branch
        Assert.AreEqual(1, research.Children.Single().BranchIndex); // inherited
        Assert.AreEqual(-1, m.Root.BranchIndex);                  // root is neutral
    }

    [TestMethod]
    public void Mindmap_BrBecomesNewline()
    {
        var m = new MermaidMindmapParser().Parse(MindmapSrc);
        var eff = m.Root!.Children[1].Children.Single();
        StringAssert.Contains(eff.Text, "\n");
    }

    [TestMethod]
    public void Mindmap_Shapes()
    {
        var m = new MermaidMindmapParser().Parse(
            "mindmap\n  id1[Root]\n    a(Rounded)\n    b((Circle))\n    c{{Hex}}\n    d)Cloud(\n    e))Bang((\n");
        Assert.AreEqual(MindmapShape.Square, m.Root!.Shape);
        Assert.AreEqual("Root", m.Root.Text);
        var byText = m.Root.Children.ToDictionary(c => c.Text, c => c.Shape);
        Assert.AreEqual(MindmapShape.Rounded, byText["Rounded"]);
        Assert.AreEqual(MindmapShape.Circle,  byText["Circle"]);
        Assert.AreEqual(MindmapShape.Hexagon, byText["Hex"]);
        Assert.AreEqual(MindmapShape.Cloud,   byText["Cloud"]);
        Assert.AreEqual(MindmapShape.Bang,    byText["Bang"]);
    }

    // ── State diagram ──────────────────────────────────────────────────────

    private static Graph State(string src) => new MermaidStateParser().Parse(src);

    [TestMethod]
    public void State_SimpleSample_StatesEdgesAndPseudostates()
    {
        var g = State(
            """
            stateDiagram-v2
                [*] --> Still
                Still --> [*]
                Still --> Moving
                Moving --> Still
                Moving --> Crash
                Crash --> [*]
            """);

        // Still, Moving, Crash + one shared start + one shared end.
        Assert.AreEqual(1, g.Nodes.Count(n => n.Shape == NodeShape.StateStart));
        Assert.AreEqual(1, g.Nodes.Count(n => n.Shape == NodeShape.StateEnd));
        foreach (var id in new[] { "Still", "Moving", "Crash" })
            Assert.IsNotNull(g.FindNode(id), $"missing state {id}");
        Assert.AreEqual(6, g.Edges.Count);
    }

    [TestMethod]
    public void State_Descriptions_BothForms()
    {
        var g1 = State("stateDiagram-v2\n    state \"This is a state description\" as s2");
        Assert.AreEqual("This is a state description", g1.FindNode("s2")!.Label);

        var g2 = State("stateDiagram-v2\n    s2 : This is a state description");
        Assert.AreEqual("This is a state description", g2.FindNode("s2")!.Label);
    }

    [TestMethod]
    public void State_TransitionLabel()
    {
        var g = State("stateDiagram-v2\n    s1 --> s2: A transition");
        var e = g.Edges.Single();
        Assert.AreEqual("s1", e.SourceId);
        Assert.AreEqual("s2", e.TargetId);
        Assert.AreEqual("A transition", e.Label);
    }

    [TestMethod]
    public void State_Choice_IsDiamond_WithLabelledBranches()
    {
        var g = State(
            """
            stateDiagram-v2
                state if_state <<choice>>
                [*] --> IsPositive
                IsPositive --> if_state
                if_state --> False: if n < 0
                if_state --> True : if n >= 0
            """);

        Assert.AreEqual(NodeShape.Diamond, g.FindNode("if_state")!.Shape);
        Assert.IsTrue(g.Edges.Any(e => e is { TargetId: "False", Label: "if n < 0" }));
        Assert.IsTrue(g.Edges.Any(e => e is { TargetId: "True",  Label: "if n >= 0" }));
    }

    [TestMethod]
    public void State_ForkAndJoin_AreBars()
    {
        var g = State(
            """
            stateDiagram-v2
                state fork_state <<fork>>
                state join_state <<join>>
                [*] --> fork_state
                fork_state --> State2
                State2 --> join_state
            """);

        Assert.AreEqual(NodeShape.ForkJoin, g.FindNode("fork_state")!.Shape);
        Assert.AreEqual(NodeShape.ForkJoin, g.FindNode("join_state")!.Shape);
    }

    [TestMethod]
    public void State_CompositeBecomesSubgraphWithMembers()
    {
        var g = State(
            """
            stateDiagram-v2
                [*] --> First
                state First {
                    [*] --> second
                    second --> [*]
                }
            """);

        var sg = g.Subgraphs.SingleOrDefault(s => s.Id == "First");
        Assert.IsNotNull(sg, "composite First should become a subgraph");
        Assert.IsTrue(sg!.NodeIds.Contains("second"), "second should be a member of First");
        // An edge connects the root start to the composite box.
        Assert.IsTrue(g.Edges.Any(e => e.TargetId == "First"));
    }

    [TestMethod]
    public void State_NestedComposites_CarryParentLinks()
    {
        var g = State(
            """
            stateDiagram-v2
                [*] --> First
                state First {
                    [*] --> Second
                    state Second {
                        [*] --> second
                        second --> Third
                        state Third {
                            [*] --> third
                            third --> [*]
                        }
                    }
                }
            """);

        Subgraph Sg(string id) => g.Subgraphs.Single(s => s.Id == id);
        Assert.IsNull(Sg("First").ParentId, "First is top level");
        Assert.AreEqual("First",  Sg("Second").ParentId);
        Assert.AreEqual("Second", Sg("Third").ParentId);
        // The deepest state lives in the innermost composite only.
        Assert.IsTrue(Sg("Third").NodeIds.Contains("third"));
        Assert.IsFalse(Sg("First").NodeIds.Contains("third"), "membership is innermost-only");
    }

    [TestMethod]
    public void State_NamedComposite_TakesItsDescriptionAsLabel()
    {
        var g = State(
            """
            stateDiagram-v2
                [*] --> NamedComposite
                NamedComposite: Another Composite
                state NamedComposite {
                    [*] --> namedSimple
                    namedSimple --> [*]
                }
            """);

        Assert.AreEqual("Another Composite", g.Subgraphs.Single(s => s.Id == "NamedComposite").Label);
    }

    [TestMethod]
    public void State_Note_AddsDottedArrowlessEdgeToNoteNode()
    {
        var g = State(
            """
            stateDiagram-v2
                State1: The state with a note
                note right of State1
                    Important information! You can write
                    notes.
                end note
                State1 --> State2
                note left of State2 : This is the note to the left.
            """);

        var notes = g.Nodes.Where(n => n.Shape == NodeShape.Note).ToList();
        Assert.AreEqual(2, notes.Count, "two notes expected");
        StringAssert.Contains(notes[0].Label, "Important information");
        var noteEdge = g.Edges.Single(e => e.TargetId == notes[0].Id);
        Assert.AreEqual(EdgeArrow.None, noteEdge.Arrow);
        Assert.AreEqual(EdgeStyle.Dotted, noteEdge.Style);
    }

    [TestMethod]
    public void State_Styling_ClassDefAndInlineOperatorApplyFill()
    {
        var classed = State(
            """
            stateDiagram
                classDef notMoving fill:white
                classDef badBadEvent fill:#f00,color:white,stroke:yellow
                [*] --> Still
                Still --> Moving
                Moving --> Crash
                class Still notMoving
                class Crash badBadEvent
            """);
        Assert.AreEqual("white", classed.FindNode("Still")!.FillColor);
        Assert.AreEqual("#f00",  classed.FindNode("Crash")!.FillColor);
        Assert.AreEqual("yellow", classed.FindNode("Crash")!.StrokeColor);

        var inline = State(
            """
            stateDiagram
                classDef notMoving fill:white
                [*] --> Still:::notMoving
            """);
        Assert.AreEqual("white", inline.FindNode("Still")!.FillColor);
    }

    [TestMethod]
    public void State_Direction_IsParsed()
    {
        var g = State("stateDiagram\n    direction LR\n    [*] --> A\n    A --> B");
        Assert.AreEqual(GraphDirection.LeftRight, g.Direction);
    }

    [TestMethod]
    public void State_CommentsAreStripped()
    {
        var g = State(
            """
            stateDiagram-v2
                [*] --> Still
            %% this is a comment
                Moving --> Still %% another comment
            """);
        // The trailing comment must not leak into the target id.
        Assert.IsNotNull(g.FindNode("Still"));
        Assert.IsNotNull(g.FindNode("Moving"));
        Assert.IsFalse(g.Nodes.Any(n => n.Id.Contains('%')), "comment markers must be stripped");
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

    // ── Kanban board ───────────────────────────────────────────────────────

    private const string KanbanSrc =
        """
        kanban
          Todo
            [Create Documentation]
            docs[Create Blog about the new diagram]
          id9[Ready for deploy]
            id8[Design grammar]@{ assigned: 'knsv' }
          id10[Ready for test]
            id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
            id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }
          id11[Done]
            id3[Update DB function]@{ ticket: MC-2037, assigned: knsv, priority: 'Very High' }

          id12[Can't reproduce]
            id5[Weird flickering in Firefox]
        """;

    private static KanbanBoard Kanban(string src) => new MermaidKanbanParser().Parse(src);

    [TestMethod]
    public void Kanban_IndentationGroupsCardsUnderColumns()
    {
        var b = Kanban(KanbanSrc);
        CollectionAssert.AreEqual(
            new[] { "Todo", "Ready for deploy", "Ready for test", "Done", "Can't reproduce" },
            b.Columns.Select(c => c.Title).ToArray());
        Assert.AreEqual(2, b.Columns[0].Items.Count);                 // Todo
        Assert.AreEqual(2, b.Columns[2].Items.Count);                 // Ready for test
        Assert.AreEqual(1, b.Columns[4].Items.Count);                 // Can't reproduce (after a blank line)
    }

    [TestMethod]
    public void Kanban_NodeForms_BareBracketedAndWithId()
    {
        var b = Kanban(KanbanSrc);
        // Bare column → id and title are the same.
        Assert.AreEqual("Todo", b.Columns[0].Id);
        Assert.AreEqual("Todo", b.Columns[0].Title);
        // id[Title] column.
        Assert.AreEqual("id9", b.Columns[1].Id);
        Assert.AreEqual("Ready for deploy", b.Columns[1].Title);
        // [Title] card (no id) → id falls back to the title; title is the bracket text.
        var bareCard = b.Columns[0].Items[0];
        Assert.AreEqual("Create Documentation", bareCard.Text);
        // id[Title] card.
        var idCard = b.Columns[0].Items[1];
        Assert.AreEqual("docs", idCard.Id);
        Assert.AreEqual("Create Blog about the new diagram", idCard.Text);
    }

    [TestMethod]
    public void Kanban_Metadata_TicketAssignedPriority()
    {
        var b = Kanban(KanbanSrc);
        var card = b.Columns[2].Items.Single(i => i.Text == "Create parsing tests");
        Assert.AreEqual("MC-2038", card.Ticket);
        Assert.AreEqual("K.Sveidqvist", card.Assigned);
        Assert.AreEqual(KanbanPriority.High, card.Priority);
    }

    [TestMethod]
    public void Kanban_Metadata_AttachedDirectlyToBracket()
    {
        // "id8[Design grammar]@{ assigned: 'knsv' }" — no space before @{.
        var card = Kanban(KanbanSrc).Columns[1].Items.Single();
        Assert.AreEqual("Design grammar", card.Text);
        Assert.AreEqual("knsv", card.Assigned);
        Assert.IsNull(card.Ticket);
        Assert.AreEqual(KanbanPriority.None, card.Priority);
    }

    [TestMethod]
    public void Kanban_Priority_AllForms()
    {
        var b = Kanban(KanbanSrc);
        Assert.AreEqual(KanbanPriority.VeryLow,  b.Columns[2].Items.Single(i => i.Text == "last item").Priority);
        Assert.AreEqual(KanbanPriority.VeryHigh, b.Columns[3].Items.Single().Priority);
        // Unquoted metadata value parses too (assigned: knsv).
        Assert.AreEqual("knsv", b.Columns[3].Items.Single().Assigned);
    }

    [TestMethod]
    public void Kanban_BareCard_HasNoMetadata()
    {
        var card = Kanban(KanbanSrc).Columns[0].Items[0];
        Assert.IsNull(card.Assigned);
        Assert.IsNull(card.Ticket);
        Assert.AreEqual(KanbanPriority.None, card.Priority);
    }

    [TestMethod]
    public void Kanban_EmptyColumn_KeepsZeroCards()
    {
        var b = Kanban("kanban\n  Backlog\n    a[one]\n  Doing\n  Done\n");
        Assert.AreEqual(3, b.Columns.Count);
        Assert.AreEqual(1, b.Columns[0].Items.Count);
        Assert.AreEqual(0, b.Columns[1].Items.Count);   // Doing — no cards
        Assert.AreEqual(0, b.Columns[2].Items.Count);   // Done — last, no cards
    }

    [TestMethod]
    public void Kanban_CommentsAreStripped()
    {
        var b = Kanban("kanban\n  Todo\n    a[task] %% trailing comment\n  %% whole-line comment\n  Done\n");
        CollectionAssert.AreEqual(new[] { "Todo", "Done" }, b.Columns.Select(c => c.Title).ToArray());
        Assert.AreEqual("task", b.Columns[0].Items.Single().Text);
    }
}
