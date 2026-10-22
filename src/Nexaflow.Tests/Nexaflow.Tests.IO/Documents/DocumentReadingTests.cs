using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A document as whole-document work reads it: its outline kept to what fits, the blocks a reader reads, and text too long to
/// hand a model at once cut where a reader would pause — nothing lost and nothing added.
/// </summary>
[TestClass]
[CoversNode("documents-model")]
public class DocumentReadingTests
{
    [TestMethod]
    public void AnOutline_LeavesOutTheDeepestHeadingsFirst_ToFit()
    {
        var reading = DocumentReading.Of("# Animals\n\n## Dogs\n\n### Puppies\n\ntext\n\n## Cats\n");

        StringAssert.Contains(reading.Outline(500), "    - Puppies");

        var tight = reading.Outline(30);
        Assert.IsFalse(tight.Contains("Puppies"), "the deepest goes first");
        StringAssert.Contains(tight, "  - Dogs");
        StringAssert.Contains(tight, "  - Cats");
    }

    [TestMethod]
    public void WhatAReaderReads_LeavesOutFrontMatterAndLinkDefinitions()
    {
        var reading = DocumentReading.Of("---\ntitle: x\n---\nWords.\n\n[a]: https://a.example\n");

        CollectionAssert.AreEqual(new[] { "Words." }, reading.Blocks(0, reading.Root.Children.Count).Select(DocumentReading.Written).ToArray());
        StringAssert.Contains(reading.FrontMatter, "title: x");
    }

    [TestMethod]
    public void TextTooLong_IsCutAtTheEndsOfItsSentences_AndNothingIsLost()
    {
        var text = string.Concat(Enumerable.Range(1, 40).Select(n => $"Sentence number {n} is here. "));
        var cut = DocumentCuts.Cut(text, 100);

        Assert.AreEqual(text, string.Concat(cut));
        Assert.IsTrue(cut.All(piece => piece.Length <= 100));
        Assert.IsTrue(cut.SkipLast(1).All(piece => piece.EndsWith(". ", StringComparison.Ordinal)), "each piece ends where a sentence does");
    }

    [TestMethod]
    public void WhatIsReadOfALongDocument_IsFromAllThroughIt()
    {
        var spread = DocumentCuts.Spread(20, 5);

        Assert.AreEqual(5, spread.Count);
        Assert.AreEqual(0, spread[0]);
        Assert.AreEqual(19, spread[^1]);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DocumentCuts.Spread(3, 5).ToArray(), "a short one is read whole");
    }
}
