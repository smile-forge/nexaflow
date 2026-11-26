using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// Searching content laid line by line a line at a time, and again after an edit only where it wrote — always coming to what searching all
/// of it would — and a document known to read as it did without reading what it shares with the version it was made from.
/// </summary>
[TestClass]
[CoversNode("content-search")]
public class ContentSearchTests
{
    private static readonly Func<string, IReadOnlyList<(int Index, int Length)>> Fox =
        line => [.. Regex.Matches(line, "fox").Select(match => (match.Index, match.Length))];

    [TestMethod]
    public void EveryLine_IsSearched_AndEveryMatchStandsWhereItIsInTheDocument()
    {
        var document = ContentDocument.Of("a fox\nno\nfox and fox\r\nend");

        var found = ContentSearch.Lines(document, Fox);

        CollectionAssert.AreEqual(new[] { (2, 3), (9, 3), (17, 3) }, found);
        Assert.IsTrue(found.All(match => document.Slice(match.Start, match.Length) == "fox"));
    }

    [TestMethod]
    public void AfterAnEdit_WhatIsFoundAgain_IsWhatSearchingEverythingFinds()
    {
        var text = string.Concat(Enumerable.Range(0, 400).Select(line => line % 7 == 0 ? $"the fox {line}\n" : $"line {line}\n"));
        var before = ContentDocument.Of(text);
        var found = ContentSearch.Lines(before, Fox);

        foreach (var change in new[]
        {
            ContentChange.Write(text.IndexOf("line 3", StringComparison.Ordinal), 0, "fox "),
            ContentChange.Write(text.IndexOf("the fox 70", StringComparison.Ordinal) + 4, 3, "cat"),
            ContentChange.Write(text.IndexOf("line 100", StringComparison.Ordinal), "line 100\nline 101\n".Length, "a fox\nanother fox\nfox\n"),
        })
        {
            var after = before.Apply(change);
            var again = ContentSearch.Again(before, after, found, Fox);

            CollectionAssert.AreEqual(ContentSearch.Lines(after, Fox), again);
            (before, found) = (after, again);
        }
    }

    [TestMethod]
    public void FoundAgainThroughAStepTakenBack_IsWhatWasFoundBeforeIt()
    {
        var before = ContentDocument.Of("fox\nbird\nfox\n");
        var found = ContentSearch.Lines(before, Fox);
        var after = before.Apply(ContentChange.Write(4, 4, "fox"));

        var forward = ContentSearch.Again(before, after, found, Fox);
        var back = ContentSearch.Again(after, before, forward, Fox);

        CollectionAssert.AreEqual(found, back);
    }

    [TestMethod]
    public void ALongDocumentWrittenAndWrittenBack_ReadsAsItDid()
    {
        var text = new string('x', ContentDocument.RopeFrom * 4);
        var original = ContentDocument.Of(text);

        var written = original.Apply(ContentChange.Write(1000, 1, "y"));
        var back = written.Apply(ContentChange.Write(1000, 1, "x"));

        Assert.IsTrue(back.Equals(original));
        Assert.IsFalse(written.Equals(original));
    }
}
