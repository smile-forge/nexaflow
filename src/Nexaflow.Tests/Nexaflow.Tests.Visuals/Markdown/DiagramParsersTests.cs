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

    // ── Sequence: participant metadata, notes, activations, fragments ──────

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

    // ── Requirement diagram ────────────────────────────────────────────────

    // ── Sankey — parser ───────────────────────────────────────────────────

    // ── Sankey — config ───────────────────────────────────────────────────

    // ── ER diagram — parser ───────────────────────────────────────────────

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
