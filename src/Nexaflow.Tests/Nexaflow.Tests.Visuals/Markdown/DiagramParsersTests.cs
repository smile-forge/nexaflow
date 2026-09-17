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

    // ── Sankey — parser ───────────────────────────────────────────────────

    [TestMethod]
    public void Sankey_InfersNodesAndLinks()
    {
        var d = new MermaidSankeyParser().Parse(
            "sankey\n\nA,B,10\nA,C,5\nB,C,3\n");

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, d.Nodes.Select(x => x.Name).ToArray());
        Assert.AreEqual(3, d.Links.Count);
        Assert.AreEqual(0, d.Links[0].Source);   // A
        Assert.AreEqual(1, d.Links[0].Target);   // B
        Assert.AreEqual(10, d.Links[0].Value, 1e-9);
    }

    [TestMethod]
    public void Sankey_QuotedFieldsAndDoubledQuotes()
    {
        var d = new MermaidSankeyParser().Parse(
            "sankey\nPumped heat,\"Heating and cooling, homes\",193.026\nPumped heat,\"Heating and cooling, \"\"commercial\"\"\",70.672\n");

        Assert.AreEqual("Heating and cooling, homes", d.Nodes[1].Name);            // comma inside quotes
        Assert.AreEqual("Heating and cooling, \"commercial\"", d.Nodes[2].Name);    // doubled "" → literal "
        Assert.AreEqual(193.026, d.Links[0].Value, 1e-9);
    }

    [TestMethod]
    public void Sankey_SkipsCommentsAndBlankLines()
    {
        var d = new MermaidSankeyParser().Parse(
            "sankey\n\n%% source,target,value\nA,B,1\n\nB,C,2\n");
        Assert.AreEqual(3, d.Nodes.Count);
        Assert.AreEqual(2, d.Links.Count);
    }

    [TestMethod]
    public void Sankey_NodeUsedAsSourceAndTarget_IsOneNode()
    {
        var d = new MermaidSankeyParser().Parse("sankey\nA,B,1\nB,C,2\n");
        Assert.AreEqual(3, d.Nodes.Count);                 // A, B, C — B shared
        Assert.AreEqual(1, d.Links[0].Target);             // B
        Assert.AreEqual(1, d.Links[1].Source);             // B again
    }

    // ── Sankey — config ───────────────────────────────────────────────────

    [TestMethod]
    public void SankeyConfig_ParsesEnumsAndFlags()
    {
        var cfg = SankeyConfigParser.Parse(
            """
            config:
              sankey:
                showValues: false
                linkColor: source
                nodeAlignment: left
                nodeWidth: 15
                nodePadding: 20
                labelStyle: outlined
                suffix: " TWh"
            """);
        Assert.IsFalse(cfg.ShowValues);
        Assert.AreEqual(SankeyLinkColor.Source, cfg.LinkColor);
        Assert.AreEqual(SankeyNodeAlignment.Left, cfg.NodeAlignment);
        Assert.AreEqual(15, cfg.NodeWidth, 1e-9);
        Assert.AreEqual(20, cfg.NodePadding, 1e-9);
        Assert.AreEqual(SankeyLabelStyle.Outlined, cfg.LabelStyle);
        Assert.AreEqual(" TWh", cfg.Suffix);
    }

    [TestMethod]
    public void SankeyConfig_LinkColorHex_IsCustom()
    {
        var cfg = SankeyConfigParser.Parse("config:\n  sankey:\n    linkColor: \"#a1a1a1\"\n");
        Assert.AreEqual(SankeyLinkColor.Custom, cfg.LinkColor);
        Assert.IsNotNull(cfg.LinkColorCustom);
    }

    [TestMethod]
    public void SankeyConfig_ParsesNodeColorsMap()
    {
        var cfg = SankeyConfigParser.Parse(
            """
            config:
              sankey:
                nodeColors:
                  Electricity grid: "#4e79a7"
                  Industry: "#e15759"
            """);
        Assert.AreEqual(2, cfg.NodeColors.Count);
        Assert.IsTrue(cfg.NodeColors.ContainsKey("Electricity grid"));
        var industry = ((System.Windows.Media.SolidColorBrush)cfg.NodeColors["Industry"]).Color;
        Assert.AreEqual(0xE1, industry.R);
        Assert.AreEqual(0x57, industry.G);
        Assert.AreEqual(0x59, industry.B);
    }

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

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesGroupsServicesIconsAndMembership()
    {
        var d = new MermaidArchitectureParser().Parse(
            """
            architecture-beta
                group api(cloud)[Public API]
                service db(database)[My Database] in api
                service plain
            """);

        var g = d.FindGroup("api")!;
        Assert.AreEqual("cloud", g.Icon);
        Assert.AreEqual("Public API", g.Title);

        var db = d.FindService("db")!;
        Assert.AreEqual("database", db.Icon);
        Assert.AreEqual("My Database", db.Title);
        Assert.AreEqual("api", db.GroupId);

        var plain = d.FindService("plain")!;
        Assert.IsNull(plain.Icon);
        Assert.AreEqual(string.Empty, plain.Title);
        Assert.IsNull(plain.GroupId);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesNestedGroups()
    {
        var d = new MermaidArchitectureParser().Parse(
            "architecture-beta\n  group public(cloud)[Public]\n  group private(cloud)[Private] in public\n");

        Assert.AreEqual("public", d.FindGroup("private")!.ParentId);
        Assert.IsNull(d.FindGroup("public")!.ParentId);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesEdgeSidesAndArrowForms()
    {
        var d = new MermaidArchitectureParser().Parse(
            """
            architecture-beta
                service a(server)[A]
                service b(server)[B]
                service c(server)[C]
                a:R -- L:b
                a:B --> T:c
                b:R <-- L:c
                a:T <--> B:c
            """);

        Assert.AreEqual(4, d.Edges.Count);

        var e0 = d.Edges[0];
        Assert.AreEqual("a", e0.FromId);
        Assert.AreEqual(ArchSide.Right, e0.FromSide);
        Assert.AreEqual(ArchSide.Left, e0.ToSide);
        Assert.IsFalse(e0.StartArrow);
        Assert.IsFalse(e0.EndArrow);

        Assert.IsTrue(d.Edges[1].EndArrow);
        Assert.IsFalse(d.Edges[1].StartArrow);

        Assert.IsTrue(d.Edges[2].StartArrow);
        Assert.IsFalse(d.Edges[2].EndArrow);

        Assert.IsTrue(d.Edges[3].StartArrow);
        Assert.IsTrue(d.Edges[3].EndArrow);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesCrossGroupEdges()
    {
        var d = new MermaidArchitectureParser().Parse(
            """
            architecture-beta
                group g1(cloud)[G1]
                group g2(cloud)[G2]
                service a(server)[A] in g1
                service b(server)[B] in g2
                a{group}:R --> L:b{group}
            """);

        var e = d.Edges.Single();
        Assert.IsTrue(e.FromIsGroup);
        Assert.IsTrue(e.ToIsGroup);
        Assert.AreEqual("a", e.FromId);
        Assert.AreEqual("b", e.ToId);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesJunction()
    {
        var d = new MermaidArchitectureParser().Parse(
            "architecture-beta\n  group g(cloud)[G]\n  junction j1 in g\n");

        var j = d.FindService("j1")!;
        Assert.IsTrue(j.IsJunction);
        Assert.AreEqual("g", j.GroupId);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesAlignmentRowAndColumn()
    {
        var d = new MermaidArchitectureParser().Parse(
            """
            architecture-beta
                service a(server)[A]
                service b(server)[B]
                service c(server)[C]
                align row a b c
                align column a b
            """);

        Assert.AreEqual(2, d.Alignments.Count);
        Assert.IsTrue(d.Alignments[0].IsRow);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, d.Alignments[0].Ids.ToArray());
        Assert.IsFalse(d.Alignments[1].IsRow);
        CollectionAssert.AreEqual(new[] { "a", "b" }, d.Alignments[1].Ids.ToArray());
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_ParsesCustomIconPack()
    {
        var d = new MermaidArchitectureParser().Parse(
            "architecture-beta\n  service q(aws:lambda)[Fn]\n");
        Assert.AreEqual("aws:lambda", d.FindService("q")!.Icon);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_CommentsAndBlankLinesTolerated()
    {
        var d = new MermaidArchitectureParser().Parse(
            """
            architecture-beta
                %% a comment

                service a(server)[A]
                service b(server)[B]
                a:R -- L:b   %% trailing comment
            """);
        Assert.AreEqual(2, d.Services.Count);
        Assert.AreEqual(1, d.Edges.Count);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void Architecture_EmptyOrMalformed_ReturnsEmptyWithoutThrowing()
    {
        Assert.AreEqual(0, new MermaidArchitectureParser().Parse("architecture-beta\n").Services.Count);
        Assert.AreEqual(0, new MermaidArchitectureParser().Parse("architecture-beta\n  service\n  ???\n").Edges.Count);
    }

    [TestMethod]
    [CoversNode("architecture")]
    public void ArchitectureConfig_ParsesKeys()
    {
        var cfg = ArchitectureConfigParser.Parse(
            """
            config:
              architecture:
                nodeSeparation: 60
                seed: 7
            """);
        Assert.AreEqual(60, cfg.NodeSeparation, 1e-9);
        Assert.AreEqual(7, cfg.Seed);
    }

    // ── Cynefin diagram — parser ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_ParsesTitleAndDomainItems()
    {
        var d = new MermaidCynefinParser().Parse(
            """
            cynefin-beta
                title Making sense
                complex
                    "Investigate root cause"
                    "Run experiment"
                clear
                    "Apply best practice"
            """);

        Assert.AreEqual("Making sense", d.Title);
        CollectionAssert.AreEqual(
            new[] { "Investigate root cause", "Run experiment" },
            d.ItemsIn(CynefinDomain.Complex).Select(i => i.Text).ToArray());
        Assert.AreEqual("Apply best practice", d.ItemsIn(CynefinDomain.Clear).Single().Text);
        Assert.AreEqual(0, d.ItemsIn(CynefinDomain.Chaotic).Count);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_RecognisesAllFiveDomains()
    {
        var d = new MermaidCynefinParser().Parse(
            "cynefin-beta\ncomplex\n \"a\"\ncomplicated\n \"b\"\nclear\n \"c\"\nchaotic\n \"d\"\nconfusion\n \"e\"\n");

        Assert.AreEqual("a", d.ItemsIn(CynefinDomain.Complex).Single().Text);
        Assert.AreEqual("b", d.ItemsIn(CynefinDomain.Complicated).Single().Text);
        Assert.AreEqual("c", d.ItemsIn(CynefinDomain.Clear).Single().Text);
        Assert.AreEqual("d", d.ItemsIn(CynefinDomain.Chaotic).Single().Text);
        Assert.AreEqual("e", d.ItemsIn(CynefinDomain.Confusion).Single().Text);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_UnknownDomainKeywordIsNotADomain()
    {
        // An unknown keyword before any domain means its "items" have nowhere to go.
        var d = new MermaidCynefinParser().Parse(
            "cynefin-beta\n  disorder\n    \"orphan item\"\n  clear\n    \"kept\"\n");
        Assert.AreEqual("kept", d.ItemsIn(CynefinDomain.Clear).Single().Text);
        Assert.AreEqual(0, d.ItemsIn(CynefinDomain.Confusion).Count);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_ConfusionOverflowCounted()
    {
        var d = new MermaidCynefinParser().Parse(
            "cynefin-beta\nconfusion\n \"a\"\n \"b\"\n \"c\"\n \"d\"\n \"e\"\n");
        Assert.AreEqual(5, d.ItemsIn(CynefinDomain.Confusion).Count);
        Assert.AreEqual(5 - CynefinDiagram.ConfusionMaxBadges, d.ConfusionOverflow);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_ParsesTransitionsWithAndWithoutLabel()
    {
        var d = new MermaidCynefinParser().Parse(
            """
            cynefin-beta
                complex --> complicated : "Pattern found"
                chaotic --> complex
            """);

        Assert.AreEqual(2, d.Transitions.Count);
        Assert.AreEqual(CynefinDomain.Complex, d.Transitions[0].From);
        Assert.AreEqual(CynefinDomain.Complicated, d.Transitions[0].To);
        Assert.AreEqual("Pattern found", d.Transitions[0].Label);
        Assert.AreEqual(CynefinDomain.Chaotic, d.Transitions[1].From);
        Assert.AreEqual(string.Empty, d.Transitions[1].Label);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_CommentsAndWhitespaceTolerated()
    {
        var d = new MermaidCynefinParser().Parse(
            "cynefin-beta\n%% a comment\n  complex\n    \"item\"  %% trailing\n");
        Assert.AreEqual("item", d.ItemsIn(CynefinDomain.Complex).Single().Text);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void Cynefin_EmptyDiagram_ReturnsEmptyWithoutThrowing()
    {
        var d = new MermaidCynefinParser().Parse("cynefin-beta\n");
        Assert.AreEqual(0, d.Transitions.Count);
        Assert.AreEqual(0, d.ItemsIn(CynefinDomain.Complex).Count);
    }

    [TestMethod]
    [CoversNode("cynefin")]
    public void CynefinConfig_ParsesOptionsAndThemeColours()
    {
        var cfg = CynefinConfigParser.Parse(
            """
            config:
              cynefin:
                width: 600
                showDomainDescriptions: true
              themeVariables:
                cynefin:
                  complexBg: "#4e79a7"
                  clearBg: "#59a14f"
            """);
        Assert.AreEqual(600, cfg.Width, 1e-9);
        Assert.IsTrue(cfg.ShowDomainDescriptions);
        Assert.IsNotNull(cfg.ComplexBg);
        Assert.IsNotNull(cfg.ClearBg);
        Assert.IsNull(cfg.ChaoticBg);
    }

    // ── Swimlane diagram — parser ─────────────────────────────────────────

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
    public void Swimlane_DefaultDirectionIsTopDown()
    {
        var g = new MermaidSwimlaneParser().Parse("swimlane-beta\n  subgraph a[A]\n    n1[N]\n  end\n");
        Assert.AreEqual(GraphDirection.TopDown, g.Direction);
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_ExplicitDirectionParsed()
    {
        Assert.AreEqual(GraphDirection.LeftRight,
            new MermaidSwimlaneParser().Parse("swimlane-beta LR\n  n1[N]\n").Direction);
        Assert.AreEqual(GraphDirection.RightLeft,
            new MermaidSwimlaneParser().Parse("swimlane-beta RL\n  n1[N]\n").Direction);
        Assert.AreEqual(GraphDirection.BottomUp,
            new MermaidSwimlaneParser().Parse("swimlane-beta BT\n  n1[N]\n").Direction);
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_TopLevelSubgraphsAreLanes()
    {
        var g = new MermaidSwimlaneParser().Parse(SwimlaneSrc);
        var lanes = g.Subgraphs.Where(s => s.ParentId is null).ToList();
        Assert.AreEqual(2, lanes.Count);

        var customer = lanes.Single(l => l.Id == "customer");
        Assert.AreEqual("Customer", customer.Label);
        CollectionAssert.IsSubsetOf(new[] { "start", "pay" }, customer.NodeIds.ToArray());
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_ParsesFlowchartNodeShapes()
    {
        var g = new MermaidSwimlaneParser().Parse(
            "swimlane-beta\n  a[Rect]\n  b(Round)\n  c([Stadium])\n  d{Decision}\n  e((Circle))\n");
        Assert.AreEqual(NodeShape.Rectangle, g.FindNode("a")!.Shape);
        Assert.AreEqual(NodeShape.RoundedRect, g.FindNode("b")!.Shape);
        Assert.AreEqual(NodeShape.Stadium, g.FindNode("c")!.Shape);
        Assert.AreEqual(NodeShape.Diamond, g.FindNode("d")!.Shape);
        Assert.AreEqual(NodeShape.Circle, g.FindNode("e")!.Shape);
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_ParsesEdgeStylesAndLabels()
    {
        var g = new MermaidSwimlaneParser().Parse(SwimlaneSrc);

        var labelled = g.Edges.Single(e => e.SourceId == "pick" && e.TargetId == "ship");
        Assert.AreEqual("Yes", labelled.Label);

        var dotted = g.Edges.Single(e => e.SourceId == "pick" && e.TargetId == "pay");
        Assert.AreEqual(EdgeStyle.Dotted, dotted.Style);
        Assert.AreEqual("No", dotted.Label);
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_ThickEdgeParsed()
    {
        var g = new MermaidSwimlaneParser().Parse("swimlane-beta LR\n  a[A]\n  b[B]\n  a ==> b\n");
        Assert.AreEqual(EdgeStyle.Thick, g.Edges.Single().Style);
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_CrossLaneEdgeConnectsNodesInDifferentLanes()
    {
        var g = new MermaidSwimlaneParser().Parse(SwimlaneSrc);
        // pay (customer lane) --> pick (fulfilment lane)
        Assert.IsTrue(g.Edges.Any(e => e.SourceId == "pay" && e.TargetId == "pick"));
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_AccessibilityLinesAreNotNodes()
    {
        var g = new MermaidSwimlaneParser().Parse(
            "swimlane-beta\n  accTitle: Order flow\n  accDescr: How an order flows\n  a[A]\n");
        Assert.IsNull(g.FindNode("accTitle"));
        Assert.IsNull(g.FindNode("accDescr"));
        Assert.IsNotNull(g.FindNode("a"));
    }

    [TestMethod]
    [CoversNode("swimlanes")]
    public void Swimlane_EmptyDiagram_ReturnsEmptyWithoutThrowing()
    {
        var g = new MermaidSwimlaneParser().Parse("swimlane-beta\n");
        Assert.AreEqual(0, g.Nodes.Count);
        Assert.AreEqual(0, g.Edges.Count);
    }

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

    // ── Timeline — parser ─────────────────────────────────────────────────

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
    public void Timeline_ParsesTitlePeriodsAndEvents()
    {
        var d = new MermaidTimelineParser().Parse("timeline\n  title T\n  2004 : Facebook : Google\n  2005 : YouTube\n");
        Assert.AreEqual("T", d.Title);
        Assert.AreEqual(2, d.PeriodCount);
        var p = d.Periods.First();
        Assert.AreEqual("2004", p.Title);
        CollectionAssert.AreEqual(new[] { "Facebook", "Google" }, p.Events);
        Assert.AreEqual(TimelineDirection.LeftToRight, d.Direction);
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_ContinuationColonLinesAppendToLastPeriod()
    {
        var d = new MermaidTimelineParser().Parse(TimelineSrc);
        var p = d.Periods.Single(x => x.Title == "2004");
        CollectionAssert.AreEqual(new[] { "Facebook", "Google" }, p.Events);
        Assert.AreEqual(4, d.PeriodCount);
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_SectionsGroupPeriods_AndHasSections()
    {
        var d = new MermaidTimelineParser().Parse(
            """
            timeline
                title Industrial ages
                section Stone Age
                    7000 BC : Stone tools
                    2500 BC : Copper
                section Bronze Age
                    2000 BC : Bronze tools
            """);
        Assert.IsTrue(d.HasSections);
        Assert.AreEqual(2, d.Sections.Count);
        Assert.AreEqual("Stone Age", d.Sections[0].Name);
        Assert.AreEqual(2, d.Sections[0].Periods.Count);
        Assert.AreEqual("Bronze Age", d.Sections[1].Name);
        Assert.AreEqual("2000 BC", d.Sections[1].Periods.Single().Title);

        var flat = new MermaidTimelineParser().Parse(TimelineSrc);
        Assert.IsFalse(flat.HasSections);
        Assert.AreEqual(1, flat.Sections.Count);
        Assert.AreEqual(string.Empty, flat.Sections[0].Name);
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_BrBecomesLineBreak_AndCommentsStripped()
    {
        var d = new MermaidTimelineParser().Parse(
            "timeline\n%% a comment\n  2010 : Instagram<br>launched : Pinterest %% trailing\n  2011 : Time#colon; 10#colon;30\n");
        CollectionAssert.AreEqual(new[] { "Instagram\nlaunched", "Pinterest" }, d.Periods.First().Events);
        Assert.AreEqual("Time: 10:30", d.Periods.Last().Events.Single());
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_PeriodWithoutEvents_AndEmptyBodyHasNoPeriods()
    {
        var d = new MermaidTimelineParser().Parse("timeline\n  accTitle: hidden\n  2020\n  2021 : Something\n");
        Assert.AreEqual(2, d.PeriodCount);
        Assert.AreEqual("2020", d.Periods.First().Title);
        Assert.AreEqual(0, d.Periods.First().Events.Count);

        var empty = new MermaidTimelineParser().Parse("timeline\n");
        Assert.AreEqual(0, empty.PeriodCount);
        Assert.AreEqual(0, empty.Sections.Count);
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void Timeline_DirectionTd()
    {
        Assert.AreEqual(TimelineDirection.TopDown,     new MermaidTimelineParser().Parse("timeline\n  direction TD\n  2020 : a\n").Direction);
        Assert.AreEqual(TimelineDirection.TopDown,     new MermaidTimelineParser().Parse("timeline TD\n  2020 : a\n").Direction);
        Assert.AreEqual(TimelineDirection.LeftToRight, new MermaidTimelineParser().Parse("timeline\n  direction LR\n  2020 : a\n").Direction);
    }

    [TestMethod]
    [CoversNode("timeline")]
    public void TimelineConfig_ParsesDisableMulticolorAndCScales()
    {
        var cfg = TimelineConfigParser.Parse(
            """
            config:
              timeline:
                disableMulticolor: true
                padding: 12
              themeVariables:
                cScale0: "#4e79a7"
                cScale2: "#59a14f"
                cScaleLabel0: "#ffffff"
            """);
        Assert.IsTrue(cfg.DisableMulticolor);
        Assert.AreEqual(12, cfg.Padding, 1e-9);
        Assert.IsNotNull(cfg.ScaleAt(0));
        Assert.IsNull(cfg.ScaleAt(1));          // slots keep their index
        Assert.IsNotNull(cfg.ScaleAt(2));
        Assert.IsNotNull(cfg.ScaleLabelAt(0));
        Assert.IsNull(cfg.ScaleLabelAt(2));
        Assert.IsFalse(TimelineConfigParser.Parse(null).DisableMulticolor);
    }

    // ── Journey — parser ──────────────────────────────────────────────────

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
    public void Journey_ParsesTitleSectionsTasksScoresActors()
    {
        var d = new MermaidJourneyParser().Parse(JourneySrc);
        Assert.AreEqual("My working day", d.Title);
        Assert.AreEqual(2, d.Sections.Count);
        Assert.AreEqual("Go to work", d.Sections[0].Name);
        Assert.AreEqual(3, d.Sections[0].Tasks.Count);
        Assert.AreEqual(5, d.TaskCount);
        var work = d.Tasks.Single(t => t.Name == "Do work");
        Assert.AreEqual(1, work.Score);
        CollectionAssert.AreEqual(new[] { "Me", "Cat" }, work.Actors);
    }

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_ActorsAreDistinctInFirstAppearanceOrder()
    {
        var d = new MermaidJourneyParser().Parse("journey\n  A: 3: Zed, Amy\n  B: 4: Amy, Bob\n  C: 2: Zed\n");
        CollectionAssert.AreEqual(new[] { "Zed", "Amy", "Bob" }, d.Actors.ToList());
    }

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_MissingScoreIsNeutral_AndOutOfRangeClamps()
    {
        var d = new MermaidJourneyParser().Parse("journey\n  A: : Me\n  B: 9: Me\n  C: 0: Me\n  D: abc: Me\n");
        var t = d.Tasks.ToList();
        Assert.AreEqual(3, t[0].Score);
        Assert.AreEqual(5, t[1].Score);
        Assert.AreEqual(1, t[2].Score);
        Assert.AreEqual(3, t[3].Score);

        Assert.AreEqual(JourneyMood.Sad,     JourneyDiagram.MoodOf(1));
        Assert.AreEqual(JourneyMood.Sad,     JourneyDiagram.MoodOf(2));
        Assert.AreEqual(JourneyMood.Neutral, JourneyDiagram.MoodOf(3));
        Assert.AreEqual(JourneyMood.Happy,   JourneyDiagram.MoodOf(4));
        Assert.AreEqual(JourneyMood.Happy,   JourneyDiagram.MoodOf(5));
    }

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_TaskWithoutActors_AndNonTaskLinesIgnored()
    {
        var d = new MermaidJourneyParser().Parse("journey\n  accTitle: hidden\n  not a task\n  Sit down: 5\n  %% comment\n");
        var t = d.Tasks.Single();
        Assert.AreEqual("Sit down", t.Name);
        Assert.AreEqual(5, t.Score);
        Assert.AreEqual(0, t.Actors.Count);
        Assert.AreEqual(0, d.Actors.Count);
    }

    [TestMethod]
    [CoversNode("journey")]
    public void Journey_TasksBeforeSectionLandInUnnamedSection()
    {
        var d = new MermaidJourneyParser().Parse("journey\n  Wake up: 3: Me\n  section Morning\n  Coffee: 5: Me\n");
        Assert.AreEqual(2, d.Sections.Count);
        Assert.AreEqual(string.Empty, d.Sections[0].Name);
        Assert.AreEqual("Wake up", d.Sections[0].Tasks.Single().Name);
        Assert.AreEqual("Morning", d.Sections[1].Name);
    }

    [TestMethod]
    [CoversNode("journey")]
    public void JourneyConfig_ParsesArraysSizesAndFillTypes()
    {
        var cfg = JourneyConfigParser.Parse(
            """
            config:
              journey:
                width: 120
                height: 40
                boxMargin: 6
                taskFontSize: 11
                actorColours: ["#e15759", "#4e79a7"]
                sectionFills:
                  - "#59a14f"
                  - "#f28e2b"
            """);
        Assert.AreEqual(120, cfg.Width, 1e-9);
        Assert.AreEqual(40, cfg.Height, 1e-9);
        Assert.AreEqual(6, cfg.BoxMargin, 1e-9);
        Assert.AreEqual(11, cfg.TaskFontSize, 1e-9);
        Assert.AreEqual(2, cfg.ActorColours.Count);
        Assert.AreEqual(2, cfg.SectionFills.Count);

        var themed = JourneyConfigParser.Parse("config:\n  themeVariables:\n    fillType0: \"#ff0000\"\n    fillType1: \"#00ff00\"\n");
        Assert.AreEqual(2, themed.SectionFills.Count);
        Assert.AreEqual(0, JourneyConfigParser.Parse(null).ActorColours.Count);
    }

    // ── Block diagram — parser ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("block")]
    public void Block_ParsesRowsColumnsWidthsAndShapes()
    {
        var d = new MermaidBlockParser().Parse(
            """
            block-beta
              columns 3
              a["A label"] b:2 c
              d(("Disk")) e{{"Hex"}} f>"Flag"]
            """);
        Assert.AreEqual(3, d.Root.Columns);
        Assert.AreEqual(6, d.Root.Items.Count);
        var a = (BlockNode)d.Root.Items[0];
        Assert.AreEqual("A label", a.Label);
        Assert.AreEqual(NodeShape.Rectangle, a.Shape);
        Assert.AreEqual(2, d.Root.Items[1].Width);
        Assert.AreEqual("c", ((BlockNode)d.Root.Items[2]).Label);          // a bare id labels itself
        Assert.AreEqual(NodeShape.Circle,     ((BlockNode)d.Root.Items[3]).Shape);
        Assert.AreEqual(NodeShape.Hexagon,    ((BlockNode)d.Root.Items[4]).Shape);
        Assert.AreEqual(NodeShape.Asymmetric, ((BlockNode)d.Root.Items[5]).Shape);
        Assert.AreEqual("Flag", ((BlockNode)d.Root.Items[5]).Label);
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_RecognisesEveryBracketShape()
    {
        var d = new MermaidBlockParser().Parse(
            "block-beta\n r1(\"a\") r2([\"b\"]) r3[[\"c\"]] r4[(\"d\")] r5((\"e\")) r6(((\"f\"))) r7{\"g\"} r8[/\"h\"/] r9[\\\"i\"\\] r10[/\"j\"\\] r11[\\\"k\"/]\n");
        CollectionAssert.AreEqual(new[]
        {
            NodeShape.RoundedRect, NodeShape.Stadium, NodeShape.Subroutine, NodeShape.Cylinder, NodeShape.Circle,
            NodeShape.DoubleCircle, NodeShape.Diamond, NodeShape.Parallelogram, NodeShape.ParallelogramAlt,
            NodeShape.Trapezoid, NodeShape.TrapezoidAlt,
        }, d.Nodes.Select(n => n.Shape).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" }, d.Nodes.Select(n => n.Label).ToArray());
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_NestedGroupsHaveTheirOwnColumns()
    {
        var d = new MermaidBlockParser().Parse(
            """
            block-beta
              columns 3
              a:3
              block:group1:2
                columns 2
                h i j k
              end
              g
              block
                l m
              end
            """);
        Assert.AreEqual(4, d.Root.Items.Count);
        var g1 = (BlockGroup)d.Root.Items[1];
        Assert.AreEqual("group1", g1.Id);
        Assert.AreEqual(2, g1.Width);
        Assert.AreEqual(2, g1.Columns);
        Assert.AreEqual(4, g1.Items.Count);
        var anon = (BlockGroup)d.Root.Items[3];
        Assert.IsNull(anon.Columns);                       // auto: one row
        Assert.AreEqual(2, anon.Items.Count);
        Assert.IsNotNull(d.Find("group1"));
        Assert.IsNotNull(d.Find("k"));
        Assert.AreEqual(10, d.ItemCount);                  // a, group1, h i j k, g, anon, l m
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_SpacesAndBlockArrows()
    {
        var d = new MermaidBlockParser().Parse(
            """
            block-beta
              columns 3
              a space:2
              right<["Yes"]>(right) both<[" "]>(x, down) space
            """);
        Assert.IsInstanceOfType(d.Root.Items[1], typeof(BlockSpace));
        Assert.AreEqual(2, d.Root.Items[1].Width);
        var right = (BlockArrow)d.Root.Items[2];
        Assert.AreEqual("Yes", right.Label);
        Assert.AreEqual(BlockArrowDirections.Right, right.Directions);
        var both = (BlockArrow)d.Root.Items[3];
        Assert.AreEqual(string.Empty, both.Label);
        Assert.AreEqual(BlockArrowDirections.Left | BlockArrowDirections.Right | BlockArrowDirections.Down, both.Directions);
        Assert.AreEqual(5, d.Root.Items.Count);
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_EdgesWithAndWithoutLabels_AndInlineShapes()
    {
        var d = new MermaidBlockParser().Parse(
            """
            block-beta
              A space B
              A --> B
              A -- "X" --> B
              A --- B
              id1("Start")-->id2("Stop")
              ID --> D
            """);
        Assert.AreEqual(5, d.Edges.Count);
        Assert.IsTrue(d.Edges[0].HasArrow);
        Assert.AreEqual("A", d.Edges[0].From);
        Assert.AreEqual("B", d.Edges[0].To);
        Assert.AreEqual("X", d.Edges[1].Label);
        Assert.IsFalse(d.Edges[2].HasArrow);
        var start = (BlockNode)d.Find("id1")!;
        Assert.AreEqual("Start", start.Label);
        Assert.AreEqual(NodeShape.RoundedRect, start.Shape);
        Assert.IsNotNull(d.Find("ID"));                    // an unknown endpoint is created in the current group
        Assert.AreEqual(7, d.Root.Items.Count);            // A, space, B, id1, id2, ID, D
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_StyleClassDefAndClassApply_EvenBeforeDeclaration()
    {
        var d = new MermaidBlockParser().Parse(
            """
            block-beta
              style late fill:#636,stroke:#333,stroke-width:4px
              A space B late
              classDef blue fill:#6e6ce6,stroke:#333,stroke-width:2px;
              class A,B blue
              style B fill:#bbf,stroke:#f66,color:#fff,stroke-dasharray: 5 5
            """);
        var a = d.Find("A")!.Style!;
        Assert.AreEqual("#6e6ce6", a.Fill);
        Assert.AreEqual(2, a.StrokeWidth);
        var b = d.Find("B")!.Style!;
        Assert.AreEqual("#bbf", b.Fill);                   // the explicit style wins over the class
        Assert.AreEqual("#f66", b.Stroke);
        Assert.AreEqual("#fff", b.TextColor);
        Assert.IsTrue(b.Dashed);
        Assert.AreEqual(2, b.StrokeWidth);                 // kept from the class
        var late = d.Find("late")!.Style!;
        Assert.AreEqual("#636", late.Fill);
        Assert.AreEqual(4, late.StrokeWidth);
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_LabelsDecodeEntitiesAndLineBreaks_AndCommentsSkipped()
    {
        var d = new MermaidBlockParser().Parse(
            "block-beta\n%% a comment\n  a[\"Line one<br>two\"] arrow<[\"&nbsp;&nbsp;\"]>(down) %% trailing\n");
        Assert.AreEqual("Line one\ntwo", ((BlockNode)d.Root.Items[0]).Label);
        Assert.AreEqual(string.Empty, ((BlockArrow)d.Root.Items[1]).Label);
        Assert.AreEqual(BlockArrowDirections.Down, ((BlockArrow)d.Root.Items[1]).Directions);
    }

    [TestMethod]
    [CoversNode("block")]
    public void Block_HeaderVariants_AndEmptyDiagram()
    {
        Assert.AreEqual(3, new MermaidBlockParser().Parse("block\n  a b c\n").Root.Items.Count);
        Assert.AreEqual(3, new MermaidBlockParser().Parse("block-beta\n  a b c\n").Root.Items.Count);
        var empty = new MermaidBlockParser().Parse("block-beta\n");
        Assert.AreEqual(0, empty.ItemCount);
        Assert.AreEqual(0, empty.Edges.Count);
    }

    [TestMethod]
    [CoversNode("block")]
    public void BlockConfig_ParsesPadding()
    {
        var cfg = BlockConfigParser.Parse("config:\n  block:\n    padding: 12\n    useMaxWidth: false\n");
        Assert.AreEqual(12, cfg.Padding, 1e-9);
        Assert.IsFalse(cfg.UseMaxWidth);
        Assert.AreEqual(8, BlockConfigParser.Parse(null).Padding, 1e-9);
    }
}
