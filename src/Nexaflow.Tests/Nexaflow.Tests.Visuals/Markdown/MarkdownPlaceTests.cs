using System;
using System.Linq;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Where the page stands, said as a place in the source — what a host showing one document twice holds its two
/// halves together by (the markdown viewer's split).
///
/// <para>
/// A place in the source is the only thing two showings of a document agree on: the raw source wraps at its own
/// width and a rendered diagram is ten lines of source drawn as one picture, so neither a line number nor a
/// fraction of the way down carries across.
/// </para>
///
/// <para>Interactive desktop only — the page has nowhere to stand until it has been through a render pass. Run with
/// <c>--filter "TestCategory=Desktop"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // each case spins an off-screen Window; concurrent WPF layout makes the offsets flaky
public class MarkdownPlaceTests
{
    /// <summary>A document far taller than the page, with one paragraph of its own well down the middle of it.</summary>
    private static string Tall(out int marker)
    {
        var filler = string.Concat(Enumerable.Repeat("filler paragraph line\n\n", 120));
        var doc = "top of the document\n\n" + filler + "ZEBRA stands alone\n\n" + filler;

        marker = doc.IndexOf("ZEBRA", StringComparison.Ordinal);

        return doc;
    }

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void WhereThePageStandsIsSaidAsACharacterOfTheSource() => UiThread.Run(() =>
    {
        var doc = Tall(out var marker);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            Assert.AreEqual(0, editor.ShownFrom, "the top of a document is its first character");

            Assert.IsTrue(editor.ShowFrom(marker), "there is a place in the laid-out page for it to stand at");
            editor.UpdateLayout();

            Assert.AreEqual(marker, editor.ShownFrom, "and what was put at the top is what it says is at the top");
            Assert.IsTrue(MarkdownEditorHarness.Scrolled(editor).Offset > 0, "which is well down a document this tall");
        });
    });

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndItMovesToOneItIsAlreadyShowing() => UiThread.Run(() =>
    {
        var doc = Tall(out _);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            // The second paragraph is on the page from the start, so bringing it into view would move nothing.
            // Standing somewhere is not the same as being able to see it, which is the whole of the difference
            // between this and going to something that was found.
            var second = doc.IndexOf("filler", StringComparison.Ordinal);

            Assert.IsTrue(editor.ShowFrom(second));
            editor.UpdateLayout();

            Assert.AreEqual(second, editor.ShownFrom);
            Assert.IsTrue(MarkdownEditorHarness.Scrolled(editor).Offset > 0, "the page stands there now, not merely shows it");
        });
    });

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndACharacterItDrawsNothingForStandsWhereThatLineDoes() => UiThread.Run(() =>
    {
        // A heading's hashes are written but never drawn, so the page has no place for the '#' itself. It has to
        // stand at the heading all the same: a caret put there stands at the end of whatever came before, which
        // would leave a split showing one half a whole paragraph behind the other.
        var filler = string.Concat(Enumerable.Repeat("filler paragraph line\n\n", 120));
        var doc = "top of the document\n\n" + filler + "## ZEBRA\n\n" + filler;
        var hashes = doc.IndexOf("## ZEBRA", StringComparison.Ordinal);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            Assert.IsTrue(editor.ShowFrom(hashes));
            editor.UpdateLayout();

            Assert.AreEqual(hashes + "## ".Length, editor.ShownFrom,
                "the heading's own words are the first thing drawn from there on, and that is where it stands");
        });
    });

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndThePageSaysWheneverItHasMoved() => UiThread.Run(() =>
    {
        var doc = Tall(out var marker);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            var moved = 0;
            editor.PlaceChanged += (_, _) => moved++;

            editor.ShowFrom(marker);
            editor.UpdateLayout();

            Assert.IsTrue(moved > 0, "so a second showing of the same document can follow this one");
        });
    });

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndNowhereIsNotSomewhere() => UiThread.Run(() =>
    {
        MarkdownEditorHarness.Run(Tall(out _), editor =>
        {
            Assert.IsFalse(editor.ShowFrom(-1), "a half with nothing laid out yet says -1, and that is not a place");
            Assert.AreEqual(0d, MarkdownEditorHarness.Scrolled(editor).Offset, "so the page was left where it was");
        });
    });

    /// <summary>A flowchart drawn far taller than the ten lines it is written as, with prose either side of it.</summary>
    private static string Drawn(out int fence, out int after)
    {
        var filler = string.Concat(Enumerable.Repeat("filler paragraph line\n\n", 20));
        var doc = filler + string.Join("\n",
            "```mermaid",
            "flowchart TD",
            "  a[\"Alpha\"] --> b[\"Bravo\"]",
            "  b --> c[\"Charlie\"]",
            "  c --> d[\"Delta\"]",
            "  d --> e[\"Echo\"]",
            "  e --> f[\"Foxtrot\"]",
            "  f --> g[\"Golf\"]",
            "  g --> h[\"Hotel\"]",
            "```") + "\n\nafter the diagram\n\n" + filler;

        fence = doc.IndexOf("```mermaid", StringComparison.Ordinal);
        after = doc.IndexOf("after the diagram", StringComparison.Ordinal);

        return doc;
    }

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndWhatStandsOnItsTopRowIsWhatItSaysIsThere() => UiThread.Run(() =>
    {
        // A flowchart's nodes are drawn down the middle of the page, so nothing of it is near the top-left corner
        // where a page might be asked what it is showing. Taking the nearest thing to that corner named the
        // paragraph above the diagram instead — a block too early, and a split would show the wrong half of itself.
        var doc = Drawn(out var fence, out var after);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            Assert.IsTrue(editor.ShowFrom(fence));
            editor.UpdateLayout();

            var standing = editor.ShownFrom;
            Assert.IsTrue(standing >= fence && standing < after,
                $"the diagram is what it stands in, not what came before it: {standing}, with the fence at {fence}");
        });
    });

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndItNeverSaysALineItIsOnlyHalfShowing() => UiThread.Run(() =>
    {
        // A page comes to rest wherever the reader lets go of the scrollbar, which is never on a line boundary. What
        // it then says it stands at must be a line wholly on it: naming the one the top edge cuts through had a
        // second showing of the document display the whole of a paragraph this one had all but scrolled past, which
        // in a split reads as a block of daylight between the two halves.
        var doc = Drawn(out _, out _);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            var page = MarkdownEditorHarness.Drawn<System.Windows.Controls.ScrollViewer>(editor)!;

            foreach (var to in new[] { 137.0, 441.5, 802.3 })
            {
                page.ScrollToVerticalOffset(to);
                editor.UpdateLayout();

                var standing = editor.ShownFrom;
                Assert.IsTrue(standing >= 0, $"the page stands somewhere at {to}");

                Assert.IsTrue(editor.ShowFrom(standing), $"and can be put where it says it is: {to}");
                editor.UpdateLayout();

                // Down to the next whole line, or nowhere. Never up — up means it named a line it was cutting.
                Assert.IsTrue(page.VerticalOffset >= to - Hair,
                    $"it went back up to {page.VerticalOffset} from {to}, so it named a line it was only half showing");

                Assert.AreEqual(standing, editor.ShownFrom, $"and it stands at the place it named: {to}");

                var settled = page.VerticalOffset;
                Assert.IsTrue(editor.ShowFrom(editor.ShownFrom));
                editor.UpdateLayout();

                Assert.AreEqual(settled, page.VerticalOffset, Hair, $"and is settled there: {to}");
            }
        });
    });

    /// <summary>Closer than this and two places on the page are the one place.</summary>
    private const double Hair = 0.5;

    [TestMethod]
    [CoversNode("markdown-surface")]
    public void AndADrawingTallerThanThePageIsNotOneItShowsWhole() => UiThread.Run(() =>
    {
        // What a second showing of the document needs in order to know when following a line is pointless: a
        // diagram a page and a half tall has no one line that a line of its ten-line source answers to, and the
        // whole of that source is what a reader wants beside it. A sentence has lines worth following.
        var doc = Drawn(out var fence, out var after);

        MarkdownEditorHarness.Run(doc, editor =>
        {
            var diagram = editor.BlockAt(fence + "```mermaid".Length);
            Assert.AreEqual(fence, diagram.Start, "the fence is the block that line was written in");

            Assert.IsFalse(editor.ShowsWhole(diagram), "drawn taller than the page");
            Assert.IsTrue(editor.ShowsWhole(editor.BlockAt(after)), "and a sentence is not");
        });
    });
}
