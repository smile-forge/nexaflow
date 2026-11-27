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
    public void RangeMarks_AnswerOnlyForTheStretchAsked()
    {
        var marks = new RangeMarks(ContentMarkKind.Wash, "Token");
        marks.Set([(0, 3), (100, 5), (500, 2), (90, 40)]);

        var into = new List<ContentMark>();
        marks.Mark(new ContentMarkRequest(ContentDocument.Of(new string('x', 1000)), 95, 120), into);

        CollectionAssert.AreEquivalent(new[] { (90, 40), (100, 5) }, into.Select(mark => (mark.Start, mark.Length)).ToArray());
    }

    [TestMethod]
    public void RangeMarks_StayOverWhatTheyNamed_ThroughWritesOnEitherSide()
    {
        var document = new EditedDocument("alpha beta gamma");
        using var editor = new ContentEditor(document);
        var marks = new RangeMarks(ContentMarkKind.Wash, "Token");
        editor.Marks.Add(marks);
        marks.Set([(6, 4)]);

        document.Write(ContentChange.Write(0, 0, ">> "));
        document.Write(ContentChange.Write(document.Current.Length, 0, " delta"));

        var (start, length) = marks.Ranges.Single();
        Assert.AreEqual("beta", document.Current.Slice(start, length));
    }

    [TestMethod]
    public void AMarkWhoseCharactersWereAllTakenOut_IsGone()
    {
        var document = new EditedDocument("alpha beta gamma");
        using var editor = new ContentEditor(document);
        var marks = new RangeMarks(ContentMarkKind.Wash, "Token");
        editor.Marks.Add(marks);
        marks.Set([(6, 4), (11, 5)]);

        document.Write(ContentChange.Write(5, 5, string.Empty));

        Assert.AreEqual("gamma", document.Current.Slice(marks.Ranges.Single().Start, marks.Ranges.Single().Length));
    }

    [TestMethod]
    public void ContentLoadedInItsPlace_TakesEveryMark()
    {
        var document = new EditedDocument("one two");
        using var editor = new ContentEditor(document);
        var marks = new RangeMarks(ContentMarkKind.Wash, "Token");
        editor.Marks.Add(marks);
        marks.Set([(4, 3)]);

        document.Load("something else");

        Assert.AreEqual(0, marks.Ranges.Count);
    }

    [TestMethod]
    public void WhatWasFound_IsWashed_AndTheOneLookedAtOutlined()
    {
        var found = new FoundMarks();
        found.Show([(0, 2), (5, 2), (9, 2)], 1);

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
        var matches = new MatchMarks();
        editor.Marks.Add(matches);

        var asked = new List<string>();
        matches.Show((_, line) =>
        {
            asked.Add(line);
            var found = new List<(int, int)>();
            for (var at = line.IndexOf("foo", StringComparison.Ordinal); at >= 0; at = line.IndexOf("foo", at + 3, StringComparison.Ordinal)) found.Add((at, 3));
            return found;
        });
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

        matches.Show(null);
        Assert.IsTrue(matches.IsEmpty, "no search, nothing marked");
        Assert.IsNull(matches.LookedAt);
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
    public void WhatWasFound_StaysOnItsCharacters_ThroughWrites_AndIsAskedOnlyNearTheStretchDrawn()
    {
        var document = new EditedDocument("foo one foo two foo");
        using var editor = new ContentEditor(document);
        editor.Found.Show([(0, 3), (8, 3), (16, 3)], at: 1);

        document.Write(ContentChange.Write(0, 0, ">> "));

        CollectionAssert.AreEqual(new[] { (3, 3), (11, 3), (19, 3) }, editor.Found.Places.ToArray(), "each carried with its characters");
        Assert.IsTrue(editor.Found.Places.All(place => document.Current.Slice(place.Start, place.Length) == "foo"));
        Assert.AreEqual(1, editor.Found.At, "the one looked at is still the one looked at");

        var into = new List<ContentMark>();
        editor.Found.Mark(new ContentMarkRequest(document.Current, 12, 13), into);
        Assert.AreEqual(1, into.Count(mark => mark.Kind == ContentMarkKind.Wash), "only the place reaching into the stretch drawn");
    }

    [TestMethod]
    public void ARangeGrownByWritingInsideIt_IsStillMarked_WhereOnlyItsEndIsDrawn()
    {
        var document = new EditedDocument("0123456789abcdefghij");
        using var editor = new ContentEditor(document);
        var marks = new RangeMarks(ContentMarkKind.Wash, "Token");
        editor.Marks.Add(marks);
        marks.Set([(2, 5)]);

        document.Write(ContentChange.Write(4, 0, new string('x', 20)));
        Assert.AreEqual((2, 25), marks.Ranges.Single());

        // Drawn from well past where the range was ever as long as it was set.
        var into = new List<ContentMark>();
        marks.Mark(new ContentMarkRequest(document.Current, 20, 30), into);
        Assert.AreEqual(1, into.Count, "a range grown long reaches into a stretch past its old end");
    }

    [TestMethod]
    public void TheLayers_AreDrawnByTheirOrder_AndSayWhenAnyChanges()
    {
        using var editor = new ContentEditor(new EditedDocument("text"));
        var low = new RangeMarks(ContentMarkKind.LineWash, "Low", order: -5);
        var high = new RangeMarks(ContentMarkKind.Wash, "High", order: 5);
        var changed = 0;
        editor.Marks.Changed += (_, _) => changed++;

        editor.Marks.Add(high);
        editor.Marks.Add(low);
        high.Set([(0, 1)]);

        CollectionAssert.AreEqual(new ContentMarkLayer[] { low, high, editor.Found }, editor.Marks.ToArray());
        Assert.AreEqual(3, changed, "two layers laid on and one remarked");
    }
}
