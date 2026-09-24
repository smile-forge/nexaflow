using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// The corners of CommonMark and its extensions a document has to get right however it is drawn — each one a fix the
/// renderer before this one was held to, held to again on the layout tree.
///
/// <para>
/// Every case is asked of what was laid: the words drawn, the pieces they are, and what a press on them means. A
/// construct's marks are never drawn, and what they said is said by how the words are set.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownParityTests
{
    // ── Blocks ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnUnderlinedHeadingIsAHeadingLikeOneWithHashes()
    {
        var underlined = Lay("Title\n=====\n\nwords\n");
        var hashed = Lay("# Title\n\nwords\n");

        Assert.AreEqual("Title", Words(underlined)[0].Words!.Glyphs.Text, "the underline is not drawn");
        Assert.AreEqual(Words(hashed)[0].Bounds.Height, Words(underlined)[0].Bounds.Height, 0.5, "and it is set as large");
    }

    [TestMethod]
    public void AnImportantAlertIsDrawnInItsOwnColour()
    {
        var laid = Lay("> [!IMPORTANT]\n> Read this.\n");

        StringAssert.Contains(Drawn(laid), "Important");
        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).Any(mark => mark switch
        {
            RuleMark rule => ReferenceEquals(rule.Foreground, StyleFormat.Dark.Important),
            WashMark wash => ReferenceEquals(wash.Fill, StyleFormat.Dark.Important),
            _ => false,
        })), "in the colour kept for it, not the accent every other alert could share");
    }

    [TestMethod]
    public void AnAlertsMarkerIsDrawnAsItsWordOnceAndIsNotWrittenIn()
    {
        var laid = Lay("> [!NOTE]\n> Useful information.\n");

        Assert.IsFalse(Drawn(laid).Contains('['), "the marker is not drawn as it was written");

        var label = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "Note");
        Assert.IsFalse(label.Words!.Writes, "pressing the word it is drawn as shows nothing to write in");
    }

    [TestMethod]
    public void ARuleIsDrawnAcrossWhereTheDashesWere()
    {
        var laid = Lay("one\n\n---\n\ntwo\n");

        Assert.AreEqual(1, Pieces(laid, MarkdownPieces.Rule).Count);
        Assert.IsFalse(Drawn(laid).Contains('-'), "and the dashes are not drawn");
    }

    [TestMethod]
    public void ARuleIsPressedAsTheBandItSitsInAndTakesNoCaret()
    {
        var laid = Lay("one\n\n---\n\ntwo\n");
        var rule = Pieces(laid, MarkdownPieces.Rule).Single();

        Assert.IsTrue(rule.Bounds.Height >= 8, $"a band a pointer can land on, not a hairline ({rule.Bounds.Height:0.#} tall)");
        Assert.AreEqual(Stops.None, rule.Stops, "there is nothing in a rule to write, so the pointer over it is no bar");
    }

    [TestMethod]
    public void AQuoteIsBarredInTheAccent()
    {
        var laid = Lay("> quoted\n");
        var quote = Pieces(laid, MarkdownPieces.Block).First(piece => Marks(piece).OfType<RuleMark>().Any());

        Assert.AreEqual(StyleFormat.Dark.Accent, Marks(quote).OfType<RuleMark>().Single().Foreground);
    }

    [TestMethod]
    public void AQuoteIsItsWordsBesideABarWithoutTheMarks()
    {
        var laid = Lay("> quoted\n> more\n");

        StringAssert.Contains(Drawn(laid), "quoted");
        Assert.IsFalse(Drawn(laid).Contains('>'), "the marks are not drawn");
        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).OfType<RuleMark>().Any()), "and a bar stands beside it");
    }

    [TestMethod]
    public void EveryItemOfAListWithGapsBetweenIsDrawn()
    {
        var laid = Lay("- one\n\n- two\n\n- three\n");

        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, Words(laid).Select(piece => piece.Words!.Glyphs.Text.Trim()).ToArray());
    }

    [TestMethod]
    public void IndentedCodeIsShownOnAPanelOfItsOwn()
    {
        var laid = Lay("    var x = 1;\n");

        var shown = Pieces(laid, MarkdownPieces.Verbatim).Single();
        StringAssert.Contains(shown.Words!.Glyphs.Text, "var x = 1;");
        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).OfType<WashMark>().Any()), "on a panel, as code is");
    }

    // ── Words ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void CodeInASentenceIsItsOwnWordsWithoutTheBackticks()
    {
        var words = Words(Lay("a `b` c\n")).Select(piece => piece.Words!.Glyphs.Text).ToList();

        CollectionAssert.Contains(words, "b");
        Assert.IsFalse(words.Any(said => said.Contains('`')));
    }

    [TestMethod]
    public void AReferenceLinkGoesWhereItsDefinitionSaysAndTheDefinitionIsNotDrawn()
    {
        var laid = Lay("see [the page][ref]\n\n[ref]: https://example.org/page\n");

        CollectionAssert.Contains(Targets(laid), "https://example.org/page");
        Assert.IsFalse(Drawn(laid).Contains("[ref]:"), "the definition says where a link goes; it is not part of the page");
    }

    [TestMethod]
    public void AnAddressInAngleBracketsIsALinkToItself()
    {
        var laid = Lay("go to <https://example.org> now\n");

        CollectionAssert.Contains(Targets(laid), "https://example.org");
        Assert.IsFalse(Drawn(laid).Contains('<'), "and the brackets are not drawn");
    }

    [TestMethod]
    public void ABareAddressIsALinkToItself()
    {
        CollectionAssert.Contains(Targets(Lay("see https://example.com now\n")), "https://example.com");
    }

    [TestMethod]
    public void APictureOnAnotherMachineIsNeverFetchedAndItsWordsAreDrawnInstead()
    {
        var laid = Lay("![alt text](https://example.com/x.png)\n");

        Assert.IsFalse(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).OfType<PictureMark>().Any()), "nothing was fetched");
        StringAssert.Contains(Drawn(laid), "alt text");
    }

    [TestMethod]
    public void StruckWordsAreTheirOwnPieceWithoutTheTildes() => SetApart("a ~~gone~~ b\n", "gone", MarkdownKinds.Strike);

    [TestMethod]
    public void InsertedWordsAreTheirOwnPieceWithoutThePluses() => SetApart("a ++new++ b\n", "new", MarkdownKinds.Insert);

    [TestMethod]
    public void MarkedWordsAreWashedBehind()
    {
        var laid = SetApart("a ==lit== b\n", "lit", MarkdownKinds.Mark);

        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => Marks(piece).OfType<WashMark>().Any()), "a wash behind them");
    }

    [TestMethod]
    public void ADisplayFormulaWithACommandItCannotDrawIsStillSet_WithAWaveUnderTheCommand()
    {
        const string source = "$$ \\sideset{_a^b}{_c^d}\\sum $$\n";
        var laid = Lay(source);

        Assert.IsFalse(Words(laid).Any(piece => piece.Words!.Glyphs.Text.Contains("$$")), "set as a formula, not shown as its source");
        Assert.IsTrue(laid.Trouble.Any(one => source.Substring(one.Start, one.Length).Contains("sideset")),
                      "with the trouble said where it is: " + string.Join(" | ", laid.Trouble.Select(one => one.Message)));
    }

    [TestMethod]
    public void ABlockItsLanguageCannotReadIsItsSourceWithWhatIsWrongWithIt()
    {
        var laid = Lay("```barcode\nformat: NOTAFORMAT\nvalue: 12345\n```\n");
        var drawn = Drawn(laid);

        StringAssert.Contains(drawn, "format: NOTAFORMAT", "the characters written, to put right");
        StringAssert.Contains(drawn, "Unknown barcode format 'NOTAFORMAT'", "and what is wrong with them");
        StringAssert.Contains(drawn, "EAN13", "with what it could have said instead");
    }

    [TestMethod]
    public void APlotThatCannotBeReadIsItsSourceWithWhatIsWrongWithIt()
    {
        var laid = Lay("```scatter\nwidth: wide\n\n1 2\n3 4\n```\n");
        var drawn = Drawn(laid);

        StringAssert.Contains(drawn, "width: wide", "the characters written, to put right");
        StringAssert.Contains(drawn, "width", "and what is wrong with them, under them");
        Assert.IsTrue(drawn.Split("width").Length > 2, $"the reason as well as the source: {drawn}");
    }

    [TestMethod]
    public void AConstructStartsPastTheSpaceBeforeIt()
    {
        var laid = Lay("The `x` block and **y** is\nparsed, ==marked `code`== too.\n");
        var words = laid.Root.Placed().Where(one => one.Piece.Words is not null).OrderBy(one => one.Where.Left).ToList();

        Assert.IsTrue(words.Count > 6, "the sentence is several pieces");

        for (var at = 1; at < words.Count; at++)
        {
            var (before, where) = words[at - 1];
            var glyphs = before.Words!.Glyphs;
            if (!glyphs.Text.EndsWith(' ')) continue;

            Assert.IsTrue(words[at].Where.Left >= where.Left + glyphs.WidthIncludingTrailingWhitespace - 0.5,
                          $"'{words[at].Piece.Words!.Glyphs.Text}' starts past the space after '{glyphs.Text}'");
        }
    }

    [TestMethod]
    public void CodeInASentenceIsSetInTheAccentEvenInsideAWash()
    {
        var laid = Lay("a ==marked `code`== b\n");
        var code = Words(laid).Single(piece => piece.Words!.Glyphs.Text == "code");

        Assert.AreEqual(StyleFormat.Dark.Accent, Marks(code).OfType<TextMark>().Single().Foreground);
    }

    [TestMethod]
    public void ASubscriptIsSetSmallerThanTheWordsItSitsIn() => Smaller("H~2~O\n", "2");

    [TestMethod]
    public void ASuperscriptIsSetSmallerThanTheWordsItSitsIn() => Smaller("e=mc^2^\n", "2");

    [TestMethod]
    public void AnAbbreviationsDefinitionIsNotPartOfThePage()
    {
        var laid = Lay("HTML is great.\n\n*[HTML]: HyperText Markup Language\n");

        Assert.IsFalse(Drawn(laid).Contains("HyperText Markup Language"));
    }

    [TestMethod]
    public void AnAbbreviationIsKnownWhereverItsDefinitionIsWritten() =>
        SetApart("*[HTML]: HyperText Markup Language\n\nHTML is a thing\n", "HTML", MarkdownKinds.Abbreviation);

    [TestMethod]
    public void AReferenceLinkInAListOrAQuoteGoesWhereTheDocumentSays()
    {
        var laid = Lay("- see [one][ref]\n\n> and [two][ref]\n\n[ref]: https://example.org/page\n");

        Assert.AreEqual(2, Targets(laid).Count(target => target == "https://example.org/page"));
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ATableWithoutOuterPipesIsStillATable() => Cells("a | b\n--- | ---\n1 | 2\n", "1", "2");

    [TestMethod]
    public void ATableDirectlyUnderALineOfWordsStillForms() => Cells("Some intro text\n| a | b |\n|---|---|\n| 1 | 2 |\n", "1", "2");

    [TestMethod]
    public void ATableInsideAQuoteIsATable() => Cells("> | a | b |\n> |---|---|\n> | 1 | 2 |\n", "1", "2");

    [TestMethod]
    public void ARowWithMoreCellsThanTheHeadStillDraws() => Cells("| a | b |\n|---|---|\n| 1 | 2 | 3 |\n", "1", "2");

    [TestMethod]
    public void ATableWrittenWithWindowsLineEndingsIsATable() => Cells("| a | b |\r\n|---|---|\r\n| 1 | 2 |\r\n", "1", "2");

    [TestMethod]
    public void AnEmptyCellIsACellWithNothingInIt() => Cells("| a | b |\n|---|---|\n|  | 2 |\n", "2");

    [TestMethod]
    public void AnEscapedPipeIsPartOfTheCellAndDoesNotSplitIt()
    {
        var laid = Lay("| a | b |\n|---|---|\n| x \\| y | z |\n");

        Assert.AreEqual(4, Pieces(laid, MarkdownPieces.Cell).Count, "two columns, not three");
        StringAssert.Contains(Drawn(laid), "x | y");
    }

    [TestMethod]
    public void WordsInACellAreSetAsWordsAnywhereElseAre()
    {
        var laid = Lay("| h |\n|---|\n| **b** `c` [l](http://x) |\n");

        var words = Words(laid).Select(piece => piece.Words!.Glyphs.Text.Trim()).ToList();
        CollectionAssert.IsSubsetOf(new[] { "b", "c", "l" }, words, "each its own piece, without its marks");
        CollectionAssert.Contains(Targets(laid), "http://x", "and the link goes where it says");
    }

    [TestMethod]
    public void AGridTablesCellsAreDrawnInItsColumns() =>
        Cells("+---+---+\n| a | b |\n+===+===+\n| 1 | 2 |\n+---+---+\n", "a", "b", "1", "2");

    [TestMethod]
    public void AGridTableCellCanSpanTheColumnsUnderIt()
    {
        var laid = Lay("+-------+------+\n| Big          |\n+=======+======+\n| a     | b    |\n+-------+------+\n");
        var cells = Pieces(laid, MarkdownPieces.Cell);

        Piece Holding(string said) => cells.First(cell => cell.SelfAndDescendants().Any(piece => piece.Words?.Glyphs.Text.Trim() == said));

        var big = Holding("Big");
        var under = Holding("a").Bounds.Width + Holding("b").Bounds.Width;

        Assert.IsTrue(big.Bounds.Width > under * 0.9, $"the merged cell is {big.Bounds.Width:F0} wide over {under:F0} of columns");
    }

    [TestMethod]
    public void AGridTableCellHoldingAListDrawsTheList() =>
        Cells("+---------+-----+\n| a       | b   |\n+=========+=====+\n| - one   | y   |\n| - two   |     |\n+---------+-----+\n",
              "one", "two");

    [TestMethod]
    public void AColumnSqueezedForRoomStillHoldsItsLongestWord()
    {
        var notes = string.Join(" ", Enumerable.Repeat("a long explanation of what it does", 8));
        var laid = Lay($"| Feature | Call | Notes |\n|---|---|---|\n| Abbreviations | `UseAbbreviations()` | {notes} |\n"
                       + "| YAML front matter | `UseYamlFrontMatter()` | ✅ (not drawn) |\n", room: 420);

        // A cell's own box grows to take in whatever it holds, so the columns are read off the head, which holds one word each.
        var placed = laid.Root.Placed().ToList();
        var edges = placed.Where(one => one.Piece.Kind == MarkdownPieces.Cell).Take(3).Select(one => one.Where.Right).ToList();

        foreach (var (words, where) in placed.Where(one => one.Piece.Words is not null))
        {
            var edge = edges.First(right => where.Left < right);

            Assert.IsTrue(where.Right <= edge + 0.5,
                          $"'{words.Words!.Glyphs.Text}' reaches {where.Right:0.#}, past its column's edge at {edge:0.#}");
        }
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static Laid Lay(string source, double room = 480) => MarkdownBuilder.Lay(source, StyleFormat.Dark, room);

    private static List<Piece> Pieces(Laid laid, string kind) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static List<Piece> Words(Laid laid) =>
        [.. Pieces(laid, MarkdownPieces.Words).Where(piece => piece.Words!.Glyphs.Text.Trim().Length > 0)];

    private static string Drawn(Laid laid) =>
        string.Concat(laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null).Select(piece => piece.Words!.Glyphs.Text));

    private static List<LayoutMark> Marks(Piece piece)
    {
        var found = new List<LayoutMark>();
        foreach (var mark in piece.Marks) found.Add(mark);
        return found;
    }

    /// <summary>Where every link on the page goes.</summary>
    private static List<string> Targets(Laid laid) =>
        [.. laid.Root.SelfAndDescendants()
            .Select(piece => piece.Acts?.Click)
            .Where(act => act is { Verb: LayoutVerbs.Navigate })
            .Select(act => act!.Value.Target ?? string.Empty)];

    /// <summary>The words drawn without their marks, as a piece of their own, drawn from the construct that marked them.</summary>
    private static Laid SetApart(string source, string words, string kind)
    {
        var laid = Lay(source);
        var piece = Words(laid).SingleOrDefault(one => one.Words!.Glyphs.Text == words);

        Assert.IsTrue(piece.Exists, $"'{words}' is a piece of its own, without its marks");
        Assert.IsTrue(piece.Naming() is ContentPart part && (part.Kind == kind || part.Ancestors().Any(above => above.Kind == kind)),
                      $"drawn from the {kind} that marked it");

        return laid;
    }

    /// <summary>The words set smaller than the words round them.</summary>
    private static void Smaller(string source, string words)
    {
        var all = Words(Lay(source));
        var raised = all.Single(one => one.Words!.Glyphs.Text == words);
        var rest = all.First(one => one.Words!.Glyphs.Text != words);

        Assert.IsTrue(raised.Bounds.Height < rest.Bounds.Height, $"'{words}' is set smaller than '{rest.Words!.Glyphs.Text}'");
    }

    /// <summary>That the document drew a table, and every one of <paramref name="words"/> in it.</summary>
    private static void Cells(string source, params string[] words)
    {
        var laid = Lay(source);

        Assert.IsTrue(Pieces(laid, MarkdownPieces.Cell).Count > 0, "a table was drawn");

        foreach (var said in words) StringAssert.Contains(Drawn(laid), said);
    }
}
