using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// The drawing half of expandable nodes: a chip is a hit region of its own, so a node's body can
/// still navigate while its chip opens the subtree behind it. Renderer-level, so it needs the UI
/// thread; what the chip <i>means</i> is decided WPF-free in <c>GraphExpansionTests</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("graph-expandable-nodes")]
public class ExpandableDiagramTests
{
    private const string Src =
        """
        ---
        config:
          nexaflow:
            expandDepth: 1
        ---
        graph TD
          root["Root"] --> child["Child"]
          child --> hidden["Hidden"]
          click root "https://example.com/root"
        """;

    /// <summary>Every element under <paramref name="root"/> carrying a click action.</summary>
    private static List<DependencyObject> Targets(DependencyObject root)
    {
        var found = new List<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (DiagramInteraction.GetTarget(d) is not null) found.Add(d);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }

    private static FrameworkElement Render(DiagramRenderOptions options)
    {
        var element = DiagramRenderer.Render("mermaid", Src, options);
        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
        return element;
    }

    [TestMethod]
    public void ANodesBodyAndItsChipAreTwoIndependentTargets() => UiThread.Run(() =>
    {
        var navigated = new List<string>();
        var expanded  = new List<DiagramExpandRequest>();

        var element = Render(new DiagramRenderOptions
        {
            Palette    = MarkdownPalette.Dark,
            OnNavigate = href => { navigated.Add(href); return true; },
            OnExpand   = req  => { expanded.Add(req);   return true; },
        });

        var targets = Targets(element);
        Assert.IsTrue(targets.Count >= 2,
            "the linked node and the collapsed node's chip must both be clickable");

        foreach (var t in targets) DiagramInteraction.GetTarget(t)!.Invoke();

        Assert.IsTrue(navigated.Contains("https://example.com/root"),
            "the node body still navigates — expansion did not take its gesture");
        Assert.IsTrue(expanded.Any(r => r.Expand),
            "the chip asks the host to open the node it belongs to");
    });

    [TestMethod]
    public void WithNoHandlerAtAllTheDiagramOpensTheNodeItself() => UiThread.Run(() =>
    {
        // No OnExpand: a plain markdown flowchart with an expandDepth is still explorable, because
        // the source already describes what is behind the chip.
        var element = Render(new DiagramRenderOptions { Palette = MarkdownPalette.Dark });

        var view = element as GraphDiagramView;
        Assert.IsNotNull(view, "a graph diagram renders through the expandable view");

        Assert.AreEqual(0, TextOf(element).Count(t => t.Contains("Hidden")),
            "depth 1 hides the grandchild to begin with");

        // Exactly one node is closed, so exactly one chip offers to open something.
        var chip = Targets(element)
            .Select(DiagramInteraction.GetTarget)
            .Single(t => t!.Tooltip?.StartsWith("Expand") == true);
        chip!.Invoke();

        element.UpdateLayout();
        Assert.AreEqual(1, TextOf(element).Count(t => t.Contains("Hidden")),
            "clicking the chip opened the subtree in place");
    });

