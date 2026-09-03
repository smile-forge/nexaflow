using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Projecting a parsed C4 diagram onto the shared graph model — elements to cards, boundaries to
/// nested subgraphs, relationships to edges — plus the layout and renderer behaviour that only C4
/// exercises. WPF-free except where a test says otherwise.
/// </summary>
[TestClass]
public class C4ProjectionTests
{
    private static Graph Project(string src) =>
        C4GraphProjector.ToGraph(new MermaidC4Parser().Parse(src));

    // ── Elements ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_BecomeC4ElementNodesCarryingTheirCard()
    {
        var g = Project("""
            C4Container
            Container(web, "Web Application", "Java, Spring MVC", "Delivers the SPA")
            """);
        var node = g.FindNode("web")!;
        Assert.AreEqual(NodeShape.C4Element, node.Shape);
        Assert.AreEqual("Web Application", node.Label);
        Assert.AreEqual(C4ElementKind.Container, node.C4!.Kind);
        Assert.AreEqual("Java, Spring MVC", node.C4.Technology);
        Assert.AreEqual("Delivers the SPA", node.C4.Description);
        Assert.AreEqual("[Container: Java, Spring MVC]", node.C4.Stereotype());
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_PersonStyleAppliesToPeopleOnly()
    {
        var g = Project("""
            C4Context
            SHOW_PERSON_PORTRAIT()
            Person(p, "P")
            System(s, "S")
            SystemDb(db, "DB")
            """);
        Assert.AreEqual(C4ElementShape.PersonPortrait, g.FindNode("p")!.C4!.Shape);
        Assert.AreEqual(C4ElementShape.Box, g.FindNode("s")!.C4!.Shape);
        Assert.AreEqual(C4ElementShape.Database, g.FindNode("db")!.C4!.Shape, "a shape of its own is not overridden");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Elements_StyleResolvesTypeThenTagThenAlias()
    {
        var g = Project("""
            C4Container
            AddElementTag("hot", $bgColor="#tagged", $fontColor="#tagfont")
            UpdateElementStyle("container", $bgColor="#bytype", $fontColor="#typefont", $borderColor="#typeborder")
            Container(a, "A")
            Container(b, "B", $tags="hot")
            Container(c, "C", $tags="hot")
            UpdateElementStyle(c, $bgColor="#byalias")
            """);
        Assert.AreEqual("#bytype",  g.FindNode("a")!.C4!.FillColor);
        Assert.AreEqual("#tagged",  g.FindNode("b")!.C4!.FillColor, "a tag beats the type");
        Assert.AreEqual("#tagfont", g.FindNode("b")!.C4!.FontColor);
        Assert.AreEqual("#byalias", g.FindNode("c")!.C4!.FillColor, "the element's own alias beats both");
        Assert.AreEqual("#tagfont", g.FindNode("c")!.C4!.FontColor, "…without dropping what it did not set");
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Elements_ExternalStyleKeyIsDistinctFromThePlainOne()
    {
        var g = Project("""
            C4Context
            UpdateElementStyle("system", $bgColor="#plain")
            UpdateElementStyle("external_system", $bgColor="#ext")
            System(a, "A")
            System_Ext(b, "B")
            """);
        Assert.AreEqual("#plain", g.FindNode("a")!.C4!.FillColor);
        Assert.AreEqual("#ext",   g.FindNode("b")!.C4!.FillColor);
    }

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Elements_HideStereotypeReachesEveryCard()
    {
        var g = Project("C4Context\nHIDE_STEREOTYPE()\nPerson(p, \"P\")\nSystem(s, \"S\")\n");
        Assert.IsTrue(g.Nodes.All(n => n.C4!.HideStereotype));
        Assert.AreEqual(string.Empty, g.FindNode("p")!.C4!.Stereotype());
    }

    // ── Boundaries ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_BecomeNestedStyledSubgraphs()
    {
        var g = Project("""
            C4Container
            System_Boundary(outer, "Outer") {
              Container_Boundary(inner, "Inner") {
                Container(a, "A")
              }
              Container(b, "B")
            }
            """);
        var outer = g.Subgraphs.Single(s => s.Id == "outer");
        var inner = g.Subgraphs.Single(s => s.Id == "inner");

        Assert.IsNull(outer.ParentId);
        Assert.AreEqual("outer", inner.ParentId);
        Assert.AreEqual("[System]", outer.Style!.SubLabel);
        Assert.AreEqual("[Container]", inner.Style!.SubLabel);
        Assert.AreEqual(EdgeStyle.Dashed, outer.Style.BorderStyle);

        // A nested boundary joins by ParentId, not by being listed as a member node.
        CollectionAssert.AreEqual(new[] { "b" }, outer.NodeIds);
        CollectionAssert.AreEqual(new[] { "a" }, inner.NodeIds);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_DeploymentNodesAreSolidAndSayWhatTheyAre()
    {
        var g = Project("""
            C4Deployment
            Deployment_Node(prod, "Production", "AWS") {
              Container(app, "App")
            }
            Deployment_Node(bare, "Bare")
            """);
        var prod = g.Subgraphs.Single(s => s.Id == "prod");
        Assert.AreEqual("[Deployment Node: AWS]", prod.Style!.SubLabel);
        Assert.AreEqual(EdgeStyle.Solid, prod.Style.BorderStyle, "a physical box reads better solid");
        Assert.AreEqual("[Deployment Node]", g.Subgraphs.Single(s => s.Id == "bare").Style!.SubLabel);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Boundaries_UntypedBoundaryHasNoSubLabel()
    {
        var g = Project("C4Context\nBoundary(b, \"Just a group\")\nSystem(s, \"S\")\nBoundary_End()\n");
        Assert.IsNull(g.Subgraphs.Single().Style!.SubLabel);
    }

    // ── Relationships ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_TechnologyBecomesTheEdgeSubLabel()
    {
        var g = Project("C4Container\nRel(a, b, \"Calls\", \"JSON/HTTPS\")\n");
        var e = g.Edges.Single();
        Assert.AreEqual("Calls", e.Label);
        Assert.AreEqual("[JSON/HTTPS]", e.SubLabel);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_BackSwapsTheEndpoints()
    {
        var g = Project("C4Context\nRel_Back(customer, mail, \"Sends e-mails to\")\n");
        var e = g.Edges.Single();
        Assert.AreEqual("mail", e.SourceId, "declared customer→mail but pointing back");
        Assert.AreEqual("customer", e.TargetId);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_BidirectionalGetsAHeadAtBothEnds()
    {
        var g = Project("C4Context\nBiRel(a, b, \"Both\")\n");
        var e = g.Edges.Single();
        Assert.AreEqual(EdgeArrow.Normal, e.Arrow);
        Assert.AreEqual(EdgeArrow.Normal, e.StartArrow);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_DynamicPrefixesTheIndex()
    {
        var g = Project("""
            C4Dynamic
            Rel(a, b, "First", $index=Index())
            Rel(b, c, "Second", $index=Index())
            """);
        Assert.AreEqual("1: First", g.Edges[0].Label);
        Assert.AreEqual("2: Second", g.Edges[1].Label);

        // A container diagram numbers nothing, even when the author asked for an index.
        var plain = Project("C4Container\nRel(a, b, \"First\", $index=Index())\n");
        Assert.AreEqual("First", plain.Edges[0].Label);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_UndeclaredEndpointsStillGetACard()
    {
        var g = Project("C4Context\nRel(ghost, other, \"Uses\")\n");
        Assert.AreEqual(2, g.Nodes.Count);
        Assert.IsTrue(g.Nodes.All(n => n.Shape == NodeShape.C4Element && n.C4 is not null));
        Assert.AreEqual(1, g.Edges.Count);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Relationships_TagLineStyleReachesTheEdge()
    {
        var g = Project("""
            C4Container
            AddRelTag("async", $lineStyle="DottedLine")
            Rel(a, b, "Publishes", $tags="async")
            """);
        Assert.AreEqual(EdgeStyle.Dotted, g.Edges.Single().Style);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Relationships_ColoursReachTheEdge()
    {
        var g = Project("""
            C4Container
            AddRelTag("async", $textColor="#0f0", $lineColor="#f00", $lineStyle="DottedLine")
            Rel(a, b, "Publishes", $tags="async")
            Rel(c, d, "Direct")
            UpdateRelStyle(c, d, $textColor="#00f", $lineColor="#00f")
            """);
        var tagged = g.Edges[0];
        Assert.AreEqual("#f00", tagged.LineColor);
        Assert.AreEqual("#0f0", tagged.TextColor);
        Assert.AreEqual(EdgeStyle.Dotted, tagged.Style);

        var byPair = g.Edges[1];
        Assert.AreEqual("#00f", byPair.LineColor);
        Assert.AreEqual("#00f", byPair.TextColor);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void EdgeColours_SurviveTheExpansionCopy()
    {
        var projected = Project("""
            C4Container
            Container(a, "A")
            Container(b, "B")
            Rel(a, b, "Calls")
            UpdateRelStyle(a, b, $lineColor="#f00", $textColor="#0f0")
            """);
        var view = GraphExpansion.Apply(projected, null);
        Assert.AreEqual("#f00", view.Edges.Single().LineColor);
        Assert.AreEqual("#0f0", view.Edges.Single().TextColor);
    }

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-styling")]
    public void Render_EdgeLineColourIsUsed() => UiThread.Run(() =>
    {
        const string body = """
            C4Container
            Container(a, "A")
            Container(b, "B")
            Rel(a, b, "Calls")
            """;
        var plain   = WpfGraphRenderer.RenderCanvas(SugiyamaLayout.Compute(Project(body), 900), MarkdownPalette.Dark);
        var colored = WpfGraphRenderer.RenderCanvas(
            SugiyamaLayout.Compute(Project(body + "\nUpdateRelStyle(a, b, $lineColor=\"#ff0000\")\n"), 900),
            MarkdownPalette.Dark);

        static System.Windows.Media.Color StrokeOf(Canvas c) =>
            DiagramBrushes.ColorOf(c.Children.OfType<System.Windows.Shapes.Path>().First().Stroke, Colors.Transparent);

        Assert.AreNotEqual(StrokeOf(plain), StrokeOf(colored));
        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), StrokeOf(colored));
    });

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Relationships_DirectionDirectiveSetsTheGraphDirection()
    {
        Assert.AreEqual(GraphDirection.LeftRight, Project("C4Context\nLAYOUT_LEFT_RIGHT()\n").Direction);
        Assert.AreEqual(GraphDirection.TopDown, Project("C4Context\n").Direction, "the layout's own default");
    }

    // ── Legend ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Legend_ListsTheFlavoursActuallyPresent()
    {
        var g = Project("""
            C4Container
            Person(p, "P")
            Person_Ext(pe, "PE")
            Container(c, "C")
            ContainerDb(db, "DB")
            SHOW_LEGEND()
            """);
        var labels = g.Legend!.Select(e => e.Label).ToArray();
        CollectionAssert.AreEqual(
            new[] { "Person", "Person (external)", "Container", "Container (database)" }, labels);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Legend_IsAbsentWithoutTheDirective()
    {
        Assert.IsNull(Project("C4Context\nPerson(p, \"P\")\n").Legend);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Legend_SurvivesTheExpansionCopy()
    {
        // Every diagram goes through GraphExpansion.Apply, which rebuilds the graph. Anything it
        // forgets to copy is silently gone by the time the renderer sees it.
        var projected = Project("C4Context\nPerson(p, \"P\")\nSHOW_LEGEND()\n");
        var view = GraphExpansion.Apply(projected, null);
        Assert.IsNotNull(view.Legend);
        Assert.AreEqual(projected.Legend!.Count, view.Legend!.Count);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void BoundaryStyle_SurvivesTheExpansionCopy()
    {
        var projected = Project("C4Container\nSystem_Boundary(b, \"B\") {\n  Container(a, \"A\")\n}\n");
        var view = GraphExpansion.Apply(projected, null);
        Assert.AreEqual("[System]", view.Subgraphs.Single().Style!.SubLabel);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void EdgeSubLabel_SurvivesTheExpansionCopy()
    {
        var projected = Project("C4Container\nContainer(a, \"A\")\nContainer(b, \"B\")\nRel(a, b, \"Calls\", \"HTTPS\")\n");
        var view = GraphExpansion.Apply(projected, null);
        Assert.AreEqual("[HTTPS]", view.Edges.Single().SubLabel);
    }

    [TestMethod]
    [CoversNode("c4-styling")]
    public void Legend_IncludesTagsThatNamedTheirOwnText()
    {
        var g = Project("""
            C4Container
            AddElementTag("v2", $bgColor="#4CAF50", $legendText="Shipping in v2")
            Container(a, "A", $tags="v2")
            SHOW_LEGEND()
            """);
        Assert.IsTrue(g.Legend!.Any(e => e.Label == "Shipping in v2"));
    }

    // ── Layout ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("c4-elements")]
    public void Layout_SizesACardFromTheSharedMetrics()
    {
        var g = Project("""
            C4Container
            Container(web, "Web Application", "Java, Spring MVC", "Delivers the static content and the SPA.")
            """);
        var lg = SugiyamaLayout.Compute(g, 900);
        var node = lg.AllNodes.Single(n => !n.IsDummy);
        var (w, h) = C4ElementMetrics.Measure("Web Application", g.FindNode("web")!.C4!);

        Assert.AreEqual(w, node.Width, 1e-9, "the layout must reserve exactly what the painter draws");
        Assert.AreEqual(h, node.Height, 1e-9);
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Layout_BoundaryHeaderMakesRoomForTheSubLabel()
    {
        // A [type] line under a boundary title needs a taller header band, or the first element
        // inside the box is drawn over it.
        var withSub = SugiyamaLayout.Compute(Project("""
            C4Container
            System_Boundary(b, "Boundary") {
              Container(a, "A")
            }
            """), 900);

        var plain = new Graph();
        plain.GetOrAdd("a", "A");
        var sg = new Subgraph { Id = "b", Label = "Boundary" };
        sg.NodeIds.Add("a");
        plain.Subgraphs.Add(sg);
        var withoutSub = SugiyamaLayout.Compute(plain, 900);

        Assert.IsTrue(
            withSub.SubgraphBoxes.Single().Bounds.Height > withoutSub.SubgraphBoxes.Single().Bounds.Height,
            "the sub-label band should make the box taller");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Layout_BoundaryContainingOnlyBoundariesStillGetsABox()
    {
        // Deployment diagrams nest node-inside-node with no elements at the outer levels, so a
        // cluster whose direct members are all child clusters has to be laid out too — otherwise
        // the outermost boxes of every deployment diagram silently disappear.
        var g = Project("""
            C4Deployment
            Deployment_Node(plc, "Big Bank plc") {
              Deployment_Node(dn, "bigbank-api") {
                Deployment_Node(tomcat, "Apache Tomcat") {
                  Container(api, "API")
                }
              }
              Deployment_Node(dbnode, "bigbank-db01") {
                ContainerDb(db, "Database")
              }
            }
            """);
        // Through GraphExpansion, as the renderer does — laying the projected graph out directly
        // would skip the step that used to delete these boxes.
        var lg = SugiyamaLayout.Compute(GraphExpansion.Apply(g, null), 900);
        var ids = lg.SubgraphBoxes.Select(b => b.Source?.Id).ToList();

        CollectionAssert.AreEquivalent(new[] { "plc", "dn", "tomcat", "dbnode" }, ids);

        // …and nested, so the outer box actually encloses the inner one.
        var plc = lg.SubgraphBoxes.Single(b => b.Source?.Id == "plc").Bounds;
        var dn  = lg.SubgraphBoxes.Single(b => b.Source?.Id == "dn").Bounds;
        Assert.IsTrue(plc.Contains(dn), $"plc {plc} should enclose dn {dn}");
    }

    [TestMethod]
    [CoversNode("c4-boundaries")]
    public void Layout_SubgraphBoxCarriesItsSourceSubgraph()
    {
        // Without this the renderer cannot know how a boundary wanted to be drawn.
        var lg = SugiyamaLayout.Compute(Project("""
            C4Container
            System_Boundary(b, "Boundary") {
              Container(a, "A")
            }
            """), 900);
        var box = lg.SubgraphBoxes.Single();
        Assert.IsNotNull(box.Source);
        Assert.AreEqual("b", box.Source!.Id);
        Assert.AreEqual("[System]", box.Source.Style!.SubLabel);
    }

    [TestMethod]
    [CoversNode("c4-relationships")]
    public void Layout_ClusteredEdgesKeepTheirSubLabel()
    {
        // The clustered layout rebuilds every edge per level; anything the renderer reads has to be
        // carried across or it vanishes the moment a diagram has a boundary — which C4 always does.
        var lg = SugiyamaLayout.Compute(Project("""
            C4Container
            System_Boundary(b, "Boundary") {
              Container(a, "A")
            }
            Container(outside, "Outside")
            Rel(a, outside, "Calls", "HTTPS")
            """), 900);

        var edge = lg.Edges.Single(e => e.Source is not null).Source!;
        Assert.AreEqual("[HTTPS]", edge.SubLabel);
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Render_RoutesToTheGraphViewNotRawText() => UiThread.Run(() =>
    {
        foreach (var header in new[] { "C4Context", "C4Container", "C4Component", "C4Dynamic", "C4Deployment" })
        {
            var fe = DiagramRenderer.Render("mermaid", $"{header}\nPerson(p, \"P\")\nSystem(s, \"S\")\nRel(p, s, \"Uses\")\n", MarkdownPalette.Dark);
            Assert.IsInstanceOfType(fe, typeof(GraphDiagramView), header);
        }
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-styling")]
    public void Render_LegendGrowsTheCanvas() => UiThread.Run(() =>
    {
        const string body = """
            C4Container
            Person(p, "P")
            Container(c, "C")
            Rel(p, c, "Uses")
            """;
        var without = SugiyamaLayout.Compute(Project(body), 900);
        var with    = SugiyamaLayout.Compute(Project(body + "\nSHOW_LEGEND()\n"), 900);

        var plainCanvas  = WpfGraphRenderer.RenderCanvas(without, MarkdownPalette.Dark);
        var legendCanvas = WpfGraphRenderer.RenderCanvas(with, MarkdownPalette.Dark);

        Assert.IsTrue(legendCanvas.Height > plainCanvas.Height,
            $"the legend should add height ({plainCanvas.Height} -> {legendCanvas.Height})");
        Assert.IsTrue(legendCanvas.Children.Count > plainCanvas.Children.Count);
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Render_EveryShapeAndBothPalettesDraw() => UiThread.Run(() =>
    {
        const string src = """
            C4Deployment
            title Everything
            SHOW_LEGEND()
            Person(p, "Person")
            Person_Ext(pe, "External person")
            Deployment_Node(n, "Node", "Ubuntu") {
              Container(c, "Container", "Java", "A description that is long enough to wrap onto more lines.")
              ContainerDb(db, "Database", "SQL")
              ContainerQueue(q, "Queue", "Kafka")
            }
            Rel(p, c, "Uses", "HTTPS")
            BiRel(c, db, "Reads and writes", "JDBC")
            Rel_Back(pe, q, "Consumes")
            """;
        foreach (var palette in new[] { MarkdownPalette.Dark, MarkdownPalette.Light })
            Assert.IsNotNull(WpfGraphRenderer.RenderCanvas(SugiyamaLayout.Compute(Project(src), 1000), palette));
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Render_SelectedCardHighlightsItsOutline() => UiThread.Run(() =>
    {
        // Highlight() restrokes Children[0] of a composite node; the card painter must keep the
        // outline first or selecting a C4 element silently does nothing.
        var lg = SugiyamaLayout.Compute(Project("C4Context\nPerson(p, \"P\")\n"), 900);
        var canvas = WpfGraphRenderer.RenderCanvas(lg, MarkdownPalette.Dark, new GraphRenderOptions { SelectedNodeId = "p" });
        var cell = canvas.Children.OfType<Canvas>().Single();
        var outline = (System.Windows.Shapes.Shape)cell.Children[0];
        Assert.AreEqual(3, outline.StrokeThickness, 1e-9, "the selection stroke should have been applied");
    });

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("c4-elements")]
    public void Render_EmptyDiagramDoesNotThrow() => UiThread.Run(() =>
    {
        Assert.IsNotNull(DiagramRenderer.Render("mermaid", "C4Context\n", MarkdownPalette.Dark));
    });
}
