using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using System;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The drawing half of nodes that fold: what is past the frontier is never drawn, what holds it carries a chip, and
/// the chip is a thing to press of its own — so a node whose body means something else goes on meaning it.
///
/// <para>
/// What a chip <em>means</em> is decided WPF-free in <c>DiagramExpansionTests</c>; this is that, drawn and pressed, in a
/// document — where a press is answered, and where what the host said about diagrams is heard.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("graph-expandable-nodes")]
public class FoldingDiagramTests
{
    private const string Src =
        """
        ---
        config:
          nexaflow:
            defaultExpansion: 1
        ---
        graph TD
          root["Root"] --> child["Child"]
          child --> hidden["Hidden"]
          click root "https://example.com/root"
        """;

    /// <summary>The diagram fenced in a document, as a reader is shown it, measured and arranged.</summary>
    private static MarkdownSurface Shown(string diagram, Action<MarkdownSurface>? host = null)
    {
        var surface = new MarkdownSurface();
        host?.Invoke(surface);
        surface.Markdown = "```mermaid\n" + diagram.TrimEnd('\n') + "\n```\n";

        return Settled(surface);
    }

    private static MarkdownSurface Settled(MarkdownSurface surface)
    {
        surface.Measure(new Size(900, 900));
        surface.Arrange(new Rect(0, 0, 900, 900));
        surface.UpdateLayout();
        return surface;
    }

    /// <summary>Every word drawn anywhere in it.</summary>
    private static List<string> Words(MarkdownSurface surface) =>
        [.. surface.Shown.Laid.Root.SelfAndDescendants()
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .OfType<string>()];

    /// <summary>Where each piece of a kind was placed, in the order they were drawn.</summary>
    private static List<Rect> Placed(MarkdownSurface surface, string kind) =>
        [.. surface.Shown.Laid.Tree.Root.Placed().Where(at => at.Piece.Kind == kind).Select(at => at.Where)];

    private static Point Middle(Rect where) => new(where.X + (where.Width / 2), where.Y + (where.Height / 2));

    /// <summary>A press at the middle of <paramref name="where"/>, and the page as it then stands.</summary>
    private static void Press(MarkdownSurface surface, Rect where)
    {
        surface.Shown.BeginPointerSelect(Middle(where));
        Settled(surface);
    }

    [TestMethod]
    public void AnOrdinaryFlowchartDrawsNoChips() => UiThread.Run(() =>
    {
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n");

        Assert.AreEqual(0, Placed(surface, MermaidPiece.Chip).Count,
                        "a diagram that never mentions folding must grow no chips");
    });

    [TestMethod]
    public void WhatIsPastTheFrontierIsNotDrawnAndWhatHoldsItCarriesAChip() => UiThread.Run(() =>
    {
        var surface = Shown(Src);
        var words = Words(surface);

        Assert.IsTrue(words.Contains("Root") && words.Contains("Child"), "one level down is inside the frontier");
        Assert.IsFalse(words.Contains("Hidden"), "and the grandchild is past it");

        Assert.AreEqual(2, Placed(surface, MermaidPiece.Chip).Count,
                        "the open root and the folded child each say so");
        Assert.IsTrue(words.Contains("+1"), "and the folded one says how much is behind it");
    });

