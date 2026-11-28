using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// Marks laid over a view of a document: asked only about the stretch drawn, carried through every change so each stays over what it
/// named, and saying when they change so they are drawn again — with nothing of a control in it.
/// </summary>
[TestClass]
[CoversNode("content-marks")]
public class ContentMarksTests
{
    [TestMethod]
    public void PlacesFound_AreAskedOnlyForTheStretchDrawn()
    {
        var marks = new FoundMarks();
        marks.Show([(0, 3), (100, 5), (500, 2), (90, 40)]);

        var into = new List<ContentMark>();
        marks.Mark(new ContentMarkRequest(ContentDocument.Of(new string('x', 1000)), 95, 120), into);

        CollectionAssert.AreEquivalent(new[] { (90, 40), (100, 5) }, into.Select(mark => (mark.Start, mark.Length)).ToArray());
    }

    [TestMethod]
    public void PlacesFound_StayOverWhatTheyNamed_ThroughWritesOnEitherSide()
    {
        var document = new EditedDocument("alpha beta gamma");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(6, 4)]);

        document.Write(ContentChange.Write(0, 0, ">> "));
        document.Write(ContentChange.Write(document.Current.Length, 0, " delta"));

        var (start, length) = editor.Found.Places.Single();
        Assert.AreEqual("beta", document.Current.Slice(start, length));
    }

    [TestMethod]
    public void AMarkWhoseCharactersWereAllTakenOut_IsGone()
    {
        var document = new EditedDocument("alpha beta gamma");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(6, 4), (11, 5)]);

        document.Write(ContentChange.Write(5, 5, string.Empty));

        Assert.AreEqual("gamma", document.Current.Slice(editor.Found.Places.Single().Start, editor.Found.Places.Single().Length));
    }

    [TestMethod]
    public void ContentLoadedInItsPlace_TakesEveryMark()
    {
        var document = new EditedDocument("one two");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(4, 3)], (4, 3));

        document.Load("something else");

        Assert.AreEqual(0, editor.Found.Places.Count);
        Assert.IsNull(editor.Found.LookedAt);
    }

    [TestMethod]
    public void WhatWasFound_IsWashed_AndTheOneLookedAtOutlined()
    {
        var found = new FoundMarks();
        found.Show([(0, 2), (5, 2), (9, 2)], (5, 2));

        var into = new List<ContentMark>();
        found.Mark(new ContentMarkRequest(ContentDocument.Of("ab ab ab ab"), 0, 11), into);

        Assert.AreEqual(3, into.Count(mark => mark.Kind == ContentMarkKind.Wash));
        Assert.AreEqual((5, 2), into.Where(mark => mark.Kind == ContentMarkKind.Outline).Select(mark => (mark.Start, mark.Length)).Single());
    }

    [TestMethod]
    public void WhatASearchMatches_IsWorkedOutOnlyOnTheLinesDrawn_AndTheOneLookedAtFollowsItsText()
    {
        var document = new EditedDocument("foo one\nbar\nfoo two\nfoo three\n");
        using var editor = new ContentEditor(document);
        var matches = editor.Found;

        var asked = new List<string>();
        matches.Show(new LineQuery(line =>
        {
            asked.Add(line);
            return Foo(line);
        }));
        matches.LookAt((12, 3));

        // Only the lines the stretch drawn touches are asked about: here the third and fourth.
        var into = new List<ContentMark>();
        matches.Mark(new ContentMarkRequest(document.Current, 13, 21), into);
        CollectionAssert.AreEqual(new[] { "foo two", "foo three" }, asked);
        CollectionAssert.AreEqual(new[] { 12, 20 }, into.Where(mark => mark.Kind == ContentMarkKind.Wash).Select(mark => mark.Start).ToArray());
        Assert.AreEqual(12, into.Single(mark => mark.Kind == ContentMarkKind.Outline).Start);

        // A write before it moves the match looked at with its characters.
        document.Write(ContentChange.Write(0, 0, ">> "));
        Assert.AreEqual((15, 3), matches.LookedAt);
        Assert.AreEqual("foo", document.Current.Slice(matches.LookedAt!.Value.Start, 3));

        matches.Clear();
        Assert.IsTrue(matches.IsEmpty, "no search, nothing marked");
        Assert.IsNull(matches.LookedAt);
    }

    [TestMethod]
    public void WhatASearchMatches_IsMarkedOnlyOnTheLinesItIsNarrowedTo_AndOnlyWhereTheLineIsOneItFinds()
    {
        var document = ContentDocument.Of("foo bar\nfoo\nfoo bar\nbar\n");
        var matches = new FoundMarks();

        // Found where both words are written; marked where either is, on the lines kept.
        matches.Show(new LineQuery(line => line.Contains("foo") && line.Contains("bar"), line => [.. Foo(line), .. Bar(line)]), line => line != 0);

        var into = new List<ContentMark>();
        matches.Mark(new ContentMarkRequest(document, 0, document.Length), into);

        CollectionAssert.AreEqual(new[] { (12, 3), (16, 3) }, into.Select(mark => (mark.Start, mark.Length)).ToArray(),
            "the third line alone: the first is not kept, and the second and fourth are not lines it finds");
    }

    [TestMethod]
    public void LineMarks_AreAskedOnlyOfTheLinesDrawn_AndMarkEachWholeLineTheyName()
    {
        var document = new EditedDocument("INFO start\nERROR broke\nINFO more\nERROR again\n");
        using var editor = new ContentEditor(document);
        var levels = new LineMarks(ContentMarkKind.LineWash, order: -20);
        editor.Marks.Add(levels);
        Assert.IsTrue(levels.IsEmpty);

        var asked = new List<int>();
        levels.Show((line, text) =>
        {
            asked.Add(line);
            return text.StartsWith("ERROR", StringComparison.Ordinal) ? "Log.Error" : null;
        });

        // From the middle of the second line to the start of the fourth.
        var into = new List<ContentMark>();
        levels.Mark(new ContentMarkRequest(document.Current, 14, 34), into);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, asked);
        CollectionAssert.AreEqual(new[] { (11, 11), (33, 11) }, into.Select(mark => (mark.Start, mark.Length)).ToArray(), "each line it names, which is washed across its rows");
        Assert.IsTrue(into.All(mark => mark is { Kind: ContentMarkKind.LineWash, Token: "Log.Error" }));
    }

    [TestMethod]
    public void EveryLayer_IsHandedTheLinesDrawnSlicedOnce_AndALineReadOnceForEveryLayerSharingTheReading()
    {
        var document = new EditedDocument("INFO start\nERROR broke\nINFO more\nERROR again\n");
        using var editor = new ContentEditor(document);
        var (levels, veil) = (new LineMarks(ContentMarkKind.LineWash, order: -20), new LineMarks(ContentMarkKind.Veil, order: 2000));
        editor.Marks.Add(levels);
        editor.Marks.Add(veil);

        var read = 0;
        var parsed = new LineReading<string>(text =>
        {
            read++;
            return text.Split(' ')[0];
        });
        levels.Show(parsed, (_, _, level) => level == "ERROR" ? "Log.Error" : null);
        veil.Show(parsed, (_, text, level) => level == "INFO" && text.EndsWith("more", StringComparison.Ordinal) ? "Log.Filter" : null);

        var request = new ContentMarkRequest(document.Current, 0, document.Current.Length);
        var sliced = request.Lines;
        var marks = editor.Marks.Mark(request);

        Assert.AreEqual(sliced.Count, read, "each line drawn read once, however many layers read it");
        Assert.AreSame(sliced, request.Lines, "the lines drawn sliced once for every layer");
        CollectionAssert.AreEqual(new[] { "Log.Error", "Log.Error", "Log.Filter" }, marks.Select(mark => mark.Token).ToArray());
    }

    [TestMethod]
    public void WhatWasFound_StaysOnItsCharacters_ThroughWrites_AndIsAskedOnlyNearTheStretchDrawn()
    {
        var document = new EditedDocument("foo one foo two foo");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(0, 3), (8, 3), (16, 3)], (8, 3));

        document.Write(ContentChange.Write(0, 0, ">> "));

        CollectionAssert.AreEqual(new[] { (3, 3), (11, 3), (19, 3) }, editor.Found.Places.ToArray(), "each carried with its characters");
        Assert.IsTrue(editor.Found.Places.All(place => document.Current.Slice(place.Start, place.Length) == "foo"));
        Assert.AreEqual((11, 3), editor.Found.LookedAt, "the one looked at is still the one looked at");

        var into = new List<ContentMark>();
        editor.Found.Mark(new ContentMarkRequest(document.Current, 12, 13), into);
        Assert.AreEqual(1, into.Count(mark => mark.Kind == ContentMarkKind.Wash), "only the place reaching into the stretch drawn");
    }

    [TestMethod]
    public void APlaceGrownByWritingInsideIt_IsStillMarked_WhereOnlyItsEndIsDrawn()
    {
        var document = new EditedDocument("0123456789abcdefghij");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(2, 5)]);

        document.Write(ContentChange.Write(4, 0, new string('x', 20)));
        Assert.AreEqual((2, 25), editor.Found.Places.Single());

        // Drawn from well past where the place was ever as long as it was found.
        var into = new List<ContentMark>();
        editor.Found.Mark(new ContentMarkRequest(document.Current, 20, 30), into);
        Assert.AreEqual(1, into.Count, "a place grown long reaches into a stretch past its old end");
    }

    [TestMethod]
    public void TheLayers_AreDrawnByTheirOrder_AndSayWhenAnyChanges()
    {
        using var editor = new ContentEditor(new EditedDocument("text"));
        var low = new LineMarks(ContentMarkKind.LineWash, order: -5);
        var high = new FoundMarks("High", order: 5);
        var changed = 0;
        editor.Marks.Changed += (_, _) => changed++;

        editor.Marks.Add(high);
        editor.Marks.Add(low);
        high.Show([(0, 1)]);

        CollectionAssert.AreEqual(new ContentMarkLayer[] { low, high, editor.Found }, editor.Marks.ToArray());
        Assert.AreEqual(3, changed, "two layers laid on and one remarked");
    }

    /// <summary>Where "foo" is written on <paramref name="line"/>.</summary>
    private static List<(int Index, int Length)> Foo(string line) => Written(line, "foo");

    /// <summary>Where "bar" is written on <paramref name="line"/>.</summary>
    private static List<(int Index, int Length)> Bar(string line) => Written(line, "bar");

    private static List<(int Index, int Length)> Written(string line, string word)
    {
        var found = new List<(int, int)>();
        for (var at = line.IndexOf(word, StringComparison.Ordinal); at >= 0; at = line.IndexOf(word, at + word.Length, StringComparison.Ordinal)) found.Add((at, word.Length));
        return found;
    }
}
