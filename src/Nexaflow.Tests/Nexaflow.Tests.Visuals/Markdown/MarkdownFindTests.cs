using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Finding a place in a document: a search, a line number, and a reference somebody saved.
///
/// <para>
/// <strong>Looking is done in the source and never in the drawing.</strong> The parser only ever copies, so
/// the source is the one place the words are whole — in the drawing a label is broken wherever whatever drew
/// it needed a break, a lyric is split by the notes it is sung on, and a wrapped word is two runs. So the
/// offsets come out of the source and the tree turns them into places on the page, which is a thing it can
/// already do for every language at once.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-text")]
public class MarkdownFindTests
{
    private const string Doc =
        "# Getting Started\n\nSome words about chrome.\n\n"
        + "```mermaid\npie\n  \"chrome\" : 40\n  \"firefox\" : 12\n```\n\n"
        + "## Notes\n\n- one\n- two\n- three\n";

    // ── A search ────────────────────────────────────────────────────────────

    [TestMethod]
    public void ASearchFindsEveryPlaceAWordIsWritten()
    {
        var found = MarkdownFind.Every(Doc, "chrome");

        Assert.AreEqual(2, found.Count, "once in the prose and once inside the diagram");
        Assert.IsTrue(found.All(one => Doc.Substring(one.Start, one.Length).Equals("chrome", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AndFindsItInsideADiagramWhereNoAmountOfLookingAtThePageWould() => UiThread.Run(() =>
    {
        // The slice is drawn as a label, a share and a legend row, each its own piece and none of them the
        // word on its own. Looking in the source finds it; looking at the drawing would not.
        var laid = Laying.Lay(null, Doc, 480, StyleFormat.Dark);
        var inside = MarkdownFind.Every(Doc, "chrome")[1];

        Assert.IsTrue(laid.Root.RangeRects(inside.Start, inside.Length).Count > 0,
            "and the tree still says where on the page it came out");
    });

    [TestMethod]
    public void ASearchIsNotSomethingAReaderHasToMatchTheCaseOf()
    {
        Assert.AreEqual(2, MarkdownFind.Every(Doc, "CHROME").Count);
        Assert.AreEqual(0, MarkdownFind.Every(Doc, "nothing says this").Count);
        Assert.AreEqual(0, MarkdownFind.Every(Doc, "").Count);
        Assert.AreEqual(0, MarkdownFind.Every(null, "chrome").Count);
    }

    [TestMethod]
    public void AndOverlappingWordsAreEachTheirOwnPlace()
    {
        // aaa holds "aa" twice, and a reader stepping through hits wants both.
        Assert.AreEqual(2, MarkdownFind.Every("aaa", "aa").Count);
    }

    [TestMethod]
    public void WhatIsDrawnButNeverWrittenIsFoundToo() => UiThread.Run(() =>
    {
        // A writer who numbered every line 1. meant a list, and the page says 1. 2. 3. — so a reader looking
        // for the third item searches for what they can see, and the source has never said it.
        const string source = "1. one\n1. two\n1. three\n";
        var laid = Laying.Lay(null, source, 480, StyleFormat.Dark);

        Assert.AreEqual(0, MarkdownFind.Every(source, "3.").Count, "nowhere in the source does it say 3.");

        var third = MarkdownFind.Shown(laid, "3.").Single();

        Assert.AreEqual("1. ", source.Substring(third.Start, third.Length),
            "and the place given back is the marker it stands for, so the hit reads like any other");
    });

    [TestMethod]
    public void AndNothingHadToBeToldWhichConstructsThoseAre() => UiThread.Run(() =>
    {
        // A run already says whether what is drawn is what was written, because that is what makes a caret
        // possible inside it. The ones that say no are exactly the ones to look at — whatever they are, and
        // whatever language drew them.
        var laid = Laying.Lay(null, "> [!CAUTION]\n> Mind out.\n\na &amp; b and not \\*this\\*\n", 480, StyleFormat.Dark);

        Assert.AreEqual(1, MarkdownFind.Shown(laid, "Caution").Count, "an alert's label");
        Assert.AreEqual(1, MarkdownFind.Shown(laid, "&").Count, "an entity");
        Assert.AreEqual(2, MarkdownFind.Shown(laid, "*").Count, "an escape");
    });

    [TestMethod]
    public void AndASearchIsBothAtOnceWithEachPlaceOnlyOnce() => UiThread.Run(() =>
    {
        const string source = "Item 2 is below.\n\n1. one\n1. two\n";
        var laid = Laying.Lay(null, source, 480, StyleFormat.Dark);

        var found = MarkdownFind.In(laid, source, "2");

        Assert.AreEqual(2, found.Count, "the one in the sentence, and the marker drawn for the second item");
        CollectionAssert.AreEqual(found.OrderBy(place => place.Start).ToArray(), found.ToArray(),
            "in the order a reader comes to them");
    });

    // ── A line number ───────────────────────────────────────────────────────

    [TestMethod]
    public void ALineNumberIsWhereThatLineBeginsAndHowLongItIs()
    {
        const string source = "one\ntwo\nthree\n";

        Assert.AreEqual((0, 3), MarkdownFind.Line(source, 1));
        Assert.AreEqual((4, 3), MarkdownFind.Line(source, 2));
        Assert.AreEqual((8, 5), MarkdownFind.Line(source, 3));
    }

    [TestMethod]
    public void AndALineThatIsNoLongerThereIsTheLastLineThereIs()
    {
        // A reference to line 900 of a file somebody has since cut short should land at the end of what is
        // left — which is a line with something on it, not the empty place after the last one.
        const string source = "one\ntwo\n";

        Assert.AreEqual(MarkdownFind.Line(source, 2), MarkdownFind.Line(source, 900));
        Assert.AreEqual("two", source.Substring(MarkdownFind.Line(source, 900).Start, MarkdownFind.Line(source, 900).Length));

        Assert.AreEqual(MarkdownFind.Line(source, 1), MarkdownFind.Line(source, 0), "and there is no line nought");
        Assert.AreEqual((0, 0), MarkdownFind.Line("", 1), "nor any line at all in nothing");
    }

    [TestMethod]
    public void AndTheSameOffsetSaysWhichLineItIsOnGoingBack()
    {
        const string source = "one\ntwo\nthree\n";

        Assert.AreEqual(1, MarkdownFind.LineAt(source, 0));
        Assert.AreEqual(2, MarkdownFind.LineAt(source, 5));
        Assert.AreEqual(3, MarkdownFind.LineAt(source, 9));
    }

    [TestMethod]
    public void ALineEndingIsNotPartOfTheLineItEnds()
    {
        Assert.AreEqual((0, 3), MarkdownFind.Line("one\r\ntwo\r\n", 1), "nor is the carriage return before it");
    }

    // ── A saved reference ───────────────────────────────────────────────────

    [TestMethod]
    public void AReferenceSaysWhatSomethingIsRatherThanWhereItHappensToSit()
    {
        var read = Read(Doc);
        var third = Item(read, 2);

        var path = ContentPath.Of(third);

        StringAssert.Contains(path.ToString(), "list");
        StringAssert.Contains(path.ToString(), "item#2");
    }

    [TestMethod]
    public void AndFollowingItLandsBackOnTheSameThing()
    {
        var read = Read(Doc);
        var third = Item(read, 2);

        var found = ContentPath.Of(third).In(read);

        Assert.IsNotNull(found);
        Assert.AreEqual(third.Start, found!.Start);
    }

    [TestMethod]
    public void AndItStillLandsThereAfterTheDocumentHasBeenWrittenInAbove()
    {
        // The whole point of not saving a line number: everything has moved and the third item of the list
        // is still the third item of the list.
        var path = ContentPath.Of(Item(Read(Doc), 2));

        var edited = "Another paragraph entirely.\n\nAnd one more.\n\n" + Doc;
        var found = path.In(Read(edited));

        Assert.IsNotNull(found);
        Assert.AreEqual("three", found!.Print().Trim().TrimStart('-', ' '));
    }

    [TestMethod]
    public void AReferenceReadsBackFromHowItWasWrittenDown()
    {
        // The same shape a snaplink names a declaration with, because it is the same question asked of a
        // different tree: what sort of thing, then its name or its place.
        var path = ContentPath.Read("list/item#2");

        Assert.AreEqual(2, path.Steps.Count);
        Assert.AreEqual("list", path.Steps[0].Kind);
        Assert.AreEqual(2, path.Steps[1].Index);

        Assert.AreEqual("heading:getting-started", ContentPath.Read("heading:getting-started").ToString());
        Assert.AreEqual(0, ContentPath.Read("").Steps.Count);
        Assert.AreEqual(0, ContentPath.Read(null).Steps.Count);
    }

    [TestMethod]
    public void AndAReferenceThatNoLongerLeadsAllTheWayLandsAsFarAsItDoes()
    {
        // A deep link into a section somebody has reorganised should still land in the section, which is
        // the difference between a reference that ages and one that breaks.
        var read = Read(Doc);
        var found = ContentPath.Read("list/item#40").In(read);

        Assert.IsNotNull(found);
        Assert.AreEqual(MarkdownKinds.List, found!.Kind, "as far as it got, which is the list");

        Assert.IsNull(ContentPath.Read("nothing-is-called-this").In(read), "and nothing at all where it never started");
    }

    [TestMethod]
    public void AndItReachesInsideATableAndInsideADiagramWithNothingAddedForEither()
    {
        // The kinds are open strings and the walk is one walk, so pointing at a cell or at a slice needed no
        // more code than pointing at a heading did.
        var table = Read("| a | b |\n|---|---|\n| one | two |\n");
        var cell = ContentPath.Read("table/row#1/cell#1").In(table);

        Assert.IsNotNull(cell);
        StringAssert.Contains(cell!.Print(), "two");

        var chart = Read("```mermaid\npie\n  \"chrome\" : 40\n  \"firefox\" : 12\n```\n");
        var slice = ContentPath.Of(chart.SelfAndDescendants().First(part => part.Kind == MarkdownKinds.Fence));

        Assert.IsNotNull(ContentPath.Read(slice.ToString()).In(chart));
    }

    [TestMethod]
    public void AName​IsKeptWholeHoweverItWasWritten()
    {
        // A path is written with / and :, so a name holding either is put beyond their use and comes back
        // as what it was.
        var step = new ContentStep("slice", "a/b:c");

        Assert.AreEqual(step, ContentStep.Read(step.ToString()));
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static ContentPart Read(string source) =>
        ContentPart.Of(MarkdownParser.Parsing()(source).Tree);

    private static ContentPart Item(ContentPart root, int at) =>
        root.SelfAndDescendants().Where(part => part.Kind == MarkdownKinds.Item).ElementAt(at);
}
