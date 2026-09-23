using System.Collections.Generic;
using System.Linq;

using System.Text.RegularExpressions;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
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
        ("a formula on its own line", "$$\n\\frac{x^2}{2}\n$$\n"),
        ("an empty formula", "$$\n$$\n"),
        ("an unreadable formula", "$$\n\\not_a_command{\n$$\n"),
        ("a formula in a sentence", "The value $x^2$ and then some.\n"),
        ("an unreadable formula in a sentence", "The value $\\not_a_command{$ and on.\n"),
        ("a diagram", "```mermaid\npie\n  \"a\" : 1\n```\n"),
        ("front matter", "---\ntitle: A Thing\n---\n\nwords\n"),
        ("a definition list", "Term\n:   what it means\n"),
        ("a figure", "^^^\nwords inside\n^^^ a caption\n"),
        ("a footer", "^^ at the foot\n"),
        ("a citation", "he said \"\"so\"\" once\n"),
        ("an abbreviation", "*[HTML]: HyperText Markup Language\n\nHTML is a thing\n"),
        ("a backslash escape", "not \\*emphasis\\* at all\n"),
        ("a lettered list", "a. one\nb. two\n"),
        ("a roman list", "i. one\nii. two\n"),
        ("a list starting at seven", "7. seven\n8. eight\n"),
        ("raw html", "<div>markup</div>\n"),
        ("inline html", "words <b>and</b> more\n"),
        ("a table with a cell across two", "| a | b |\n|---|---|\n| one | two |\n"),
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

            // What a formula or a diagram could not read is said where it is, under it on the page; everything else read.
            var said = string.Join("; ", laid.Trouble.Select(one => $"{one.Message} at {one.Start}+{one.Length}"));
            // (A formula in a sentence that cannot be read is written out as its characters instead, which says so itself.)
            if (what.Contains("unreadable", System.StringComparison.Ordinal))
                Assert.IsTrue(laid.Trouble.All(one => InAnotherLanguage(laid, one)), $"{what}: {said}");
            else
                Assert.AreEqual(0, laid.Trouble.Count, $"{what}: {said}");
        }
    }

    [TestMethod]
    public void EveryDocumentKeepsDrawingWhileItIsTyped()
    {
        // A builder reading what somebody is in the middle of typing is handed nonsense continually, and "the content
        // vanished" is never the right way to say so. Half a formula is trouble to the formula, which says where; the
        // document around it reads whatever is typed.
        foreach (var (what, source) in Documents)
            for (var length = 1; length <= source.Length; length++)
            {
                var laid = Lay(source[..length]);

            Assert.IsTrue(laid.Trouble.All(one => InAnotherLanguage(laid, one)), $"{what}: after {length} character(s)");
            }
    }

    /// <summary>Whether some trouble lies in a formula's or a diagram's own text, rather than in markdown's.</summary>
    private static bool InAnotherLanguage(Laid laid, Diagnostic trouble) =>
        laid.Root.SelfAndDescendants()
            .Select(piece => piece.Part as Nexaflow.Markdown.Ast.ContentPart)
            .Any(part => part is { Kind: MarkdownKinds.Fence or MarkdownKinds.Math or MarkdownKinds.Formula }
                         && part.Start <= trouble.Start && trouble.Start + trouble.Length <= part.End);

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

    [TestMethod]
    public void ALineEndingIsASpaceUnlessTheWriterAskedForABreak()
    {
        // Two spaces at the end of a line is the one way markdown has of saying "and start a new line here".
        var flowed = Lay("one\ntwo\n", room: 900);
        var broken = Lay("one  \ntwo\n", room: 900);

        Assert.IsTrue(broken.Size.Height > flowed.Size.Height);
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

    // ── Maths ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void AFormulaOnItsOwnLineIsTypesetAndNotShownAsCharacters()
    {
        var drawn = Lay("$$\n\\frac{x^2}{2}\n$$\n");

        Assert.AreEqual(0, Pieces(drawn, MarkdownPieces.Verbatim).Count, "it was typeset, not shown as its source");
        Assert.IsTrue(drawn.Size.Height > 0);
    }

    [TestMethod]
    public void AndItIsSetLargerThanTheSameFormulaInASentence()
    {
        // The one size a builder is not given: how big a formula is set is a fact about the content — whether
        // it was written on its own line or in the middle of a sentence — so it is settled where every other
        // fact about content is, and the builder only reads how big the answer came out.
        var alone = Formula(Lay("$$\nx\n$$\n"));
        var within = Formula(Lay("A $x$ here.\n"));

        Assert.IsTrue(alone.Width > within.Width * 1.3,
            $"the same formula came out {alone.Width:F1} wide on its own line and {within.Width:F1} in a sentence");
    }

    [TestMethod]
    public void AndItIsCentredInTheRoomItWasGiven()
    {
        var wide = Pieces(Lay("$$\nx\n$$\n", room: 600), MarkdownPieces.Block);
        var narrow = Pieces(Lay("$$\nx\n$$\n", room: 200), MarkdownPieces.Block);

        Assert.IsTrue(wide.Count > 0 && narrow.Count > 0);
        Assert.IsTrue(wide.Min(piece => piece.Bounds.X) > narrow.Min(piece => piece.Bounds.X),
            "the more room it was given the further in it sat");
    }

    [TestMethod]
    public void AFormulaThatCouldNotBeReadIsStillAFormula()
    {
        // Maths under a caret is invalid most of the time — every command is unreadable until its last letter
        // is typed — so a formula that turned into a box of source as it was written would spend most of its
        // life as a box of source. What could be read is set, and the reader's own parser waves under the rest.
        var drawn = Lay("$$\n\\not_a_command{\n$$\n");

        Assert.AreEqual(0, Pieces(drawn, MarkdownPieces.Verbatim).Count,
            "an unreadable formula is still typeset, still somewhere the caret can go and repair it");
        Assert.IsTrue(drawn.Size.Height > 0);
    }

    [TestMethod]
    public void AFormulaInASentenceSitsOnTheLineTheSentenceIsOn()
    {
        var drawn = Lay("The value $x^2$ and then some.\n");

        // The words either side are one line, so the formula did not push them apart.
        var words = Words(drawn);

        Assert.IsTrue(words.Count >= 2);
        Assert.AreEqual(1, words.Select(piece => Math.Round(piece.Bounds.Y)).Distinct().Count(),
            "the sentence broke into more than one line round a formula that should have sat in it");

        // And the dollars are not drawn as characters: what is there is the formula.
        StringAssert.DoesNotMatch(Drawn(drawn), new System.Text.RegularExpressions.Regex(@"\$"));
    }

    [TestMethod]
    public void AnUnreadableFormulaInASentenceShowsItsSourceInstead()
    {
        // Where a display formula keeps its typesetting, an inline one does not: half a display formula still
        // tells a reader where they are, but a sentence with a wave through the middle of it is a sentence
        // nobody can read. So the dollars come back and the reader can see what to fix.
        var drawn = Lay("The value $\\not_a_command{$ and on.\n");

        StringAssert.Contains(Drawn(drawn), "$\\not_a_command{$");
    }

    [TestMethod]
    public void AFormulaWithNothingInItIsTheCharactersItWasWrittenWith()
    {
        // There is no formula in "$$\n$$" to typeset, and the characters are the only place a caret could go —
        // so they are what is drawn, and typing into them is what starts the formula off.
        var shown = Pieces(Lay("$$\n$$\n"), MarkdownPieces.Verbatim);

        Assert.AreEqual(1, shown.Count);
        Assert.IsTrue(shown[0].Words!.Maps, "so the caret lands in the dollars and the next key writes maths");
    }

    // ── What a construct is set as ──────────────────────────────────────────

    [TestMethod]
    public void AnAlertSaysWhatItIsInTheWordAReaderMeantByTheMarks()
    {
        // The writer typed [!WARNING]; what they meant by it is the word Warning, so that is what is drawn —
        // and pressing it shows the marks, which is the same bargain a renumbered list marker makes.
        StringAssert.Contains(Drawn(Lay("> [!WARNING]\n> Mind how you go.\n")), "Warning");

        // A kind nobody has a colour for still gets its name back, tidied.
        StringAssert.Contains(Drawn(Lay("> [!wibble]\n> Something.\n")), "Wibble");

        // And a plain quotation calls itself nothing at all.
        StringAssert.DoesNotMatch(Drawn(Lay("> just quoted\n")), new Regex("Note|Warning"));
    }

    [TestMethod]
    public void AListIsCountedInWhicheverAlphabetItWasWrittenIn()
    {
        StringAssert.Contains(Markers(Lay("a. one\nb. two\n")), "a.");
        StringAssert.Contains(Markers(Lay("a. one\nb. two\n")), "b.");

        StringAssert.Contains(Markers(Lay("i. one\nii. two\niii. three\n")), "iii.");
        StringAssert.Contains(Markers(Lay("I. one\nII. two\n")), "II.");

        // Where it starts is the writer's, and renumbering keeps it rather than resetting to one.
        StringAssert.Contains(Markers(Lay("7. seven\n8. eight\n")), "7.");
        StringAssert.Contains(Markers(Lay("7. seven\n8. eight\n")), "8.");

        // A writer who numbered every line 1. still meant a list, and gets one.
        StringAssert.Contains(Markers(Lay("1. one\n1. two\n1. three\n")), "3.");
    }

    [TestMethod]
    public void AHeadingThatOpensASectionIsRuledOffUnder()
    {
        Assert.AreEqual(1, Pieces(Lay("# Title\n\nwords\n"), MarkdownPieces.Rule).Count);
        Assert.AreEqual(1, Pieces(Lay("## Part\n\nwords\n"), MarkdownPieces.Rule).Count);

        Assert.AreEqual(0, Pieces(Lay("### Smaller\n\nwords\n"), MarkdownPieces.Rule).Count,
            "a third-rank heading is a heading inside a section, not the start of one");
    }

    [TestMethod]
    public void ATermIsSetApartFromWhatItMeans()
    {
        var laid = Lay("Term\n:   what it means\n");

        var term = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("Term"));
        var means = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("what it means"));

        Assert.IsTrue(means.Bounds.X > term.Bounds.X, "what a term means is set in from the term");
        Assert.IsTrue(means.Bounds.Y > term.Bounds.Y, "and under it");
    }

    [TestMethod]
    public void AFigureIsAPanelWithItsCaptionUnderTheMiddleOfIt()
    {
        var laid = Lay("^^^\nwords inside\n^^^ a caption\n");

        StringAssert.Contains(Drawn(laid), "words inside");
        StringAssert.Contains(Drawn(laid), "a caption");

        var inside = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("words inside"));
        var caption = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("a caption"));

        Assert.IsTrue(caption.Bounds.Y > inside.Bounds.Y, "a caption goes under what it names");
        Assert.IsTrue(caption.Bounds.X > inside.Bounds.X, "and is centred, where the words it names start at the left");
    }

    [TestMethod]
    public void AFooterIsRuledOffFromTheDocumentAbove()
    {
        var laid = Lay("words\n\n^^ at the foot\n");

        StringAssert.Contains(Drawn(laid), "at the foot");
        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).OfType<RuleMark>().Any()),
            "something has to say the document proper has ended");
    }

    [TestMethod]
    public void FrontMatterIsWhatADocumentSaysAboutItselfAndIsNotOnThePage()
    {
        var laid = Lay("---\ntitle: A Thing\n---\n\nwords\n");

        StringAssert.DoesNotMatch(Drawn(laid), new Regex("A Thing"));
        StringAssert.Contains(Drawn(laid), "words", "and what the document does say is still there");
    }

    [TestMethod]
    public void RawHtmlIsShownQuietlyAndInlineHtmlIsNotShownAtAll()
    {
        // A block of it is source a reader may want to see and fix, so it is shown as source — not as code,
        // which it is not, and not as prose, which it would be pretending to be. A tag in the middle of a
        // sentence is not something a sentence can show, so the words close up round it as a browser would.
        var block = Pieces(Lay("<div>markup</div>\n"), MarkdownPieces.Verbatim).Single();

        Assert.AreEqual("<div>markup</div>", block.Words!.Glyphs.Text);
        Assert.IsTrue(block.Words.Maps, "and the caret goes into it, because it is the source");

        var inline = Drawn(Lay("words <b>and</b> more\n"));

        StringAssert.DoesNotMatch(inline, new Regex("<b>"));
        StringAssert.Contains(inline, "and");
    }

    [TestMethod]
    public void ABackslashDrawsTheCharacterItWasPutInFrontOf()
    {
        var drawn = Drawn(Lay("not \\*emphasis\\* at all\n"));

        StringAssert.Contains(drawn, "*emphasis*", "the asterisks are drawn as themselves");
        StringAssert.DoesNotMatch(drawn, new Regex(@"\\"), "and the backslashes that held them are not");

        // Still two characters underneath, so a press on one shows what was typed.
        var escape = Words(Lay("not \\*emphasis\\* at all\n")).First(piece => piece.Words!.Glyphs.Text == "*");
        Assert.IsFalse(escape.Words!.Maps);
    }

    [TestMethod]
    public void ACitationIsRaisedAndSetSmall()
    {
        var laid = Lay("he said \"\"so\"\" once\n");

        var said = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("so"));
        var plain = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("he said"));

        Assert.IsTrue(said.Bounds.Height < plain.Bounds.Height, "a citation is set smaller than the words round it");
        Assert.IsTrue(said.Bounds.Y < plain.Bounds.Bottom - (plain.Bounds.Height / 2), "and raised off the line");
    }

    [TestMethod]
    public void AnAbbreviationsMeaningIsNeverDrawnIntoTheSentence()
    {
        // What an abbreviation means is written on a line of its own, somewhere else entirely. The sentence
        // draws the word and only the word.
        //
        // It does not yet draw a rule of dots under it, and that is a consequence of the two-stage read
        // rather than an oversight: a block's words are read from the block's own source, so a definition
        // three paragraphs up is not in front of the reader when the sentence is read. The same is true of
        // link reference definitions. Fixing it means giving the block reader what the document already
        // worked out, which is a change to the seam rather than to this.
        var laid = Lay("*[HTML]: HyperText Markup Language\n\nHTML is a thing\n");

        StringAssert.Contains(Drawn(laid), "HTML is a thing");
        StringAssert.DoesNotMatch(Drawn(laid), new Regex("HyperText"));
    }

    [TestMethod]
    public void ATableSaysWhatItsColumnsAreCalledDifferentlyFromWhatIsInThem()
    {
        var laid = Lay("| head | er |\n|---|---|\n| one | two |\n");

        var head = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("head"));
        var body = Words(laid).First(piece => piece.Words!.Glyphs.Text.Contains("one"));

        Assert.AreNotEqual(head.Words!.Glyphs.Width / Math.Max(head.Words.Glyphs.Text.Length, 1),
                           body.Words!.Glyphs.Width / Math.Max(body.Words.Glyphs.Text.Length, 1),
            "the head is set differently from the rows under it");
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static string Markers(Laid laid) =>
        string.Concat(Pieces(laid, MarkdownPieces.Marker).Select(piece => piece.Words!.Glyphs.Text));

    private static List<LayoutMark> Marks(Piece piece)
    {
        var found = new List<LayoutMark>();

        foreach (var mark in piece.Marks) found.Add(mark);

        return found;
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

    /// <summary>The box another language's content was grafted into — what a formula came out as.</summary>
    private static Rect Formula(Laid laid) =>
        Pieces(laid, MarkdownPieces.Block)
            .Where(piece => piece.Bounds.Width > 0)
            .OrderBy(piece => piece.Bounds.Width)
            .First()
            .Bounds;
}