    [TestMethod]
    public void WithNoHandlerAtAllTheDiagramOpensTheNodeItself() => UiThread.Run(() =>
    {
        // Nobody listening: a plain markdown flowchart is still explorable, because the source already describes what
        // is behind the chip.
        var surface = Shown(Src);

        // The chips are drawn in the order the nodes are written, so the second is the folded child's.
        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);

        Assert.IsTrue(Words(surface).Contains("Hidden"), "pressing the chip opened what was behind it, in place");
    });

    [TestMethod]
    public void ANodesBodyAndItsChipAreTwoIndependentTargets() => UiThread.Run(() =>
    {
        var followed = new List<string>();
        var opened = new List<DiagramExpandRequest>();

        var surface = Shown(Src, host =>
        {
            host.LinkNavigate = href => { followed.Add(href); return true; };
            host.DiagramExpand = asked => { opened.Add(asked); return true; };
        });

        // The root's body: a press on it follows where its click line said it leads.
        Press(surface, Placed(surface, FlowchartPiece.Node).First());

        CollectionAssert.Contains(followed, "https://example.com/root",
                                  "the node body still leads where it said — folding did not take its press");

        // Its chip: a press on it asks for the node it belongs to, and nothing was followed by it.
        Press(surface, Placed(surface, MermaidPiece.Chip).First());

        Assert.AreEqual(1, followed.Count, "the chip is not the body");
        Assert.AreEqual(1, opened.Count, "and it asked about a node");
        Assert.AreEqual("root", opened[0].NodeId);
    });

    [TestMethod]
    public void AnOpeningIsWrittenDownBeforeTheHostIsOfferedIt() => UiThread.Run(() =>
    {
        // The host answers by drawing the whole diagram again, so an opening made here has to survive that.
        var surface = Shown(Src, host => host.DiagramExpand = _ => true);

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);
        surface.RefreshDiagrams();
        Settled(surface);

        Assert.IsTrue(Words(surface).Contains("Hidden"), "the opening was kept, whoever took the request on");
    });

    [TestMethod]
    public void WhatWasOpenedIsForgottenOnlyWhenTheHostSaysSo() => UiThread.Run(() =>
    {
        var surface = Shown(Src);

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);
        surface.ResetDiagramViews();
        Settled(surface);

        Assert.IsFalse(Words(surface).Contains("Hidden"), "the diagram is drawn as its source says again");
    });

    [TestMethod]
    public void AClickLineIsFollowedOnOnePressWithNoChipInSight() => UiThread.Run(() =>
    {
        var followed = new List<string>();
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n  click a \"https://example.com/a\"\n",
                            host => host.LinkNavigate = href => { followed.Add(href); return true; });

        Press(surface, Placed(surface, FlowchartPiece.Node).First());

        CollectionAssert.Contains(followed, "https://example.com/a",
                                  "a flowchart says where its nodes lead, and one press follows it");
    });

    [TestMethod]
    public void RightClickingOffersWhatThatOneThingCanDo() => UiThread.Run(() =>
    {
        var surface = Shown(Src);

        // The root's body leads somewhere, and says so rather than offering to fold anything.
        var body = Verbs(surface, Placed(surface, FlowchartPiece.Node).First());
        CollectionAssert.Contains(body, LayoutVerbs.Navigate);
        CollectionAssert.DoesNotContain(body, LayoutVerbs.Expand);

        // The folded child's chip offers what its press means, and nothing the node beneath it means.
        var chip = Verbs(surface, Placed(surface, MermaidPiece.Chip)[1]);
        CollectionAssert.Contains(chip, LayoutVerbs.Expand);
        CollectionAssert.DoesNotContain(chip, LayoutVerbs.Navigate);
    });

    [TestMethod]
    public void SomethingThatMeansNothingOffersNothingOfItsOwn() => UiThread.Run(() =>
    {
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n");

        var offered = Verbs(surface, Placed(surface, FlowchartPiece.Node).First());

        CollectionAssert.DoesNotContain(offered, LayoutVerbs.Navigate, "an ordinary node answers to nothing");
        CollectionAssert.DoesNotContain(offered, LayoutVerbs.Expand, "so only the document's own menu is offered");
    });

    /// <summary>What the menu offers where <paramref name="where"/> was pressed.</summary>
    private static List<string> Verbs(MarkdownSurface surface, Rect where) =>
        surface.Shown.BuildRibbon(Middle(where)) is DiagramRibbon ribbon
            ? [.. ribbon.Offers.Select(offer => offer.Verb)]
            : [];

    // ── Too many children ───────────────────────────────────────────────────

    /// <summary>A root with <paramref name="width"/> children, and a cap of three drawn at once.</summary>
    private static string Wide(int width)
    {
        var said = new System.Text.StringBuilder("---\nconfig:\n  nexaflow:\n    maxFanOut: 3\n---\ngraph TD\n");
        for (var at = 0; at < width; at++) said.Append($"  root[\"Root\"] --> c{at}[\"Child {at}\"]\n");
        return said.ToString();
    }

    [TestMethod]
    public void TooManyChildrenDrawTheFirstOfThemAndOneNodeOfferingTheRest() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(10));
        var words = Words(surface);

        Assert.IsTrue(words.Contains("Child 0") && words.Contains("Child 2"), "the first three stay on the page");
        Assert.IsFalse(words.Contains("Child 3"), "and the rest are held back");

        Assert.AreEqual(1, Placed(surface, MermaidPiece.More).Count, "one node offers them, however many there are");
        CollectionAssert.Contains(words, "+7 more");
    });

    [TestMethod]
    public void ASetOfChildrenWithinTheCapDrawsNoSuchNode() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(3));

        Assert.AreEqual(0, Placed(surface, MermaidPiece.More).Count);
        CollectionAssert.Contains(Words(surface), "Child 2");
    });

    [TestMethod]
    public void PressingItShowsEveryChild() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(10));

        Press(surface, Placed(surface, MermaidPiece.More).First());

        var words = Words(surface);
        Assert.IsTrue(words.Contains("Child 9"), "pressing it drew the ones that were held back");
        Assert.AreEqual(0, Placed(surface, MermaidPiece.More).Count, "and nothing is left to offer");
    });

    [TestMethod]
    public void ItIsDrawnAndNobodyWroteIt() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(10));
        var offering = surface.Shown.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.More);

        Assert.IsNull(offering.Part, "it stands for no part of the source, because there is none to stand for");
        Assert.AreEqual(LayoutVerbs.Expand, offering.Acts?.Click?.Verb, "and a press on it means what every chip means");
    });
}
