using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// A large document is read a stretch at a time (<see cref="ContentStretch"/>), each stretch stopping softly where nothing is
/// left open — and the stretches, stitched together (<see cref="ContentChunks"/>), must be exactly the document read whole:
/// the same pieces, standing for the same characters, wherever the stretches happened to stop.
/// </summary>
[TestClass]
[CoversNode("windowed-reading")]
public class WindowedReadingTests
{
    /// <summary>A stretch as short as can be, which stops at every place there is to stop; one a few blocks long; one most samples fit in.</summary>
    private static readonly int[] Lengths = [1, 64, 4096];

    [TestMethod]
    public void EveryMarkdownSample_ReadAStretchAtATime_IsTheSampleReadWhole()
    {
        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
            Stitched(File.ReadAllText(path), Path.GetFileName(path), MarkdownParser.Language, MarkdownParser.Windowed,
                     source => MarkdownParser.Parsing()(source).Tree);
    }

    [TestMethod]
    public void EveryRtfSample_ReadAStretchAtATime_IsTheSampleReadWhole()
    {
        foreach (var path in TestSampleData.Files("rtf"))
            Stitched(File.ReadAllText(path, System.Text.Encoding.Latin1), Path.GetFileName(path), RtfParser.Language,
                     RtfParser.Windowed, RtfParser.Parse);
    }