    [TestMethod]
    public void AnOrdinaryFlowchartDrawsNoChips() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("mermaid", "graph TD\n  a[\"A\"] --> b[\"B\"]\n",
            new DiagramRenderOptions { Palette = MarkdownPalette.Dark, OnExpand = _ => true });
        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));

        // Its nodes are still selectable — that is true of every diagram — but nothing offers to
        // open a subtree, because the diagram never said it had one.
        Assert.AreEqual(0, Targets(element).Select(DiagramInteraction.GetTarget)
                                           .Count(t => t!.Kind == DiagramTargetKind.Expand),
            "a diagram that never mentions expansion must grow no chips");
    });

    private static GraphDiagramView Laid(Graph g, NexaflowGraphConfig cfg, bool fitToWidth = false)
    {
        var view = new GraphDiagramView(g, cfg, MarkdownPalette.Dark,
                                        new DiagramRenderOptions { Palette = MarkdownPalette.Dark, FitToWidth = fitToWidth },
                                        600);
        view.Measure(new Size(600, 2000));
        view.Arrange(new Rect(0, 0, 600, 2000));
        view.UpdateLayout();
        return view;
    }

    [TestMethod]
    public void PanAndZoomAreAlwaysAvailable_NotOnlyWhenTheDiagramOverflows() => UiThread.Run(() =>
    {
        // A gesture that comes and goes with the size of the content is one nobody can learn — and
        // "it fits" is only true until the next node is opened anyway.
        var big = new Graph();
        for (int i = 0; i < 80; i++) big.AddEdge("root", $"c{i}");

        var small = new Graph();
        small.AddEdge("a", "b");

        Assert.IsNotNull(Descendant<PanZoomSurface>(Laid(big,   new NexaflowGraphConfig())));
        Assert.IsNotNull(Descendant<PanZoomSurface>(Laid(small, new NexaflowGraphConfig())),
            "a diagram that happens to fit is still pannable and zoomable");
    });

    [TestMethod]
    public void ASurfaceThatScalesDiagramsToItsWidthGetsNoViewport() => UiThread.Run(() =>
    {
        // The inline editor scales diagrams to the column; a pan gesture inside a scaled picture
        // would fight both the scaling and text selection.
        var g = new Graph();
        for (int i = 0; i < 80; i++) g.AddEdge("root", $"c{i}");

        Assert.IsNull(Descendant<PanZoomSurface>(Laid(g, new NexaflowGraphConfig(), fitToWidth: true)));
    });

    [TestMethod]
    public void SelectingANodePicksOutItsEdgesToo() => UiThread.Run(() =>
    {
        // Following one line across a dense diagram by eye is the thing selection exists for, so the
        // edges touching the selected node have to change with it — not just the node.
        var g = new Graph();
        g.AddEdge("a", "b");
        g.AddEdge("c", "d");   // nothing to do with the selection

        var view = Laid(g, new NexaflowGraphConfig());
        var before = EdgeStrokes(view);

        Targets(view).Select(DiagramInteraction.GetTarget).First(t => t!.NodeId == "a")!.Invoke();
        view.UpdateLayout();

        var after = EdgeStrokes(view);
        Assert.AreEqual(1, after.Except(before).Count(),
            "exactly the edge touching the selected node is drawn differently");
    });

    /// <summary>The stroke of every drawn edge, as a colour string.</summary>
    private static List<string> EdgeStrokes(DependencyObject root)
    {
        var strokes = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Shapes.Path { Stroke: SolidColorBrush b }) strokes.Add(b.Color.ToString());
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return strokes;
    }

    [TestMethod]
    public void WhereOpeningCostsATabASingleClickOnlyLooks() => UiThread.Run(() =>
    {
        // The PE inspector spawns a whole tab per node, so a single click must not do it by accident.
        var opened = new List<string>();
        var g = new Graph();
        g.GetOrAdd("a").Href = "C:/lib.dll";
        g.AddEdge("a", "b");

        var view = new GraphDiagramView(g, new NexaflowGraphConfig(), MarkdownPalette.Dark,
            new DiagramRenderOptions
            {
                Palette           = MarkdownPalette.Dark,
                OnNavigate        = h => { opened.Add(h); return true; },
                OpenOnDoubleClick = true,
            }, 600);
        view.Measure(new Size(600, 2000));
        view.Arrange(new Rect(0, 0, 600, 2000));
        view.UpdateLayout();

        Targets(view).Select(DiagramInteraction.GetTarget).First(t => t!.NodeId == "a")!.Invoke();
        view.UpdateLayout();   // selecting re-derives the diagram, so the old elements are gone
        CollectionAssert.AreEqual(Array.Empty<string>(), opened, "a single click selects, it does not open");

        var node  = Targets(view).First(t => DiagramInteraction.GetTarget(t)!.NodeId == "a");
        var point = ((UIElement)node).TranslatePoint(new Point(2, 2), view);
        Assert.IsTrue(((IInteractiveBlock)view).PointerDoubleClick(point), "…and a double-click does");
        CollectionAssert.AreEqual(new[] { "C:/lib.dll" }, opened);
    });

    [TestMethod]
    public void ADiagramInAFlowingDocumentDoesNotClaimThePlainWheel() => UiThread.Run(() =>
    {
        // Otherwise a page could never be scrolled past a diagram.
        var g = new Graph();
        g.AddEdge("a", "b");

        Assert.IsFalse(((IInteractiveBlock)Laid(g, new NexaflowGraphConfig())).WantsPointerWheel(new Point(10, 10)));

        var owning = new GraphDiagramView(g, new NexaflowGraphConfig(), MarkdownPalette.Dark,
            new DiagramRenderOptions { Palette = MarkdownPalette.Dark, ZoomOnWheel = true }, 600);
        owning.Measure(new Size(600, 2000));
        owning.Arrange(new Rect(0, 0, 600, 2000));
        Assert.IsTrue(((IInteractiveBlock)owning).WantsPointerWheel(new Point(10, 10)),
            "a pane whose whole content is the diagram does claim it");
    });

    [TestMethod]
    public void SelectingANodeLeavesThePanAndZoomExactlyWhereItWas() => UiThread.Run(() =>
    {
        // Re-centring the view under someone who just clicked a node throws away where they were —
        // and they were probably looking at it. If a highlighted edge is off-screen, they can pan.
        var g = new Graph();
        for (int i = 0; i < 40; i++) g.AddEdge("root", $"c{i}");

        var view    = Laid(g, new NexaflowGraphConfig());
        var surface = Descendant<PanZoomSurface>(view)!;
        surface.RestoreView(0.42, -120, -60);

        Targets(view).Select(DiagramInteraction.GetTarget).First(t => t!.NodeId == "c3")!.Invoke();
        view.UpdateLayout();

        var after = Descendant<PanZoomSurface>(view)!.View;
        Assert.AreEqual(0.42, after.Scale, 0.0001);
        Assert.AreEqual(-120, after.X, 0.01);
        Assert.AreEqual(-60,  after.Y, 0.01);
    });

    [TestMethod]
    public void TheSelectedNodeLetsGoWhenItIsClickedAgain_ButEmptyCanvasDoesNot() => UiThread.Run(() =>
    {
        var g = new Graph();
        g.AddEdge("a", "b");
        var view = Laid(g, new NexaflowGraphConfig());

        DiagramTarget NodeA() =>
            Targets(view).Select(DiagramInteraction.GetTarget)
                         .First(t => t!.NodeId == "a" && t.Kind == DiagramTargetKind.Activate)!;

        NodeA().Invoke();
        view.UpdateLayout();
        Assert.AreEqual(1, HighlightedShapes(view), "clicking a node selects it");

        // Empty canvas is where a pan starts; losing the selection to a mis-grabbed drag would be
        // its own small annoyance.
        ((IInteractiveBlock)view).BeginPointerSelect(new Point(2, 2));
        ((IInteractiveBlock)view).EndPointerSelect();
        view.UpdateLayout();
        Assert.AreEqual(1, HighlightedShapes(view), "…and panning from the background keeps it");

        NodeA().Invoke();
        view.UpdateLayout();
        Assert.AreEqual(0, HighlightedShapes(view), "…while clicking the node again lets it go");
    });

    /// <summary>How many shapes are drawn in the selection stroke.</summary>
    private static int HighlightedShapes(DependencyObject root)
    {
        var select = (MarkdownPalette.Dark.Warning as SolidColorBrush)!.Color;
        int count = 0;
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Shapes.Shape { Stroke: SolidColorBrush b } s &&
                b.Color == select && s.StrokeThickness >= 3) count++;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return count;
    }

    [TestMethod]
    public void ADiagramIsFittedWhenItArrives_EvenAfterAnEarlierEmptyRender() => UiThread.Run(() =>
    {
        // The dependency walk is asynchronous, so the first render is of an empty diagram. Fitting
        // that must not be mistaken for a viewport the reader chose, or the real diagram arrives and
        // is "restored" to the empty one's scale instead of being fitted.
        var host = new SelectableMarkdownView { Markdown = "```mermaid\ngraph LR\n  a --> b\n```" };
        host.Measure(new Size(700, 500));
        host.Arrange(new Rect(0, 0, 700, 500));
        host.UpdateLayout();

        var small = Descendant<PanZoomSurface>(host)!.View.Scale;

        var big = new System.Text.StringBuilder("```mermaid\ngraph LR\n");
        for (int i = 0; i < 60; i++) big.Append($"  root --> c{i}[\"a much longer module name {i}\"]\n");
        big.Append("```");
        host.Markdown = big.ToString();
        host.UpdateLayout();

        var fitted = Descendant<PanZoomSurface>(host)!.View.Scale;
        Assert.IsTrue(fitted < small,
            $"the bigger diagram must be fitted for itself (was {small:0.###}, now {fitted:0.###})");
    });

    [TestMethod]
    public void OpeningAFoldSurvivesTheHostReEmittingTheDiagram() => UiThread.Run(() =>
    {
        // The host answers an expand by regenerating the whole markdown, which renumbers every id and
        // replaces every element. A fold this renderer opened has to survive that, or it springs shut
        // the moment anything else is expanded.
        const string Doc =
            """
            ```mermaid
            ---
            config:
              nexaflow:
                maxFanOut: 5
                expanded:
                  n0: root.dll
            ---
            graph LR
              n0["root.dll"]
              n0 --> n1["a"]
              n0 --> n2["b"]
              n0 --> n3["c"]
              n0 --> n4["d"]
              n0 --> n5["e"]
              n0 --> n6["f"]
              n0 --> n7["g"]
            ```
            """;

        var host = new SelectableMarkdownView { Markdown = Doc, DiagramOpenOnDoubleClick = true };
        host.Measure(new Size(700, 900));
        host.Arrange(new Rect(0, 0, 700, 900));
        host.UpdateLayout();

        Assert.AreEqual(1, TextOf(host).Count(t => t.EndsWith(" more")), "the surplus siblings folded");

        var chip = Targets(host).Select(DiagramInteraction.GetTarget)
                                .Single(t => t!.Kind == DiagramTargetKind.Expand &&
                                             t.NodeId?.StartsWith(GraphExpansion.OverflowPrefix) == true);
        chip!.Invoke();
        host.UpdateLayout();
        Assert.AreEqual(0, TextOf(host).Count(t => t.EndsWith(" more")), "the fold opened");

        // Now the host re-emits — same diagram, one more import, every element replaced.
        host.Markdown = Doc.Replace("  n0 --> n7[\"g\"]", "  n0 --> n7[\"g\"]\n  n0 --> n8[\"h\"]");
        host.UpdateLayout();

        Assert.AreEqual(0, TextOf(host).Count(t => t.EndsWith(" more")),
            "…and stayed open when the document was rebuilt underneath it");

        // "Start over" is the one thing that should forget it — otherwise the reader is left zoomed
        // into a corner of a graph that no longer exists.
        host.ResetDiagramViews();
        host.Markdown = Doc + "\n";
        host.UpdateLayout();
        Assert.AreEqual(1, TextOf(host).Count(t => t.EndsWith(" more")), "resetting the view refolds it");
    });

    [TestMethod]
    public void ADiagramSizedToItsPaneLeavesRoomForItsOwnFrame() => UiThread.Run(() =>
    {
        // Given the pane's height, the whole block — frame, margins and all — has to fit inside it,
        // or the minimap and the bottom border end up past the edge of the panel.
        var g = new Graph();
        for (int i = 0; i < 80; i++) g.AddEdge("root", $"c{i}");

        var view = new GraphDiagramView(g, new NexaflowGraphConfig(), MarkdownPalette.Dark,
            new DiagramRenderOptions { Palette = MarkdownPalette.Dark, MaxHeight = 400 }, 600);
        view.Measure(new Size(600, double.PositiveInfinity));

        Assert.IsTrue(view.DesiredSize.Height <= 400,
            $"the block is {view.DesiredSize.Height:0}px in a 400px pane");
    });

    [TestMethod]
    public void TheOverflowChipOpensTheFoldedSiblings() => UiThread.Run(() =>
    {
        // The "+N more" stand-in is the renderer's own invention, so it must never be handed to a
        // host that has never heard of it — it opens locally or not at all.
        var claimed = new List<DiagramExpandRequest>();
        var g = new Graph();
        for (int i = 0; i < 40; i++) g.AddEdge("root", $"c{i}");

        var view = new GraphDiagramView(g, new NexaflowGraphConfig { MaxFanOut = 10 }, MarkdownPalette.Dark,
                                        new DiagramRenderOptions
                                        {
                                            Palette  = MarkdownPalette.Dark,
                                            OnExpand = r => { claimed.Add(r); return true; },
                                        }, 600);
        view.Measure(new Size(600, 2000));
        view.Arrange(new Rect(0, 0, 600, 2000));
        view.UpdateLayout();

        Assert.AreEqual(1, TextOf(view).Count(t => t == "+31 more"));

        Targets(view).Select(DiagramInteraction.GetTarget).Single(t => t!.Tooltip?.StartsWith("Expand") == true)!.Invoke();
        view.UpdateLayout();

        Assert.AreEqual(0, claimed.Count, "the host is never asked about a node it did not author");
        Assert.AreEqual(0, TextOf(view).Count(t => t == "+31 more"), "the stand-in is gone…");
        Assert.AreEqual(40, TextOf(view).Count(t => t.StartsWith("c")), "…and every folded sibling is shown");
    });

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Descendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static List<string> TextOf(DependencyObject root)
    {
        var text = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.TextBlock tb) text.Add(tb.Text);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return text;
    }
}
