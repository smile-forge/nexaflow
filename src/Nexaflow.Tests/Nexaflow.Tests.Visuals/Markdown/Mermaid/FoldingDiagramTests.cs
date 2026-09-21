using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The drawing half of nodes that fold: what is past the frontier is never drawn, what holds it carries a chip, and
/// the chip is a thing to press of its own — so a node whose body means something else goes on meaning it.
///
/// <para>
/// What a chip <em>means</em> is decided WPF-free in <c>DiagramExpansionTests</c>; this is that, drawn and pressed.
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

    /// <summary>The diagram as the markdown renderer shows it, measured and arranged.</summary>
    private static ContentElement Drawn(string source, DiagramRenderOptions options)
    {
        var element = (ContentElement)DiagramRenderer.Render("mermaid", source, options);
        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
        return element;
    }

    private static DiagramRenderOptions Options() => new() { Palette = StyleFormat.Dark };

    /// <summary>Every word drawn anywhere in it.</summary>
    private static List<string> Words(ContentElement element) =>
        [.. element.Laid.Root.SelfAndDescendants()
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .OfType<string>()];

    /// <summary>Where each piece of a kind was placed, in the order they were drawn.</summary>
    private static List<Rect> Placed(ContentElement element, string kind) =>
        [.. element.Laid.Tree.Root.Placed().Where(at => at.Piece.Kind == kind).Select(at => at.Where)];

    private static Point Middle(Rect where) => new(where.X + (where.Width / 2), where.Y + (where.Height / 2));

    [TestMethod]
    public void AnOrdinaryFlowchartDrawsNoChips() => UiThread.Run(() =>
    {
        var element = Drawn("graph TD\n  a[\"A\"] --> b[\"B\"]\n", Options());

        Assert.AreEqual(0, Placed(element, MermaidPiece.Chip).Count,
                        "a diagram that never mentions folding must grow no chips");
    });

    [TestMethod]
    public void WhatIsPastTheFrontierIsNotDrawnAndWhatHoldsItCarriesAChip() => UiThread.Run(() =>
    {
        var element = Drawn(Src, Options());
        var words = Words(element);

        Assert.IsTrue(words.Contains("Root") && words.Contains("Child"), "one level down is inside the frontier");
        Assert.IsFalse(words.Contains("Hidden"), "and the grandchild is past it");

        Assert.AreEqual(2, Placed(element, MermaidPiece.Chip).Count,
                        "the open root and the folded child each say so");
        Assert.IsTrue(words.Contains("+1"), "and the folded one says how much is behind it");
    });

    [TestMethod]
    public void WithNoHandlerAtAllTheDiagramOpensTheNodeItself() => UiThread.Run(() =>
    {
        // No OnExpand and no view state: a plain markdown flowchart is still explorable, because the source already
        // describes what is behind the chip.
        var element = Drawn(Src, Options());

        // The chips are drawn in the order the nodes are written, so the second is the folded child's.
        element.BeginPointerSelect(Middle(Placed(element, MermaidPiece.Chip)[1]));
        element.UpdateLayout();

        Assert.IsTrue(Words(element).Contains("Hidden"), "pressing the chip opened what was behind it, in place");
    });

    [TestMethod]
    public void ANodesBodyAndItsChipAreTwoIndependentTargets() => UiThread.Run(() =>
    {
        var followed = new List<string>();
        var opened = new List<DiagramExpandRequest>();

        var options = new DiagramRenderOptions
        {
            Palette = StyleFormat.Dark,
            OnNavigate = href => { followed.Add(href); return true; },
            OnExpand = asked => { opened.Add(asked); return true; },
        };

        var element = Drawn(Src, options);

        // The root's body: a press on it follows where its click line said it leads.
        var body = Placed(element, FlowchartPiece.Node).First();
        element.BeginPointerSelect(Middle(body));

        CollectionAssert.Contains(followed, "https://example.com/root",
                                  "the node body still leads where it said — folding did not take its press");

        // Its chip: a press on it asks for the node it belongs to, and nothing was followed by it.
        var chip = Placed(element, MermaidPiece.Chip).First();
        element.BeginPointerSelect(Middle(chip));

        Assert.AreEqual(1, followed.Count, "the chip is not the body");
        Assert.AreEqual(1, opened.Count, "and it asked about a node");
        Assert.AreEqual("root", opened[0].NodeId);
    });

    [TestMethod]
    public void AnOpeningIsWrittenDownBeforeTheHostIsOfferedIt() => UiThread.Run(() =>
    {
        // The host answers by re-emitting the whole diagram, so an opening made here has to survive that.
        var view = new DiagramViewState();
        var element = Drawn(Src, new DiagramRenderOptions
        {
            Palette = StyleFormat.Dark,
            ViewState = view,
            OnExpand = _ => true,
        });

        element.BeginPointerSelect(Middle(Placed(element, MermaidPiece.Chip)[1]));

        Assert.IsTrue(view.Expansion.ContainsKey("child"), "the key was recorded, whoever took the request on");
        Assert.IsTrue(view.Expansion["child"], "and recorded as opened");
    });

    [TestMethod]
    public void AClickLineIsFollowedOnOnePressWithNoChipInSight() => UiThread.Run(() =>
    {
        var followed = new List<string>();
        var element = Drawn("graph TD\n  a[\"A\"] --> b[\"B\"]\n  click a \"https://example.com/a\"\n",
                            new DiagramRenderOptions
                            {
                                Palette = StyleFormat.Dark,
                                OnNavigate = href => { followed.Add(href); return true; },
                            });

        element.BeginPointerSelect(Middle(Placed(element, FlowchartPiece.Node).First()));

        CollectionAssert.Contains(followed, "https://example.com/a",
                                  "a flowchart says where its nodes lead, and one press follows it");
    });

    [TestMethod]
    public void RightClickingOffersWhatThatOneThingCanDo() => UiThread.Run(() =>
    {
        var element = Drawn(Src, Options());

        // The root's body leads somewhere, and says so rather than offering to fold anything.
        CollectionAssert.AreEqual(new[] { LayoutVerbs.Navigate },
                                  Verbs(element, Placed(element, FlowchartPiece.Node).First()));

        // The folded child's chip offers what its press means, and nothing the node beneath it means.
        CollectionAssert.AreEqual(new[] { LayoutVerbs.Expand },
                                  Verbs(element, Placed(element, MermaidPiece.Chip)[1]));
    });

    [TestMethod]
    public void SomethingThatMeansNothingOffersNothing() => UiThread.Run(() =>
    {
        var element = Drawn("graph TD\n  a[\"A\"] --> b[\"B\"]\n", Options());

        Assert.IsNull(((Nexaflow.Visuals.Text.Editing.IEditableBlock)element)
                          .BuildRibbon(Middle(Placed(element, FlowchartPiece.Node).First())),
                      "an ordinary node answers to nothing, so the document's own menu opens instead");
    });

    /// <summary>What the menu offers where <paramref name="where"/> was pressed.</summary>
    private static List<string> Verbs(ContentElement element, Rect where) =>
        ((Nexaflow.Visuals.Text.Editing.IEditableBlock)element).BuildRibbon(Middle(where)) is DiagramRibbon ribbon
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
        var element = Drawn(Wide(10), Options());
        var words = Words(element);

        Assert.IsTrue(words.Contains("Child 0") && words.Contains("Child 2"), "the first three stay on the page");
        Assert.IsFalse(words.Contains("Child 3"), "and the rest are held back");

        Assert.AreEqual(1, Placed(element, MermaidPiece.More).Count, "one node offers them, however many there are");
        CollectionAssert.Contains(words, "+7 more");
    });

    [TestMethod]
    public void ASetOfChildrenWithinTheCapDrawsNoSuchNode() => UiThread.Run(() =>
    {
        var element = Drawn(Wide(3), Options());

        Assert.AreEqual(0, Placed(element, MermaidPiece.More).Count);
        CollectionAssert.Contains(Words(element), "Child 2");
    });

    [TestMethod]
    public void PressingItShowsEveryChild() => UiThread.Run(() =>
    {
        var element = Drawn(Wide(10), Options());

        element.BeginPointerSelect(Middle(Placed(element, MermaidPiece.More).First()));
        element.UpdateLayout();

        var words = Words(element);
        Assert.IsTrue(words.Contains("Child 9"), "pressing it drew the ones that were held back");
        Assert.AreEqual(0, Placed(element, MermaidPiece.More).Count, "and nothing is left to offer");
    });

    [TestMethod]
    public void ItIsDrawnAndNobodyWroteIt() => UiThread.Run(() =>
    {
        var element = Drawn(Wide(10), Options());
        var offering = element.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.More);

        Assert.IsNull(offering.Part, "it stands for no part of the source, because there is none to stand for");
        Assert.AreEqual(LayoutVerbs.Expand, offering.Acts?.Click?.Verb, "and a press on it means what every chip means");
    });
}
