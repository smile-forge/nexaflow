using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Choosing a node's shape and its colour from the ribbon a right-click opens on it — the only way a reader reaches either,
/// because the chart draws neither as words and so there is nowhere to put a caret in them.
///
/// <para>
/// Driven through a real <see cref="MarkdownSurface"/> and the ribbon it builds, because the whole claim is that a press on a
/// node offers them and a press on an option writes them: the offer has to reach the chart's own handler and the change has to
/// come back out as markdown, neither of which the chart does for itself.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("flowchart-writing")]
public class FlowchartChoiceTests
{
    /// <summary>A node written with brackets round its label, which say the shape it is drawn as.</summary>
    private const string Bracketed = "flowchart LR\n  a[\"Start\"] --> b\n  b --> c{Choose}\n";

    /// <summary>The same chart with a line already saying one node's shape.</summary>
    private const string Metadata = "flowchart LR\n  a@{ shape: cyl, label: \"Store\" }\n  a --> b\n";

    [TestMethod]
    public void AChartOffersEveryShapeOverANode_AndSaysWhichOneItIsDrawnAs() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            var shapes = Ribbon(editor, Node(chart, "Start")).Offers.Where(offer => offer.Group == "Shape").ToList();

            Assert.AreEqual(53, shapes.Count, "every shape Mermaid draws, which is every one but the one that is no shape");
            Assert.AreEqual(shapes.Count, shapes.Select(offer => offer.Verb).Distinct().Count(), "each offered under a name of its own");
            Assert.IsTrue(shapes.All(offer => offer.Tip is { Length: > 0 }), "each named, for whoever hovers or hears the screen read");

            // Every shape is drawn as itself, bar the one whose whole shape is having none, which is drawn as letters instead.
            Assert.IsTrue(shapes.All(offer => offer.Shape is { IsFrozen: true } drawn && !drawn.IsEmpty() || offer.Letters is { Length: > 0 }),
                          "each drawn as itself");
            CollectionAssert.AreEqual(new[] { "flowchart.shape.text" },
                                      shapes.Where(offer => offer.Shape is null).Select(offer => offer.Verb).ToArray());

