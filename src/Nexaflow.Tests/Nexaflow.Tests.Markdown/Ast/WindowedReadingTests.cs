using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// A long reading is read a unit at a time (<see cref="ContentStretch"/>), each unit stopping softly where nothing is left open
/// and holding its tree in offsets of its own — and the units, stitched together where the index says each starts
/// (<see cref="ContentUnits"/>), must be exactly the reading read whole: the same pieces, standing for the same characters, wherever
/// the units happened to stop.
/// </summary>
[TestClass]
[CoversNode("windowed-reading")]
public class WindowedReadingTests
{
    /// <summary>A unit as short as can be, which stops at every place there is to stop; one a few blocks long; one most samples fit in.</summary>
    private static readonly int[] Lengths = [1, 64, 4096];

    [TestMethod]
    public void EveryMarkdownSample_ReadAUnitAtATime_IsTheSampleReadWhole()
    {
        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
            Stitched(File.ReadAllText(path), Path.GetFileName(path), MarkdownParser.Language, MarkdownParser.Windowed,
                     source => MarkdownParser.Parsing()(source).Tree);
    }

    [TestMethod]
    public void EveryRtfSample_ReadAUnitAtATime_IsTheSampleReadWhole()
    {
        foreach (var path in TestSampleData.Files("rtf"))
            Stitched(File.ReadAllText(path, System.Text.Encoding.Latin1), Path.GetFileName(path), RtfParser.Language,
                     RtfParser.Windowed, RtfParser.Parse);
    }

    [TestMethod]
    public void EveryUnitIsReadInOffsetsOfItsOwn_FromNought()
    {
        var source = string.Concat(Enumerable.Range(0, 300).Select(at => $"Paragraph {at} has words in it.\n\n"));
        var units = Read(source, MarkdownParser.Language, MarkdownParser.Windowed, 256);

        Assert.IsTrue(units.Count > 10, $"Read in {units.Count} unit(s).");

        foreach (var (unit, start) in units.From(0))
        {
            Assert.AreEqual(0, unit.Tree.Offset, $"the unit at {start} is read from nought");
            Assert.AreEqual(source.Substring(start, unit.Width), unit.Tree.Print(), $"the unit at {start} prints as its own characters");
        }
    }

