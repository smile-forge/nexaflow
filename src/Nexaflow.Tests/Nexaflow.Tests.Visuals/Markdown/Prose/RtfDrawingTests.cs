using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// An RTF document on the shared layout tree: drawn by the builder a markdown document is, with the fonts, colours,
/// indents, alignment and tab stops its source spells out — and read, searched and copied as what it draws, never as the
/// control words round it.
/// </summary>
[TestClass]
[CoversNode("rtf-drawing")]
public class RtfDrawingTests
{
    private const string Header = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fswiss\fcharset0 Arial;}}{\colortbl ;\red255\green0\blue0;}" + "\r\n";

    /// <summary>The ordinary text size of the light palette, which every distance in a paragraph's look is measured in.</summary>
    private static double Em => WpfStyles.Light.TextSize;

    [TestMethod]
    public void OnlyTheWordsAreDrawn_NeverTheControlWordsRoundThem()
    {
        Assert.AreEqual("Plain bold and red\nNext", Read(Header + @"\pard Plain {\b bold} and {\cf1 red}\par Next\par}").Text);
    }

    [TestMethod]
    public void ARunIsDrawnInTheColourItsSourceGave()
    {
        var laid = Lay(Header + @"\pard plain {\cf1 red}\par}");
        var red = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "red");
        var ink = (SolidColorBrush)Marks(red).OfType<PortableTextMark>().Single().Ink!.Brush();

        Assert.AreEqual(Color.FromRgb(255, 0, 0), ink.Color);
    }

    [TestMethod]
    public void AParagraphStandsInByItsIndent()
    {
        var laid = Lay(@"{\rtf1\pard\li720 indented\par}");

        Assert.AreEqual(3 * Em, Words(laid).Single().Bounds.X, 0.5);
    }

    [TestMethod]
    public void APageBreakIsTheRoomOfAFewBlankLines_BetweenThePagesWords()
    {
        var laid = Lay(@"{\rtf1\pard Title\page\pard Next\par}");
        var page = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == PortablePieces.Page).Bounds;
        var words = Words(laid).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(2, words.Count);
        Assert.IsTrue(page.Height > 2 * words[0].Height, $"a page break should be several lines' room, not {page.Height:0.#}");
        Assert.IsTrue(words[1].Top >= page.Bottom - 0.5, "the next page's words start below it");
    }

    [TestMethod]
    public void ACentredParagraphIsSetInTheMiddleOfItsRoom()
    {
        var laid = Lay(@"{\rtf1\pard\qc middle\par}", room: 400);
        var word = Words(laid).Single().Bounds;

        Assert.AreEqual(200, word.X + (word.Width / 2), 1.0);
    }

    [TestMethod]
    public void AParagraphSetAgainstTheEndEndsAtTheEdgeOfItsRoom()
    {
        var laid = Lay(@"{\rtf1\pard\qr right\par}", room: 400);

        Assert.AreEqual(400, Words(laid).Single().Bounds.Right, 1.0);
    }

    [TestMethod]
    public void AJustifiedParagraphFillsEveryLineButItsLast()
    {
        var words = string.Join(" ", Enumerable.Repeat("several words", 20));
        var laid = Lay(@"{\rtf1\pard\qj " + words + @"\par}", room: 300);

        var lines = Words(laid).GroupBy(piece => Math.Round(piece.Bounds.Y)).OrderBy(line => line.Key).ToList();

        Assert.IsTrue(lines.Count > 2, "set over several lines");

        foreach (var line in lines.Take(lines.Count - 1))
            Assert.AreEqual(300, line.Max(piece => piece.Bounds.Right), 1.5, "a full line reaches the end of its room");

        Assert.IsTrue(lines[^1].Max(piece => piece.Bounds.Right) < 299, "the last line is left ragged");
    }

    [TestMethod]
    public void ATabTakesTheLineOnToTheNextStop()
    {
        var laid = Lay(@"{\rtf1\pard\tx2880 a\tab b\par}");
        var b = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "b");

        Assert.AreEqual(12 * Em, b.Bounds.X, 0.5);
    }

    [TestMethod]
    public void AHangingLineTabsToTheIndent_SoTheWordsLineUpUnderEachOther()
    {
        var laid = Lay(@"{\rtf1\pard\li720\fi-360 *\tab first line of a long item that goes on\par}", room: 200);
        var words = Words(laid).Where(piece => piece.Words!.Glyphs.Text != "*").ToList();

        Assert.AreEqual(3 * Em, words.Min(piece => piece.Bounds.X), 0.5);
        Assert.IsTrue(words.Select(piece => Math.Round(piece.Bounds.Y)).Distinct().Count() > 1, "set over several lines");
        Assert.IsTrue(words.GroupBy(piece => Math.Round(piece.Bounds.Y)).All(line => Math.Abs(line.Min(piece => piece.Bounds.X) - (3 * Em)) < 0.5),
                      "every line starts under the first");
    }

    [TestMethod]
    public void ALineBreakStartsANewLine()
    {
        var laid = Lay(@"{\rtf1 one\line two\par}");
        var one = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "one");
        var two = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "two");

        Assert.IsTrue(two.Bounds.Y > one.Bounds.Y);
    }

    [TestMethod]
    public void ParagraphsAreAsFarApartAsTheirSourceSays()
    {
        var tight = Lay(@"{\rtf1\pard one\par two\par}");
        var spaced = Lay(@"{\rtf1\pard\sa240 one\par two\par}");

        Assert.AreEqual(Em, Gap(spaced) - Gap(tight), 0.5);

        static double Gap(PortableLaid laid) => Words(laid)[1].Bounds.Y - Words(laid)[0].Bounds.Y;
    }

    [TestMethod]
    public void ASearchFindsTheWordsDrawn_NotTheControlWords()
    {
        const string source = @"{\rtf1\pard\par Keep the {\b paragraph} here.\par}";
        var read = Read(source);

        Assert.AreEqual(0, MarkdownFind.Drawn(read, "pard").Count, "a control word is not a word");

        var found = MarkdownFind.Drawn(read, "paragraph").Single();
        Assert.AreEqual(source.IndexOf("paragraph", StringComparison.Ordinal), found.Start);
        Assert.AreEqual("paragraph".Length, found.Length);
    }

    [TestMethod]
    public void ASearchFindsAWordWhoseLettersAreSetDifferently()
    {
        const string source = @"{\rtf1 hel{\b lo} world\par}";
        var found = MarkdownFind.Drawn(Read(source), "hello").Single();

        Assert.AreEqual(source.IndexOf("hel", StringComparison.Ordinal), found.Start);
        Assert.AreEqual(source.IndexOf("lo}", StringComparison.Ordinal) + 2, found.Start + found.Length);
    }

    [TestMethod]
    public void ACharacterWrittenAsACodeIsFoundAsTheCharacter()
    {
        var found = MarkdownFind.Drawn(Read(@"{\rtf1\ansi caf\'e9\par}"), "caf" + (char)0xE9);

        Assert.AreEqual(1, found.Count);
    }

    [TestMethod]
    public void AWordWrappedOntoTheNextLineIsFoundWhereItWasWritten()
    {
        var words = string.Join(" ", Enumerable.Repeat("filler", 30)) + " needle";
        var source = @"{\rtf1 " + words + @"\par}";
        var found = MarkdownFind.Drawn(Read(source), "needle").Single();

        Assert.AreEqual(source.IndexOf("needle", StringComparison.Ordinal), found.Start);
    }

    [TestMethod]
    public void WhatIsPickedOutIsCopiedAsItsWords()
    {
        const string source = @"{\rtf1 one {\b two} three\par four\par}";
        var read = Read(source);
        var from = source.IndexOf("two", StringComparison.Ordinal);
        var to = source.IndexOf("four", StringComparison.Ordinal) + 4;

        Assert.AreEqual("two three\nfour", read.Within(from, to - from));
    }

    [TestMethod]
    public void APictureWrittenIntoTheDocumentIsDrawn()
    {
        var png = Convert.ToHexString(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        var laid = Lay(@"{\rtf1 before {\pict\pngblip\picwgoal480\pichgoal480 " + png + @"} after\par}");
        var picture = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == PortablePieces.Picture);

        Assert.AreEqual(2 * Em, picture.Bounds.Width, 0.5, "drawn at the size it was asked for");
    }

    [TestMethod]
    public void ATableIsDrawnAsItsCells()
    {
        const string source = @"{\rtf1\trowd\cellx1000\cellx2000\pard\intbl A\cell B\cell\row}";

        Assert.AreEqual(2, Lay(source).Root.SelfAndDescendants().Count(piece => piece.Kind == PortablePieces.Cell));
        Assert.AreEqual("A\nB", Read(source).Text);
    }

    [TestMethod]
    public void TheLanguageIsReadAsWhatItDraws()
    {
        Assert.IsTrue(ContentLanguages.For(RtfParser.Language)!.Editing.ReadsAsDrawn);
        Assert.IsFalse(ContentLanguages.Markdown.Editing.ReadsAsDrawn);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static PortableLaid Lay(string source, double room = 480) =>
        Laying.Lay(RtfParser.Language, source, room, WpfStyles.Light);

    /// <summary>What the document reads as, read from what it read to.</summary>
    private static DrawnText Read(string source) => DrawnText.Of(RtfParser.Parse(source));

    private static List<PortablePiece> Words(PortableLaid laid) =>
        [.. laid.Root.SelfAndDescendants()
            .Where(piece => piece.Kind == PortablePieces.Words && piece.Words!.Glyphs.Text.Trim().Length > 0)];

    private static List<PortableMark> Marks(PortablePiece piece)
    {
        var found = new List<PortableMark>();

        foreach (var mark in piece.Marks) found.Add(mark);

        return found;
    }
}
