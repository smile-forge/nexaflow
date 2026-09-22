using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// Links: where one goes, who answers it, and the host's say in how it looks.
///
/// <para>
/// <strong>A link into this document is never the host's.</strong> Nobody else can answer it — the heading
/// is on this page, laid out by this element — so it is answered here and not offered onwards. Everything
/// else is the host's, and the document says only where it points.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-link-decorator")]
public class MarkdownLinkTests
{
    [TestMethod]
    public void ALinkSaysWhereItGoesAndNothingAboutWhatGoingThereMeans() => UiThread.Run(() =>
    {
        var laid = Lay("see [the page](https://example.org/x)\n");

        var act = Acts(laid).Single();

        Assert.AreEqual(LayoutVerbs.Navigate, act.Verb);
        Assert.AreEqual("https://example.org/x", act.Target, "the url as written, not as this renderer would write it");
    });

    [TestMethod]
    public void AHeadingAnswersToTheNameALinkCanPointAt() => UiThread.Run(() =>
    {
        var laid = Lay("## Getting Started\n\nwords\n");

        Assert.IsTrue(MarkdownAnchors.Sought(laid, "getting-started").Exists);
        Assert.IsTrue(MarkdownAnchors.Sought(laid, "Getting-Started").Exists, "a name is not case a reader has to match");

        Assert.IsFalse(MarkdownAnchors.Sought(laid, "nothing-is-called-this").Exists);
    });

    [TestMethod]
    public void AndTwoHeadingsSayingTheSameThingAreStillToldApart() => UiThread.Run(() =>
    {
        // Which one a name means is settled by reading the whole document, and nothing looking at one
        // heading's characters could see it.
        var laid = Lay("## Notes\n\none\n\n## Notes\n\ntwo\n");

        var first = MarkdownAnchors.Sought(laid, "notes");
        var second = MarkdownAnchors.Sought(laid, "notes-1");

        Assert.IsTrue(first.Exists);
        Assert.IsTrue(second.Exists);
        Assert.IsTrue(second.Bounds.Y > first.Bounds.Y);
    });

    [TestMethod]
    public void AnInPageLinkIsTheViewsToResolveAndNeverTheHosts() => UiThread.Run(() =>
    {
        var asked = new List<string>();
        var element = Element("## Getting Started\n\nsee [the start](#getting-started) and [the web](https://example.org)\n",
                              asked);

        Press(element, "the start");
        CollectionAssert.AreEqual(System.Array.Empty<string>(), asked,
            "the host is never told about a link into this document, because the host could not answer it");

        Press(element, "the web");
        CollectionAssert.AreEqual(new[] { "https://example.org" }, asked, "everything else is the host's");
    });

    [TestMethod]
    public void AndANameNothingIsCalledIsStillNotTheHostsToAnswer() => UiThread.Run(() =>
    {
        // A link into this document stays this document's even when the section it named has been deleted.
        // Handing it on would have the host try to open a page called #gone.
        var asked = new List<string>();
        var element = Element("words [somewhere](#gone) here\n", asked);

        Press(element, "somewhere");

        CollectionAssert.AreEqual(System.Array.Empty<string>(), asked);
    });

    [TestMethod]
    public void TheHostHasItsSayInHowALinkLooksWithoutTouchingTheWords() => UiThread.Run(() =>
    {
        // What the help pane does with a locate: link — put a pin in front of it so a reader can tell it
        // points at the screen, and leave the words the writer wrote exactly as they were.
        var laid = Lay("[the Help button](locate:Chrome_HelpButton) and [the web](https://example.com/x)\n",
                       (where, _) => where.StartsWith("locate:")
                           ? new LinkLook(Brushes.Orange, Before: "\U0001F4CD", After: "\u29c9")
                           : null);

        var drawn = string.Concat(Words(laid).Select(piece => piece.Words!.Glyphs.Text));

        StringAssert.Contains(drawn, "the Help button", "the words are the writer's and are not disturbed");
        StringAssert.Contains(drawn, "\U0001F4CD", "with the mark the host asked for in front of them");
        StringAssert.Contains(drawn, "\u29c9", "and the one it asked for behind");

        Assert.IsTrue(drawn.IndexOf("\U0001F4CD", StringComparison.Ordinal) < drawn.IndexOf("the Help button", StringComparison.Ordinal),
            "in front means in front");
    });