    [TestMethod]
    public void AnRtfUnit_StopsBetweenParagraphs_AndTheNextReadsInWhatTheyWereSetIn()
    {
        const string Source = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}{\f1 Courier;}}{\colortbl;\red200\green0\blue0;}" +
                              @"\f1\cf1\b one\par {\i two\par three\par} four\b0\par five\par}";

        var units = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);

        Assert.IsTrue(units.Count >= 4, $"Read in {units.Count} unit(s).");
        Assert.IsTrue(units.Tree(ContentDocument.Of(Source)).Same(RtfParser.Parse(Source)));

        // Every unit but the last stops just past a paragraph mark, and the space that ends the word.
        foreach (var (unit, start) in units.From(0).SkipLast(1))
            StringAssert.EndsWith(Source[..(start + unit.Width)].TrimEnd(), @"\par");
    }

    [TestMethod]
    public void AnRtfUnit_NeverStopsInsideATable()
    {
        const string Source = @"{\rtf1 before\par\trowd\cellx1000\cellx2000\intbl a\cell b\cell\row" +
                              @"\trowd\cellx1000\cellx2000\intbl c\cell d\cell\row\pard after\par end\par}";

        var units = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);
        var table = Source.IndexOf(@"\trowd", StringComparison.Ordinal);
        var after = Source.IndexOf(@"\pard after", StringComparison.Ordinal);

        Assert.IsFalse(units.From(0).Any(placed => placed.Start > table && placed.Start <= after), "A unit stopped inside the table.");
        Assert.IsTrue(units.Tree(ContentDocument.Of(Source)).Same(RtfParser.Parse(Source)));
    }

    [TestMethod]
    public void AMarkdownUnit_ReadsAContainerLongerThanItsWindowWhole_AskingForMoreUntilItFitsIn()
    {
        var fence = "```\n" + string.Concat(Enumerable.Range(0, 2000).Select(line => $"line {line}\n")) + "```\n";
        var source = "# Title\n\n" + fence + "\nAfter.\n\nMore.\n";

        var units = Read(source, MarkdownParser.Language, MarkdownParser.Windowed, 16);
        var fenced = source.IndexOf("```", StringComparison.Ordinal);

        Assert.IsTrue(fence.Length > 4096 * 2, "the fence runs past the first window and the one twice as long");
        Assert.IsFalse(units.From(0).Any(placed => placed.Start > fenced && placed.Start < fenced + fence.Length),
                       "A unit stopped inside the fence.");
        Assert.IsTrue(units.Tree(ContentDocument.Of(source)).Same(MarkdownParser.Parsing()(source).Tree));
    }

    [TestMethod]
    public void AnRtfUnit_ReadsATableLongerThanItsWindowWhole_AskingForMoreUntilItFitsIn()
    {
        var rows = string.Concat(Enumerable.Range(0, 600).Select(row => $@"\trowd\cellx1000\cellx2000\intbl r{row}a\cell r{row}b\cell\row"));
        var source = @"{\rtf1 before\par" + rows + @"\pard after\par end\par}";

        var units = Read(source, RtfParser.Language, RtfParser.Windowed, 1);
        var table = source.IndexOf(@"\trowd", StringComparison.Ordinal);
        var after = source.IndexOf(@"\pard after", StringComparison.Ordinal);

        Assert.IsTrue(rows.Length > 4096 * 2, "the table runs past the first window and the one twice as long");
        Assert.IsFalse(units.From(0).Any(placed => placed.Start > table && placed.Start <= after), "A unit stopped inside the table.");
        Assert.IsTrue(units.Tree(ContentDocument.Of(source)).Same(RtfParser.Parse(source)));
    }

    [TestMethod]
    public void TwoReadingsStoppingInTheSameState_SayTheirStatesAreAlike()
    {
        const string Source = @"{\rtf1{\fonttbl{\f0 Arial;}}\tx720\tx1440 one\par two\par three\par}";

        var first = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);
        var second = Read(Source, RtfParser.Language, RtfParser.Windowed, 1);

        Assert.IsTrue(first.Count > 1);
        for (var at = 0; at < first.Count; at++)
            Assert.AreEqual(first[at].Unit.Ended, second[at].Unit.Ended, $"The states read at unit {at} differ.");
    }

    [TestMethod]
    public void WhatIsNotReadYet_StandsForItsCharactersWithoutCopyingThem_SoTheTreeStillPrintsAsTheDocument()
    {
        var source = string.Concat(Enumerable.Range(0, 30).Select(at => $"Paragraph {at}.\n\n"));
        var document = ContentDocument.Of(source);
        var units = new ContentUnits(MarkdownParser.Language).ReadOn(MarkdownParser.Windowed(), document, 32);

        var tree = units.Tree(document);
        var unreached = tree.Children.Last(part => !part.IsDerived);

        Assert.IsFalse(units.Done);
        Assert.AreEqual(source, tree.Print());
        Assert.AreEqual(Kinds.Unreached, unreached.Kind);
        Assert.IsTrue(unreached.Stands, "what is not read stands for the document's characters rather than holding a copy");
    }

    [TestMethod]
    public void EveryMarkdownSample_WrittenIn_IsReadAgainFromWhereItChanged_AsTheWrittenDocumentReadsWhole()
    {
        var documents = TestSampleData.Files("markdown").ToList();
        documents.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        foreach (var path in documents)
        {
            var source = File.ReadAllText(path);
            if (source.Length < 20) continue;

            var middle = WordStart(source, source.Length / 2);
            var near = WordStart(source, source.Length - 10);

            string[] edits =
            [
                source.Insert(middle, "typed "),
                "Opening words.\n\n" + source,
                source.Remove(near, 1),
                source.Insert(middle, "\n```\n"),
                source.Insert(middle, "\n\n[late]: https://example.com/late\n\n"),
                source,
            ];

            Edited(source, edits, Path.GetFileName(path), MarkdownParser.Language, MarkdownParser.Windowed,
                   text => MarkdownParser.Parsing()(text).Tree);
        }
    }

    [TestMethod]
    public void EveryRtfSample_WrittenIn_IsReadAgainFromWhereItChanged_AsTheWrittenDocumentReadsWhole()
    {
        foreach (var path in TestSampleData.Files("rtf"))
        {
            var source = File.ReadAllText(path, System.Text.Encoding.Latin1);
            var middle = WordStart(source, source.Length / 2);
            var closing = source.IndexOf('}', source.Length / 3);

            List<string> edits = [source.Insert(middle, "typed "), source.Insert(middle, @"\par "), source.Insert(middle, @"{\b ")];
            if (closing > 0) edits.Add(source.Remove(closing, 1));

            Edited(source, [.. edits, source], Path.GetFileName(path), RtfParser.Language, RtfParser.Windowed, RtfParser.Parse);
        }
    }

    [TestMethod]
    public void AWordTypedInALongDocument_IsReadInTheUnitsAroundIt_AndEveryOneAfterIsTheSameUnit()
    {
        var source = string.Concat(Enumerable.Range(0, 400).Select(at => $"Paragraph {at} has words in it.\n\n"));
        var parse = MarkdownParser.Windowed();
        var units = Read(source, MarkdownParser.Language, MarkdownParser.Windowed, 256);
        var at = source.IndexOf("Paragraph 200 ", StringComparison.Ordinal);
        var edited = ContentDocument.Of(source.Insert(at, "typed "));

        var (rewritten, kept, read) = units.Edited(at, at, "typed ".Length, edited, parse, 256);
        var was = new HashSet<ContentUnit>(units.From(0).Select(placed => placed.Unit), ReferenceEqualityComparer.Instance);
        var same = rewritten.From(0).Count(placed => was.Contains(placed.Unit));

        Assert.IsTrue(kept > 0, "Nothing before the change was kept.");
        Assert.IsTrue(read <= 3, $"{read} units were read again.");
        Assert.AreEqual(rewritten.Count - read, same, "every unit not read again is the very unit it was, nothing in it moved");
        Assert.IsTrue(rewritten.Tree(edited).Same(MarkdownParser.Parsing()(edited.ToString()).Tree));
    }

    [TestMethod]
    public void AWordTypedJustAfterALongFence_NeverReadsTheFenceAgain_ButOneOnItsFirstLineReadsTheUnitBefore()
    {
        var fence = "```\n" + string.Concat(Enumerable.Range(0, 3000).Select(line => $"line {line}\n")) + "```\n";
        var source = "# Title\n\nOpening words.\n\n" + fence + "\nAfter the fence,\nmore words.\n\nAnd more.\n";
        var parse = MarkdownParser.Windowed();
        var units = Read(source, MarkdownParser.Language, MarkdownParser.Windowed, 64);
        var fenced = units.IndexAt(source.IndexOf("```", StringComparison.Ordinal));

        var later = source.IndexOf("more words", StringComparison.Ordinal);
        var typed = ContentDocument.Of(source.Insert(later, "x"));
        var (rewritten, kept, _) = units.Edited(later, later, 1, typed, parse, 64);

        Assert.IsTrue(kept > fenced, "the fence's unit is kept, not read again");
        Assert.AreSame(units[fenced].Unit, rewritten[fenced].Unit);
        Assert.IsTrue(rewritten.Tree(typed).Same(MarkdownParser.Parsing()(typed.ToString()).Tree));

        // An underline on the first line after a paragraph makes it a heading: the unit before is read again.
        var opening = source.IndexOf("After the fence", StringComparison.Ordinal);
        var underlined = ContentDocument.Of(source.Insert(opening, "===\n"));
        var (heading, _, _) = units.Edited(opening, opening, 4, underlined, parse, 64);

        Assert.IsTrue(heading.Tree(underlined).Same(MarkdownParser.Parsing()(underlined.ToString()).Tree));
    }

    private static void Stitched(string source, string name, string language, Func<Func<ContentStretch, ContentParse>> windowed,
                                 Func<string, ContentNode> whole)
    {
        if (source.Length == 0) return;

        var expected = whole(source);

        foreach (var length in Lengths)
        {
            var tree = Read(source, language, windowed, length).Tree(ContentDocument.Of(source));

            Assert.AreEqual(source, tree.Print(), $"{name} read {length} at a time does not print as itself.");
            Assert.IsTrue(tree.Same(expected), $"{name} read {length} at a time: {Diverged(expected, tree)}");
        }
    }

    /// <summary>
    /// <paramref name="source"/> read a unit at a time and then written in, one edit after another, each read again from where it
    /// changed — and every one the same as the document it became read whole.
    /// </summary>
    private static void Edited(string source, IReadOnlyList<string> edits, string name, string language,
                               Func<Func<ContentStretch, ContentParse>> windowed, Func<string, ContentNode> whole)
    {
        var parse = windowed();
        var units = Read(source, language, windowed, 64);
        var document = ContentDocument.Of(source);
        var step = 0;

        foreach (var edit in edits)
        {
            step++;

            var now = ContentDocument.Of(edit);
            var change = now.ChangesFrom(document).Forward.Writes[0];

            (units, _, _) = units.Edited(change.Start, change.End, change.Text.Length - change.Length, now, parse, 64);
            while (!units.Done) units = units.ReadOn(parse, now, 64);

            var tree = units.Tree(now);

            Assert.AreEqual(edit, tree.Print(), $"{name}, edit {step}, does not print as itself.");
            Assert.IsTrue(tree.Same(whole(edit)), $"{name}, edit {step}: {Diverged(whole(edit), tree)}");

            document = now;
        }
    }

    private static int WordStart(string text, int at)
    {
        while (at > 0 && !char.IsWhiteSpace(text[at - 1])) at--;
        return at;
    }

    private static ContentUnits Read(string source, string language, Func<Func<ContentStretch, ContentParse>> windowed, int length)
    {
        var (units, parse, document) = (new ContentUnits(language), windowed(), ContentDocument.Of(source));

        while (!units.Done) units = units.ReadOn(parse, document, length);

        return units;
    }

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
