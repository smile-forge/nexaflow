using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The whole document on one page: what a search turned up, where a reference leads, and what the host is
/// asked to do.
///
/// <para>
/// <strong>One element, not one per block.</strong> The prose, the diagrams and the tunes are all pieces of
/// a single laid tree, so a search looks in one place and a drag runs from a word into a chart with nothing
/// forwarding gestures between controls.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class MarkdownSurfaceTests
{
    private const string Doc =
        "# Getting Started\n\nSome words about chrome.\n\n"
        + "```mermaid\npie\n  \"chrome\" : 40\n  \"firefox\" : 12\n```\n\n"
        + "## Notes\n\n1. one\n1. two\n1. three\n";

    [TestMethod]
    public void ASearchLooksInOnePlaceAndFindsWhatIsInsideADiagramToo() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);

        Assert.AreEqual(2, surface.Find("chrome"), "once in the prose and once inside the chart");
        Assert.AreEqual(0, surface.At, "and it goes to the first straight away");
    });

    [TestMethod]
    public void AndFindsWhatIsOnlyDrawnAsWellAsWhatIsWritten() => UiThread.Run(() =>
    {
        // Every item was numbered 1. and the page says 1. 2. 3. — and the chart says 23.1%, which nobody
        // wrote either. Both are things a reader can see and neither is anywhere in the source.
        var surface = Shown(Doc);

        Assert.AreEqual(0, MarkdownFind.Every(Doc, "3.").Count, "nowhere does the source say it");

        var found = surface.Find("3.");

        Assert.IsTrue(found >= 1, "the marker drawn for the third item, at least");
        Assert.IsTrue(surface.Found.All(place => !Doc.Substring(place.Start, place.Length).Contains("3.")),
            "and every one of them stands for characters that say something else");
    });

    [TestMethod]
    public void SteppingThroughThemComesBackRoundBothWays() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);
        surface.Find("chrome");

        Assert.IsTrue(surface.Next());
        Assert.AreEqual(1, surface.At);

        Assert.IsTrue(surface.Next());
        Assert.AreEqual(0, surface.At, "past the last is the first again");

        Assert.IsTrue(surface.Previous());
        Assert.AreEqual(1, surface.At, "and before the first is the last");
    });

    [TestMethod]
    public void AndStoppingIsARepaintWithNothingToPutBack() => UiThread.Run(() =>
    {
        // Nothing in the tree was changed to mark a hit, so there is no state to get out of step with the
        // document — which is why a search survives the document being written in.
        var surface = Shown(Doc);
        surface.Find("chrome");

        surface.Stop();

        Assert.AreEqual(0, surface.Found.Count);
        Assert.AreEqual(-1, surface.At);
        Assert.AreEqual(0, surface.Shown.Showing.Places.Count);
    });

    [TestMethod]
    public void AndASearchThatFindsNothingSaysSoWithoutGoingAnywhere() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);

        Assert.AreEqual(0, surface.Find("nothing says this"));
        Assert.AreEqual(-1, surface.At);
        Assert.IsFalse(surface.Next());
    });

    [TestMethod]
    public void ShowingSomethingElseTakesTheSearchWithIt() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);
        surface.Find("chrome");

        surface.Markdown = "Something else entirely.\n";

        Assert.AreEqual(0, surface.Found.Count, "a search is about a document, and this is a different one");
        StringAssert.Contains(surface.Shown.Markdown, "Something else");
    });

    [TestMethod]
    public void AReferenceLeadsBackToWhatItNamedAfterTheDocumentHasMovedOn() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);

        Assert.IsTrue(surface.GoTo(ContentPath.Read("list/item#2")));
        Assert.IsTrue(surface.GoTo(ContentPath.Read("heading:notes")));

        // And as far as it still goes, where it no longer goes all the way.
        Assert.IsTrue(surface.GoTo(ContentPath.Read("list/item#40")));
        Assert.IsFalse(surface.GoTo(ContentPath.Read("nothing-is-called-this")));
    });

    [TestMethod]
    public void ALineNumberGoesToWhatWasDrawnForThatLine() => UiThread.Run(() =>
    {
        var surface = Shown(Doc);

        Assert.IsTrue(surface.GoTo(1));
        Assert.IsTrue(surface.GoTo(3));

        // A line inside a fence is a few pixels of a picture, so what is brought into view is the picture.
        Assert.IsTrue(surface.GoTo(7));

        Assert.IsTrue(surface.GoTo(900), "and a line that is no longer there lands at the end rather than throwing");
    });

    [TestMethod]
    public void CopyingIsSomethingTheHostIsAskedToDo() => UiThread.Run(() =>
    {
        // A clipboard is the application's, shared with every other thing in the window. The surface says
        // what would go on it; it does not reach out and put it there.
        var asked = new List<LayoutAct>();
        var surface = Shown(Doc, new Host(asked));

        var at = Middle(surface, "Some words");
        var copy = surface.Corner(at).Single(offer => offer.Verb == LayoutVerbs.Copy);

        surface.Raise(copy, at);

        Assert.AreEqual(1, asked.Count);
        Assert.AreEqual(LayoutVerbs.Copy, asked[0].Intent.Verb);
        StringAssert.Contains(asked[0].Intent.Target, "chrome", "with what would go on the clipboard already worked out");
    });

    [TestMethod]
    public void AndABlockOffersOnlyTheButtonsThatMeanAnythingForIt() => UiThread.Run(() =>
    {
        var surface = Shown("```csharp\nvar x = 1;\n```\n\nWords.\n");

        var code = surface.Corner(Middle(surface, "var")).Select(offer => offer.Verb).ToList();

        CollectionAssert.Contains(code, LayoutVerbs.Copy);
        CollectionAssert.DoesNotContain(code, LayoutVerbs.Save,
            "a picture of code is a worse copy of the code, and the language says so");
    });

    [TestMethod]
    public void AndProseOffersNoPictureEither() => UiThread.Run(() =>
    {
        var surface = Shown("Just some words.\n");

        var prose = surface.Corner(Middle(surface, "Just some")).Select(offer => offer.Verb).ToList();

        CollectionAssert.DoesNotContain(prose, LayoutVerbs.Save, "prose is not a picture of anything");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static MarkdownSurface Shown(string markdown, ILayoutActions? host = null)
    {
        var surface = new MarkdownSurface { Host = host, Markdown = markdown };

        surface.Measure(new Size(640, 2000));
        surface.Arrange(new Rect(0, 0, 640, 2000));
        surface.UpdateLayout();

        return surface;
    }

    /// <summary>
    /// The middle of whatever piece shows <paramref name="words"/>, which is where a pointer resting on it
    /// would be.
    /// </summary>
    /// <remarks>
    /// Give it something short. A run is one piece of one line set one way, and a stretch of code is split
    /// into a piece per token the moment a grammar has read it — so a phrase that was one run a moment ago is
    /// six of them once the colouring lands.
    /// </remarks>
    private static Point Middle(MarkdownSurface surface, string words)
    {
        var piece = surface.Shown.Laid.Root.SelfAndDescendants()
            .First(one => one.Words is { } said && said.Glyphs.Text.Contains(words));

        return new Point(piece.Bounds.X + (piece.Bounds.Width / 2), piece.Bounds.Y + (piece.Bounds.Height / 2));
    }

    private sealed class Host(List<LayoutAct> asked) : ILayoutActions
    {
        public bool Invoke(LayoutAct act)
        {
            asked.Add(act);

            return true;
        }

        public IReadOnlyList<LayoutIntent> Menu(LayoutAct act) => [];
    }
}