            Assert.AreEqual("flowchart.shape.rect", shapes.Single(offer => offer.Current).Verb,
                            "the brackets it is written in say a rectangle");
        }));

    [TestMethod]
    public void AndEveryColourOfTheSharedBank_WithTheClearOneAmongThem() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            var colours = Ribbon(editor, Node(chart, "Start")).Offers.Where(offer => offer.Group == "Fill").ToList();

            Assert.AreEqual(13, colours.Count, "the twelve of the bank, and no colour at all");
            Assert.AreEqual(colours.Count, colours.Select(offer => offer.Verb).Distinct().Count(),
                            "each under a name of its own, so two the theme happens to tune alike are still two offers");
            Assert.IsTrue(colours.All(offer => offer.Shade is not null), "each one a colour, which is how the ribbon draws it");
            Assert.AreEqual("flowchart.fill.none", colours.Single(offer => offer.Current).Verb, "nothing styles it yet");
        }));

    [TestMethod]
    public void ChoosingAShapeTheBracketsCanSayIsWrittenInTheBrackets() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Click(Ribbon(editor, Node(chart, "Start")), "flowchart.shape.circle");

            StringAssert.Contains(editor.Markdown, "a((\"Start\")) --> b", editor.Markdown);
            Assert.AreEqual(0, Block(editor).Diagnostics.Count, "and the chart still reads");
            Assert.AreEqual("flowchart.shape.circle", Current(editor, "Start", "Shape"), "which is what it now says it is drawn as");
        }));

    [TestMethod]
    public void AndOneTheyCannotSayIsWrittenOnALineOfItsOwn() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            // A cloud is one of the shapes no pair of brackets says, so only a line about the node can say it.
            Click(Ribbon(editor, Node(chart, "Start")), "flowchart.shape.cloud");

            StringAssert.Contains(editor.Markdown, "a[\"Start\"] --> b", "what was written stays written: " + editor.Markdown);
            StringAssert.Contains(editor.Markdown, "a@{ shape: cloud }", "and the line after it says the shape: " + editor.Markdown);
            Assert.AreEqual(0, Block(editor).Diagnostics.Count);
            Assert.AreEqual("flowchart.shape.cloud", Current(editor, "Start", "Shape"));
        }));

    [TestMethod]
    public void AndWhereALineAlreadySaysTheShapeThatLineSaysTheNewOne() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Assert.AreEqual("flowchart.shape.cyl", Current(editor, "Store", "Shape"), "what the line says now");

            Click(Ribbon(editor, Node(chart, "Store")), "flowchart.shape.circle");

            StringAssert.Contains(editor.Markdown, "a@{ shape: circle, label: \"Store\" }", editor.Markdown);
            Assert.AreEqual(1, Times(editor.Markdown, "shape:"), "the one line that said it, given a new value");
            Assert.AreEqual(0, Block(editor).Diagnostics.Count);
        }, Metadata));

    [TestMethod]
    public void ChoosingAColourStylesThatNodeAndNoOther() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Click(Ribbon(editor, Node(chart, "Start")), "flowchart.fill.blue");

            StringAssert.Contains(editor.Markdown, "style a fill:#", "a style line of its own: " + editor.Markdown);
            Assert.AreEqual(0, Block(editor).Diagnostics.Count, "and the chart still reads");

            Click(Ribbon(editor, Node(Block(editor), "Start")), "flowchart.fill.red");

            Assert.AreEqual(1, Times(editor.Markdown, "style a fill:"), "chosen again, the one line is given a new value: " + editor.Markdown);
        }));

    [TestMethod]
    public void AndTheClearColourTakesTheFillBackOut_AndTheLineWithIt() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Click(Ribbon(editor, Node(chart, "Start")), "flowchart.fill.blue");
            Click(Ribbon(editor, Node(Block(editor), "Start")), "flowchart.fill.none");

            Assert.AreEqual(0, Times(editor.Markdown, "style a"), "nothing left for the line to say: " + editor.Markdown);
            StringAssert.Contains(editor.Markdown, "a[\"Start\"] --> b", "and the chart is as it was written");
            Assert.AreEqual(0, Block(editor).Diagnostics.Count);
        }));

    [TestMethod]
    public void NoneOfItIsOfferedOverWhatIsNotANode() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            var over = Groups(editor, Link(chart));

            CollectionAssert.DoesNotContain(over, "Shape", "a link is drawn in no shape and filled with nothing");
            CollectionAssert.DoesNotContain(over, "Fill");
        }));

    [TestMethod]
    public void AndNotOverASubgraphsOwnName() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            // A subgraph's name is written exactly as a node's is, and a link joins it by that name — but it is a box round nodes,
            // not a node of the chart, so neither a shape nor a fill means anything on it.
            var over = Groups(editor, Words(chart, "Work"));

            CollectionAssert.DoesNotContain(over, "Shape");
            CollectionAssert.DoesNotContain(over, "Fill");
        }, "flowchart LR\n  subgraph Work\n    a[\"Start\"]\n  end\n"));

    // ── Driving it ──────────────────────────────────────────────────────────

    /// <summary>Shows the chart inside a document, fenced, which is how one is written.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string diagram = Bracketed) =>
        MarkdownEditorHarness.Run("Below:\n\n```mermaid\n" + diagram + "```\n", editor =>
        {
            var chart = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(chart, "the chart did not render as content");

            test(editor, chart!);
        });

    /// <summary>The block as it stands now, which is another block after every edit.</summary>
    private static DocumentBlock Block(MarkdownSurface editor)
    {
        var chart = MarkdownEditorHarness.Block(editor);
        Assert.IsNotNull(chart, "the chart did not render as content");
        return chart!;
    }

    /// <summary>Where the node drawn with some words on it stands.</summary>
    private static Rect Node(DocumentBlock chart, string words) =>
        chart.Laid.Root.SelfAndDescendants()
             .First(piece => piece.Kind == FlowchartPiece.Node
                             && piece.SelfAndDescendants().Any(inner => inner.Words?.Glyphs.Text == words))
             .Bounds;

    /// <summary>Where some words the chart drew stand, whatever drew them.</summary>
    private static Rect Words(DocumentBlock chart, string words) =>
        chart.Laid.Root.SelfAndDescendants().First(piece => piece.Words?.Glyphs.Text == words).Bounds;

    /// <summary>Where the first link stands.</summary>
    private static Rect Link(DocumentBlock chart) =>
        chart.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == FlowchartPiece.Link).Bounds;

    /// <summary>The ribbon a right-click in the middle of somewhere opens, or nothing where it opens none.</summary>
    private static DiagramRibbon? Opened(MarkdownSurface editor, Rect over) =>
        editor.Shown.BuildRibbon(new Point(over.X + (over.Width / 2), over.Y + (over.Height / 2))) as DiagramRibbon;

    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over)
    {
        var ribbon = Opened(editor, over);
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    /// <summary>The choices a right-click offers there, by name.</summary>
    private static List<string> Groups(MarkdownSurface editor, Rect over) =>
        [.. (Opened(editor, over)?.Offers ?? []).Select(offer => offer.Group).OfType<string>().Distinct()];

    /// <summary>What the ribbon says a node is, of one of its choices.</summary>
    private static string? Current(MarkdownSurface editor, string words, string group) =>
        Ribbon(editor, Node(Block(editor), words)).Offers
            .FirstOrDefault(offer => offer.Group == group && offer.Current).Verb;

    /// <summary>Presses the ribbon's button for a verb.</summary>
    private static void Click(DiagramRibbon ribbon, string verb)
    {
        var button = Logical(ribbon).OfType<Button>()
            .Single(one => System.Windows.Automation.AutomationProperties.GetAutomationId(one) == "Diagram_Ribbon_" + verb);

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MarkdownEditorHarness.Pump();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject from)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(from).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var further in Logical(child)) yield return further;
        }
    }

    /// <summary>How many times something is written.</summary>
    private static int Times(string source, string said)
    {
        var count = 0;
        for (var at = source.IndexOf(said, StringComparison.Ordinal); at >= 0; at = source.IndexOf(said, at + 1, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>
    /// The point of drawing each shape rather than naming it is that a reader picks one out by eye, so two drawn alike are worse
    /// than useless — one of them cannot be chosen on purpose. Drawn and compared as pictures, because that is the claim: two
    /// shapes built from different geometry can still come out the same square.
    /// </summary>
    [TestMethod]
    public void NoTwoShapesAreOfferedAsTheSamePicture() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            var drawn = new Dictionary<string, string>();

            foreach (var offer in Ribbon(editor, Node(chart, "Start")).Offers.Where(offer => offer.Group == "Shape"))
            {
                var picture = Pictured(offer);

                Assert.IsFalse(drawn.TryGetValue(picture, out var already), $"{offer.Tip} is drawn exactly as {already} is");
                drawn[picture] = offer.Tip!;
            }

            Assert.AreEqual(53, drawn.Count, "every shape, each its own picture");
        }));

    /// <summary>What an option's face comes out as, drawn into the box a button gives it — its shape, or its letters.</summary>
    private static string Pictured(LayoutIntent offer)
    {
        const int pixels = 48;
        const double side = 16;

        var visual = new System.Windows.Media.DrawingVisual();

        using (var ink = visual.RenderOpen())
        {
            if (offer.Shape is { } shape)
            {
                ink.DrawGeometry(System.Windows.Media.Brushes.White, null, shape);
            }
            else
            {
                var typed = new System.Windows.Media.FormattedText(
                    offer.Letters!, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), side * 0.7, System.Windows.Media.Brushes.White, 1);

                ink.DrawText(typed, new Point(0, 0));
            }
        }

        var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(
            pixels, pixels, 96.0 * pixels / side, 96.0 * pixels / side, System.Windows.Media.PixelFormats.Pbgra32);

        shot.Render(visual);

        var dots = new byte[pixels * pixels * 4];
        shot.CopyPixels(dots, pixels * 4, 0);

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dots));
    }
}
