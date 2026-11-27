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
        matches.Show(line =>
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