    [TestMethod]
    public void AndTheHostIsOfferedTheUrlAsWrittenWithTheWordsItWasWrittenAs() => UiThread.Run(() =>
    {
        var offered = new List<(string Where, string Words)>();

        Lay("[the Help button](locate:Chrome_HelpButton), [the web](https://example.com/x) and <https://example.com/auto>\n",
            (where, words) =>
            {
                offered.Add((where, words));

                return null;
            });

        CollectionAssert.AreEqual(new[] { "locate:Chrome_HelpButton", "https://example.com/x", "https://example.com/auto" },
                                  offered.Select(one => one.Where).ToArray());

        CollectionAssert.AreEqual(new[] { "the Help button", "the web", "https://example.com/auto" },
                                  offered.Select(one => one.Words).ToArray(),
                                  "offered the words too, so a host can decide by what a link says");
    });

    [TestMethod]
    public void AHostThatThrewCostsThatLinkItsLookAndNothingElse() => UiThread.Run(() =>
    {
        var laid = Lay("[one](a) and [two](b)\n", (_, _) => throw new System.InvalidOperationException("host bug"));

        var drawn = string.Concat(Words(laid).Select(piece => piece.Words!.Glyphs.Text));

        StringAssert.Contains(drawn, "one");
        StringAssert.Contains(drawn, "two");
        Assert.AreEqual(2, Acts(laid).Count, "and both still go where they were written to go");
    });

    [TestMethod]
    public void AndAHostWithNothingToSayLeavesEveryLinkAsItWasWritten() => UiThread.Run(() =>
    {
        const string source = "[the Help button](locate:Chrome_HelpButton), [the web](https://example.com/x) and <https://example.com/auto>\n";

        var laid = Lay(source, null);
        var drawn = string.Concat(Words(laid).Select(piece => piece.Words!.Glyphs.Text));

        Assert.AreEqual(3, Acts(laid).Count, "three links, going where they were written to go");
        StringAssert.Contains(drawn, "the Help button");
        StringAssert.Contains(drawn, "https://example.com/auto", "a bare url is its own words");
    });

    [TestMethod]
    public void AndTheViewHandsItsHookOnToWhatItIsShowing() => UiThread.Run(() =>
    {
        // The hook is the host's and the asking is the stage's, so the one thing the view has to do is carry
        // it between them — which is the thing that quietly stops working when a surface is swapped underneath.
        var offered = new List<string>();

        var view = new SelectableMarkdownView
        {
            LinkDecorator = (where, _) =>
            {
                offered.Add(where);

                return null;
            },
        };

        view.Markdown = "[one](a) and [two](b)\n";

        // The stage is asked while the document is being laid out, so nothing has asked it until something has.
        view.Measure(new System.Windows.Size(640, 2000));
        view.Arrange(new System.Windows.Rect(0, 0, 640, 2000));
        view.UpdateLayout();

        CollectionAssert.AreEqual(new[] { "a", "b" }, offered);
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static Laid Lay(string source, System.Func<string, string, LinkLook?>? asked = null) =>
        MarkdownBuilder.Lay(source, StyleFormat.Dark, 480,
                            reader: MarkdownParser.Reader.Then(new WithLinks(asked)));

    private static List<Piece> Words(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null)];

    private static List<LayoutIntent> Acts(Laid laid) =>
        [.. laid.Root.SelfAndDescendants()
                 .Select(piece => piece.Acts?.Click)
                 .Where(act => act is not null)
                 .Select(act => act!.Value)
                 .Distinct()];

    private static MarkdownElement Element(string source, List<string> asked) =>
        new(source, StyleFormat.Dark, new Host(asked)) { IsReadOnly = true };

    /// <summary>Presses the words of a link, the way the element's own mouse handler does.</summary>
    private static void Press(MarkdownElement element, string words)
    {
        element.Measure(new System.Windows.Size(480, 2000));
        element.Arrange(new System.Windows.Rect(0, 0, 480, 2000));

        var piece = element.Laid.Root.SelfAndDescendants()
            .First(one => one.Words is { } said && said.Glyphs.Text.Contains(words));

        element.BeginPointerSelect(new System.Windows.Point(piece.Bounds.X + (piece.Bounds.Width / 2),
                                                            piece.Bounds.Y + (piece.Bounds.Height / 2)));
        element.EndPointerSelect();
    }

    private sealed class Host(List<string> asked) : ILayoutActions
    {
        public bool Invoke(LayoutAct act)
        {
            if (act.Intent.Target is { } where) asked.Add(where);

            return true;
        }

        public IReadOnlyList<LayoutIntent> Menu(LayoutAct act) => [];
    }
}
