using System.Collections.Generic;
using System.Linq;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A markdown document on the shared layout tree: what is drawn, and what each piece of it stands for.
///
/// <para>
/// Both halves matter and the second is the one that is easy to lose. A heading drawn large with its hashes not drawn
/// at all is only correct if the hashes are still in the tree at the offsets they were typed at — otherwise the caret
/// lands somewhere other than where it looks like it will, and everything downstream of that is wrong in a way no
/// screenshot shows.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownBuilderTests
{
    private static readonly (string What, string Source)[] Documents =
    [
        ("one paragraph", "Words with **bold** and `code`.\n"),
        ("a heading", "# A *slanted* title\n\nand words under it\n"),
        ("every rank of heading", "# one\n## two\n### three\n#### four\n##### five\n###### six\n"),
        ("a list", "- one\n- two\n- three\n"),
        ("a numbered list", "1. one\n2. two\n"),
        ("a nested list", "- one\n  - under\n- two\n"),
        ("a task list", "- [ ] to do\n- [x] done\n"),
        ("a quote", "> quoted\n> more\n"),
        ("an alert", "> [!WARNING]\n> Mind how you go.\n"),
        ("a rule", "one\n\n---\n\ntwo\n"),
        ("a table", "| a | b |\n|---|---|\n| 1 | 2 |\n"),
        ("a table with alignment", "| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n"),
        ("a fence", "```csharp\nvar x = 1;\n```\n"),
        ("a diagram", "```mermaid\npie\n  \"a\" : 1\n```\n"),
        ("front matter", "---\ntitle: A Thing\n---\n\nwords\n"),
        ("a link", "see [the page](https://example.org)\n"),
        ("an entity", "a &amp; b\n"),
        ("everything at once",
         "# Title\n\nWords with **bold**.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- [ ] a task\n\n> quoted\n"),
    ];

    // ── It always draws, and it only ever draws the source ──────────────────

    [TestMethod]
    public void EveryDocumentDraws()
    {
        foreach (var (what, source) in Documents)
        {
            var laid = Lay(source);

            Assert.IsTrue(laid.Size.Height > 0, $"{what}: nothing was drawn");
            Assert.AreEqual(0, laid.Trouble.Count, $"{what}: {string.Join("; ", laid.Trouble.Select(one => one.Message))}");
        }
    }

    [TestMethod]
    public void EveryDocumentKeepsDrawingWhileItIsTyped()
    {
        // A builder reading what somebody is in the middle of typing is handed nonsense continually, and "the content
        // vanished" is never the right way to say so.
        foreach (var (what, source) in Documents)
            for (var length = 1; length <= source.Length; length++)
            {
                var laid = Lay(source[..length]);

                Assert.AreEqual(0, laid.Trouble.Count, $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void EverythingDrawnStandsForCharactersThatAreThere()
    {
        foreach (var (what, source) in Documents)
            foreach (var piece in Lay(source).Root.SelfAndDescendants())
            {
                if (piece.Part is not { } part) continue;

                Assert.IsTrue(part.Start >= 0 && part.Start + part.Length <= source.Length,
                    $"{what}: {piece.Kind} claims {part.Start}+{part.Length} of {source.Length}");
            }
    }

    [TestMethod]
    public void LayingTheSameDocumentTwiceGivesTheSameAnswer()
    {
        foreach (var (what, source) in Documents)
            Assert.AreEqual(Lay(source).Size, Lay(source).Size, what);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ARunOfWordsIsOnePieceRatherThanOnePerLetter()
    {
        var words = Words(Lay("just some plain words\n"));

        Assert.AreEqual("just some plain words", words[0].Words!.Glyphs.Text);
        Assert.IsTrue(words[0].Words!.Maps, "what is drawn is what was written, so the caret maps straight onto it");
    }

    [TestMethod]
    public void WhatIsSetHeavyIsItsOwnPiece()
    {
        var words = Words(Lay("a **bold** word\n"));

        Assert.AreEqual("a ", words[0].Words!.Glyphs.Text);
        Assert.AreEqual("bold", words[1].Words!.Glyphs.Text);
        Assert.AreEqual(" word", words[2].Words!.Glyphs.Text);
    }

    [TestMethod]
    public void TheMarksRoundAConstructAreNotDrawnAndAreStillThere()
    {
        const string source = "# Title\n";
        var laid = Lay(source);

        Assert.AreEqual("Title", Drawn(laid).Trim());

        // The hashes are machinery and are not set — but the piece that was set names the characters it stands for,
        // and those characters are where the writer put them.
        var title = Words(laid)[0];
        Assert.AreEqual("Title", source.Substring(title.Part!.Start, title.Part!.Length));
    }

    [TestMethod]
    public void AHeadingIsSetLargerThanTheWordsUnderIt()
    {
        var words = Words(Lay("# Title\n\nwords\n"));

        Assert.IsTrue(words[0].Bounds.Height > words[^1].Bounds.Height);
    }

    [TestMethod]
    public void AnEntityIsDrawnAsTheCharacterItStandsForAndSaysSo()
    {
        var entity = Words(Lay("a &amp; b\n")).First(piece => piece.Words!.Glyphs.Text == "&");

        Assert.IsFalse(entity.Words!.Maps, "what is drawn is not what was written");
        Assert.IsTrue(entity.Words!.Writes, "so pressing it shows what was written");
    }

    [TestMethod]
    public void ALinkSaysWhereItGoesWithoutKnowingWhatGoingThereMeans()
    {
        var laid = Lay("see [the page](https://example.org)\n");
        var act = laid.Root.SelfAndDescendants()
            .Select(piece => piece.Acts?.Click)
            .FirstOrDefault(click => click?.Verb == LayoutVerbs.Navigate);

        Assert.AreEqual("https://example.org", act?.Target);
    }

    [TestMethod]
    public void WordsWrapAtTheRoomTheyAreGiven()
    {
        const string source = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen\n";

        Assert.IsTrue(Lay(source, room: 90).Size.Height > Lay(source, room: 900).Size.Height);
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ANumberedListIsDrawnWithTheNumberTheItemIsAndKeepsWhatWasTyped()
    {
        // A writer who numbered every line 1. meant a list. What they typed is still there to be read back.
        var markers = Pieces(Lay("1. one\n1. two\n"), MarkdownPieces.Marker);

        Assert.AreEqual("1.", markers[0].Words!.Glyphs.Text);
        Assert.AreEqual("2.", markers[1].Words!.Glyphs.Text);
        Assert.IsFalse(markers[1].Words!.Maps);
        Assert.IsTrue(markers[1].Words!.Writes, "pressing it shows the 1. the writer actually typed");
    }

    [TestMethod]
    public void ANestedListIsSetFurtherInThanTheOneHoldingIt()
    {
        var markers = Pieces(Lay("- one\n  - under\n- two\n"), MarkdownPieces.Marker);

        Assert.AreEqual(3, markers.Count);
        Assert.IsTrue(markers[1].Bounds.X > markers[0].Bounds.X);
        Assert.AreEqual(markers[0].Bounds.X, markers[2].Bounds.X);
    }

    // ── What a reader can tick ──────────────────────────────────────────────

    [TestMethod]
    public void ATickIsItsOwnPieceOverTheThreeCharactersItStandsFor()
    {
        const string source = "- [ ] to do\n- [x] done\n";
        var ticks = Pieces(Lay(source), MarkdownPieces.Tick);

        Assert.AreEqual(2, ticks.Count);
        Assert.AreEqual("[ ]", source.Substring(ticks[0].Part!.Start, ticks[0].Part!.Length));
        Assert.AreEqual("[x]", source.Substring(ticks[1].Part!.Start, ticks[1].Part!.Length));
    }

    [TestMethod]
    public void ATickSaysWhatPressingItWouldMeanAndNotWhatWouldHappen()
    {
        var ticks = Pieces(Lay("- [ ] to do\n- [x] done\n"), MarkdownPieces.Tick);

        Assert.AreEqual(MarkdownVerbs.Tick, ticks[0].Acts?.Click?.Verb);
        Assert.AreEqual("on", ticks[0].Acts?.Click?.Target);
        Assert.AreEqual("off", ticks[1].Acts?.Click?.Target);
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ATableIsAPiecePerCell()
    {
        Assert.AreEqual(4, Pieces(Lay("| a | b |\n|---|---|\n| 1 | 2 |\n"), MarkdownPieces.Cell).Count);
    }

    [TestMethod]
    public void ATablesRowsAndColumnsReadTogetherAsAMatrixDoes()
    {
        // The same declaration a matrix makes, answered by the same code — so a drag across a table behaves the way a
        // drag across a matrix already does, and neither of them has a selection of its own.
        var cells = Pieces(Lay("| a | b |\n|---|---|\n| 1 | 2 |\n"), MarkdownPieces.Cell);

        Assert.AreEqual(2, cells[0].Sharing(vertical: false).Count);
        Assert.AreEqual(2, cells[0].Sharing(vertical: true).Count);
    }

    [TestMethod]
    public void ACellIsSetTheWayTheRuleUnderTheHeadSays()
    {
        var cells = Pieces(Lay("| a | b |\n|:--|--:|\n| 1 | 2 |\n", room: 400), MarkdownPieces.Cell);

        Assert.IsTrue(Inset(cells[3]) > Inset(cells[2]),
            "a column set to the right starts further into its cell than one set to the left");
    }

    // ── What another language reads ─────────────────────────────────────────

    [TestMethod]
    public void AFenceInALanguageSomethingDrawsIsDrawnByIt()
    {
        var drawn = Lay("```mermaid\npie\n  \"a\" : 1\n```\n");

        Assert.AreEqual(0, Pieces(drawn, MarkdownPieces.Verbatim).Count, "it was laid, not shown as characters");
        Assert.IsTrue(drawn.Size.Height > 0);
    }

    [TestMethod]
    public void AFenceInALanguageNothingDrawsIsShownAsItWasWritten()
    {
        var shown = Pieces(Lay("```nothing-reads-this\nvar x = 1;\n```\n"), MarkdownPieces.Verbatim);

        Assert.AreEqual(1, shown.Count);
        Assert.AreEqual("var x = 1;", shown[0].Words!.Glyphs.Text);
        Assert.IsTrue(shown[0].Words!.Maps, "it is the source, so the caret goes straight into it");
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static Laid Lay(string source, double room = 480) =>
        MarkdownBuilder.Lay(source, StyleFormat.Dark, room);

    private static List<Piece> Pieces(Laid laid, string kind) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static List<Piece> Words(Laid laid) =>
        [.. Pieces(laid, MarkdownPieces.Words).Where(piece => piece.Words!.Glyphs.Text.Trim().Length > 0)];

    private static string Drawn(Laid laid) =>
        string.Concat(Pieces(laid, MarkdownPieces.Words).Select(piece => piece.Words!.Glyphs.Text));

    /// <summary>How far into its cell a cell's words start.</summary>
    private static double Inset(Piece cell) =>
        cell.SelfAndDescendants().First(piece => piece.Kind == MarkdownPieces.Words).Bounds.X - cell.Bounds.X;
}