    [TestMethod]
    public void AnRtfStretch_StopsBetweenParagraphs_AndTheNextReadsInWhatTheyWereSetIn()
    {
        const string Source = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}{\f1 Courier;}}{\colortbl;\red200\green0\blue0;}" +
                              @"\f1\cf1\b one\par {\i two\par three\par} four\b0\par five\par}";

        var chunks = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);

        Assert.IsTrue(chunks.Chunks.Count >= 4, $"Read in {chunks.Chunks.Count} stretch(es).");
        Assert.IsTrue(chunks.Tree().Same(RtfParser.Parse(Source)));

        // Every stretch but the last stops just past a paragraph mark, and the space that ends the word.
        foreach (var chunk in chunks.Chunks.Take(chunks.Chunks.Count - 1))
            StringAssert.EndsWith(Source[..chunk.Stopped!.Value].TrimEnd(), @"\par");
    }

    [TestMethod]
    public void AnRtfStretch_NeverStopsInsideATable()
    {
        const string Source = @"{\rtf1 before\par\trowd\cellx1000\cellx2000\intbl a\cell b\cell\row" +
                              @"\trowd\cellx1000\cellx2000\intbl c\cell d\cell\row\pard after\par end\par}";

        var chunks = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);
        var table = Source.IndexOf(@"\trowd", StringComparison.Ordinal);
        var after = Source.IndexOf(@"\pard after", StringComparison.Ordinal);

        Assert.IsFalse(chunks.Chunks.Any(chunk => chunk.Stopped > table && chunk.Stopped <= after),
                       "A stretch stopped inside the table.");
        Assert.IsTrue(chunks.Tree().Same(RtfParser.Parse(Source)));
    }

    [TestMethod]
    public void AMarkdownStretch_ReadsAContainerLongerThanItselfWhole()
    {
        var fence = "```\n" + string.Concat(Enumerable.Range(0, 200).Select(line => $"line {line}\n")) + "```\n";
        var source = "# Title\n\n" + fence + "\nAfter.\n\nMore.\n";

        var chunks = Read(source, MarkdownParser.Language, MarkdownParser.Windowed, 16);
        var fenced = source.IndexOf("```", StringComparison.Ordinal);

        Assert.IsFalse(chunks.Chunks.Any(chunk => chunk.Stopped > fenced && chunk.Stopped < fenced + fence.Length),
                       "A stretch stopped inside the fence.");
        Assert.IsTrue(chunks.Tree().Same(MarkdownParser.Parsing()(source).Tree));
    }

    [TestMethod]
    public void ALinkDefinedFarBelow_IsReadAsALink_OnceTheWholeDocumentHasBeenRead()
    {
        var filler = string.Concat(Enumerable.Range(0, 40).Select(at => $"Paragraph {at} of filler.\n\n"));
        var source = "See [the site][site] for more.\n\n" + filler + "[site]: https://example.com\n";

        var chunks = new ContentChunks(source, MarkdownParser.Language);
        var parse = MarkdownParser.Windowed();

        while (chunks.Next(64) is { } next) chunks.Add(ContentChunk.Read(parse, source, next.From, next.SoftMaxLength, next.Resume, null));

        // The first stretch was read before the definition was, so it holds no link yet — and is the one to read again.
        Assert.IsFalse(Links(chunks.Chunks[0].Tree));
        CollectionAssert.Contains(chunks.Unsettled().ToList(), 0);

        Settled(chunks, parse, 64);

        Assert.IsTrue(Links(chunks.Chunks[0].Tree));
        var whole = MarkdownParser.Parsing()(source).Tree;
        Assert.IsTrue(chunks.Tree().Same(whole), Diverged(whole, chunks.Tree()));
    }

    [TestMethod]
    public void TwoReadingsStoppingInTheSameState_SayTheirStatesAreAlike()
    {
        const string Source = @"{\rtf1{\fonttbl{\f0 Arial;}}\tx720\tx1440 one\par two\par three\par}";

        var first = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);
        var second = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);

        Assert.IsTrue(first.Chunks.Count > 1);
        for (var at = 0; at < first.Chunks.Count; at++)
            Assert.AreEqual(first.Chunks[at].Ended, second.Chunks[at].Ended, $"The states read at stretch {at} differ.");
    }

    [TestMethod]
    public void WhatIsNotReadYet_IsHeldAsWritten_SoTheTreeStillPrintsAsTheDocument()
    {
        var source = string.Concat(Enumerable.Range(0, 30).Select(at => $"Paragraph {at}.\n\n"));
        var chunks = new ContentChunks(source, MarkdownParser.Language);
        var next = chunks.Next(32)!;

        chunks.Add(ContentChunk.Read(MarkdownParser.Windowed(), source, next.From, next.SoftMaxLength, next.Resume, null));

        var tree = chunks.Tree();

        Assert.IsFalse(chunks.Done);
        Assert.AreEqual(source, tree.Print());
        Assert.AreEqual(Kinds.Unreached, tree.Children.Last(part => !part.IsDerived).Kind);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Stitched(string source, string name, string language, Func<Func<ContentStretch, ContentParse>> windowed,
                                 Func<string, ContentNode> whole)
    {
        if (source.Length == 0) return;

        var expected = whole(source);

        foreach (var length in Lengths)
        {
            var chunks = Read(source, language, windowed, length);
            var tree = chunks.Tree();

            Assert.AreEqual(source, tree.Print(), $"{name} read {length} at a time does not print as itself.");
            Assert.IsTrue(tree.Same(expected), $"{name} read {length} at a time: {Diverged(expected, tree)}");
        }
    }

    private static ContentChunks Read(string source, string language, Func<Func<ContentStretch, ContentParse>> windowed, int length)
    {
        var chunks = new ContentChunks(source, language);
        var parse = windowed();

        while (chunks.Next(length) is { } next)
            chunks.Add(ContentChunk.Read(parse, source, next.From, next.SoftMaxLength, next.Resume, null));

        Settled(chunks, parse, length);

        return chunks;
    }

    private static void Settled(ContentChunks chunks, Func<ContentStretch, ContentParse> parse, int length)
    {
        foreach (var at in chunks.Unsettled().ToList())
        {
            var was = chunks.Chunks[at];
            chunks.Settle(at, ContentChunk.Read(parse, chunks.Source, was.From, length, was.Begun, chunks.Whole));
        }
    }

    private static bool Links(ContentNode tree) => tree.SelfAndDescendants().Any(node => node.Kind == Kinds.Link);

    /// <summary>Where two trees first part: the path of kinds down to the first node that differs, and what each holds there.</summary>
    private static string Diverged(ContentNode want, ContentNode got, string path = "")
    {
        var here = $"{path}/{want.Kind}";

        if (want.Kind != got.Kind || want.Role != got.Role || want.Text != got.Text || want.Offset != got.Offset
            || want.Trouble != got.Trouble || want.GetType() != got.GetType())
            return $"{here}: wanted {Said(want)}, got {Said(got)}";

        for (var at = 0; at < Math.Min(want.Children.Count, got.Children.Count); at++)
            if (!want.Children[at].Same(got.Children[at])) return Diverged(want.Children[at], got.Children[at], $"{here}[{at}]");

        if (want.Children.Count == got.Children.Count) return $"{here}: the same";

        return $"{here}: wanted {want.Children.Count} part(s), got {got.Children.Count}" +
               (want.Children.Count > got.Children.Count ? $"; the first missing is {Said(want.Children[got.Children.Count])}"
                                                         : $"; the first extra is {Said(got.Children[want.Children.Count])}");
    }

    private static string Said(ContentNode node) =>
        $"{node.Kind}/{node.Role} at {node.Offset?.ToString() ?? "-"} \"{Shortened(node.Print())}\"";

    private static string Shortened(string text) =>
        (text.Length > 60 ? text[..60] + "…" : text).Replace("\n", "\\n").Replace("\r", "\\r");
}
