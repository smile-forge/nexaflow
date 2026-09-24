using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Prose.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// Reading each block's body with the parser its kind names, which is what turns a list of blocks into a
/// document read all the way down.
///
/// <para>
/// The rule a stage lives under is the whole of what is checked first: the characters coming out are the
/// ones that went in, however far down the tree goes. Everything after that is about what each kind of block
/// turned out to be made of, and about the marks a writer typed being still where they typed them — because
/// those marks are what a reader puts a caret in.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class WithBlocksTests
{
    private static readonly (string What, string Source)[] Documents =
    [
        ("nothing at all", ""),
        ("one paragraph", "Words with **bold** and `code`.\n"),
        ("a heading", "# A *slanted* title\n"),
        ("a list", "- one\n- two\n- three\n"),
        ("a numbered list", "1. one\n2. two\n"),
        ("a list numbered all ones", "1. one\n1. two\n"),
        ("a nested list", "- one\n  - under\n- two\n"),
        ("a task list", "- [ ] to do\n- [x] done\n"),
        ("an item holding two paragraphs", "- one\n\n  and more\n"),
        ("a quote", "> quoted\n> more\n"),
        ("a quote of two paragraphs", "> quoted\n>\n> and more\n"),
        ("a quote holding a list", "> - one\n> - two\n"),
        ("a quote inside a quote", "> > deep\n"),
        ("an alert", "> [!NOTE]\n> Something worth knowing.\n"),
        ("a pipe table", "| a | b |\n|---|---|\n| 1 | 2 |\n"),
        ("a table with alignment", "| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n"),
        ("a table with no outer pipes", "a | b\n--|--\n1 | 2\n"),
        ("a table with marks in a cell", "| a | b |\n|---|---|\n| **one** | `two` |\n"),
        ("a table with a row missing", "| a | b |\n|---|---|\n"),
        ("a fence", "```csharp\nvar x = 1;\n```\n"),
        ("a fence never closed", "```csharp\nvar x = 1;\n"),
        ("a formula on its own line", "$$\n\\frac{x^2}{2}\n$$\n"),
        ("a formula never closed", "$$\n\\frac{x^2}{2}\n"),
        ("an empty formula", "$$\n$$\n"),
        ("a formula in a sentence", "The value $x^2$ and then some.\n"),
        ("two formulas in a sentence", "From $a$ to $b$.\n"),
        ("a formula holding a dollar", "Costs $\\$5$ exactly.\n"),
        ("a formula nobody closed in a sentence", "The value $x^2 and then some.\n"),
        ("front matter", "---\ntitle: A Thing\n---\n\nwords\n"),
        ("a definition list", "Term\n:   what it means\n"),
        ("a definition list of two", "One\n:   first\n\nTwo\n:   second\n"),
        ("a figure", "^^^\nwords inside\n^^^ a caption\n"),
        ("a footer", "^^ at the foot\n"),
        ("a citation", "he said \"\"so\"\" once\n"),
        ("an abbreviation", "*[HTML]: HyperText Markup Language\n\nHTML is a thing\n"),
        ("a backslash escape", "not \\*emphasis\\* at all\n"),
        ("an escape at the end", "trailing backslash \\\n"),
        ("only a backslash", "\\\n"),
        ("a lettered list", "a. one\nb. two\n"),
        ("a roman list", "i. one\nii. two\n"),
        ("a list starting at seven", "7. seven\n8. eight\n"),
        ("indented code", "    indented\n    code\n"),
        ("everything at once",
         "# Title\n\nWords with **bold**.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- [ ] a task\n\n"
         + "```mermaid\npie\n```\n\n> quoted\n"),
        ("windows line endings", "# Title\r\n\r\n- one\r\n- two\r\n"),
        ("only blank lines", "\n\n\n"),
        ("only space", "   "),
    ];

    // ── The rule every stage lives under ────────────────────────────────────

    [TestMethod]
    public void EveryDocumentStillPrintsAsItWasWritten()
    {
        foreach (var (what, source) in Documents)
            Assert.AreEqual(source, Read(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryDocumentDoesToo()
    {
        // Half-written input is what an editor holds all day, and a stage sees every keystroke of it.
        foreach (var (what, source) in Documents)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, Read(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void NothingReadOutOfABlockWasMadeUp()
    {
        foreach (var (what, source) in Documents)
            foreach (var place in Read(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void ReadingADocumentTwiceIsTheSameAsReadingItOnce()
    {
        // A body that is already a tree is left alone, which is what stops a block whose own source reads
        // back as itself from going round for ever.
        foreach (var (what, source) in Documents)
        {
            var once = Read(source);

            Assert.IsTrue(once.Same(new WithBlocks().Run(once)), what);
        }
    }

    // ── What a block turned out to be made of ───────────────────────────────

    [TestMethod]
    public void AParagraphIsTheConstructsItWasSpelledWith()
    {
        var paragraph = Blocks("Words with **bold** in them.\n")[0];

        Assert.AreEqual(MarkdownKinds.Words, Body(paragraph).Kind);
        Assert.AreEqual(1, Every(paragraph, MarkdownKinds.Strong).Count);
    }

    [TestMethod]
    public void AHeadingKeepsItsHashesAndReadsTheRest()
    {
        var heading = Blocks("# A *slanted* title\n")[0];

        Assert.AreEqual(MarkdownKinds.Heading, heading.Kind);
        Assert.AreEqual(1, Every(heading, MarkdownKinds.Emphasis).Count);
        Assert.AreEqual("# A *slanted* title\n", heading.Print());
    }

    // ── A table ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ATableIsItsRowsAndTheirCells()
    {
        var table = Blocks("| a | b |\n|---|---|\n| 1 | 2 |\n")[0];

        Assert.AreEqual(MarkdownKinds.Table, table.Kind);
        Assert.AreEqual(2, Every(table, MarkdownKinds.Row).Count);
        Assert.AreEqual(4, Every(table, MarkdownKinds.Cell).Count);
    }

    [TestMethod]
    public void TheRowThatNamesTheColumnsSaysSo()
    {
        var rows = Every(Blocks("| a | b |\n|---|---|\n| 1 | 2 |\n")[0], MarkdownKinds.Row);

        Assert.AreEqual(MarkdownRoles.Head, rows[0].Role);
        Assert.AreEqual(Roles.Row, rows[1].Role);
    }

    [TestMethod]
    public void ACellsWordsAreReadAsAnyOtherWordsAre()
    {
        var table = Blocks("| a | b |\n|---|---|\n| **one** | `two` |\n")[0];

        Assert.AreEqual(1, Every(table, MarkdownKinds.Strong).Count);
        Assert.AreEqual(1, Every(table, MarkdownKinds.Code).Count);
    }

    [TestMethod]
    public void ACellSaysWhichWayItIsSetAlthoughNobodyWroteThatOnIt()
    {
        // The colons are in a rule of their own, in another row — so the alignment is worked out, and
        // nothing that measures source may see it.
        var cells = Every(Blocks("| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n")[0], MarkdownKinds.Cell);

        Assert.AreEqual(MarkdownAligns.Left, cells[0].Part(Roles.Derived)?.Held);
        Assert.AreEqual(MarkdownAligns.Center, cells[1].Part(Roles.Derived)?.Held);
        Assert.AreEqual(MarkdownAligns.Right, cells[2].Part(Roles.Derived)?.Held);
        Assert.AreEqual(0, cells[0].Part(Roles.Derived)!.Width);
    }

    [TestMethod]
    public void TheRuleAndThePipesBelongToNoCell()
    {
        var table = Blocks("| a | b |\n|---|---|\n| 1 | 2 |\n")[0];

        // A cell is what stands between two pipes, spaces and all: they are inside it, not between the cells.
        Assert.AreEqual(" a ", Body(Every(table, MarkdownKinds.Cell)[0]).Print());
        Assert.AreEqual("| a | b |\n|---|---|\n| 1 | 2 |\n", table.Print());
    }

    // ── A list ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AListIsTheItemsWrittenInIt()
    {
        Assert.AreEqual(3, Every(Blocks("- one\n- two\n- three\n")[0], MarkdownKinds.Item).Count);
    }

    [TestMethod]
    public void AnItemsMarkerIsKeptExactlyWhereItWasTyped()
    {
        // A writer who numbered every line 1. gets back what they wrote; a builder that wants to draw 2.
        // counts items and never reads a character.
        var items = Every(Blocks("1. one\n1. two\n")[0], MarkdownKinds.Item);

        Assert.AreEqual("1. one\n", items[0].Print());
        Assert.AreEqual(Roles.Trivia, items[0].Children[0].Role);
        Assert.AreEqual("1. ", items[0].Children[0].Text);
    }

    [TestMethod]
    public void ANestedListIsOneMoreBlockInsideAnItem()
    {
        var items = Every(Blocks("- one\n  - under\n- two\n")[0], MarkdownKinds.Item);

        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(1, Every(items[0], MarkdownKinds.List).Count);
    }

    [TestMethod]
    public void AnItemHoldingTwoParagraphsHoldsTwoBlocks()
    {
        var item = Every(Blocks("- one\n\n  and more\n")[0], MarkdownKinds.Item)[0];

        Assert.AreEqual(2, Every(item, MarkdownKinds.Paragraph).Count);
    }

    /// <summary>
    /// The box belongs to the item and not to the paragraph its characters sit in — which is the only place it
    /// can belong, because read on its own <c>[x] done</c> is a bracket and a word.
    /// </summary>
    [TestMethod]
    public void ATickIsFoundOnTheItemItWasWrittenOn()
    {
        var list = Blocks("- [x] done\n- [ ] to do\n")[0];
        var items = Every(list, MarkdownKinds.Item);
        var ticks = Every(list, MarkdownKinds.Task);

        Assert.AreEqual(2, ticks.Count);
        Assert.IsNotNull(ticks[0].Part(MarkdownRoles.Done));
        Assert.IsNotNull(ticks[1].Part(MarkdownRoles.Todo));
        Assert.AreEqual("[x]", ticks[0].Print());

        CollectionAssert.Contains(items[0].Children.ToList(), ticks[0]);
    }

    // ── A quote ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AQuoteIsTheBlocksWrittenInsideIt()
    {
        var quote = Blocks("> quoted\n>\n> and more\n")[0];

        Assert.AreEqual(MarkdownKinds.Quote, quote.Kind);
        Assert.AreEqual(2, Every(quote, MarkdownKinds.Paragraph).Count);
    }

    [TestMethod]
    public void AQuotesMarksAreNotPartOfWhatItHolds()
    {
        var body = Body(Blocks("> quoted\n")[0]);

        Assert.AreEqual(Roles.Trivia, body.Children[0].Role);
        Assert.AreEqual("> ", body.Children[0].Text);
    }

    [TestMethod]
    public void AQuoteHoldingAListHoldsAList()
    {
        Assert.AreEqual(2, Every(Blocks("> - one\n> - two\n")[0], MarkdownKinds.Item).Count);
    }

    [TestMethod]
    public void AnAlertIsReadTheWayAQuoteIs()
    {
        var alert = Blocks("> [!NOTE]\n> Something worth knowing.\n")[0];

        Assert.AreEqual(MarkdownKinds.Alert, alert.Kind);
        Assert.AreNotEqual(0, Every(alert, MarkdownKinds.Paragraph).Count);
    }

    // ── What has no reader yet ──────────────────────────────────────────────

    [TestMethod]
    public void AFenceIsLeftToItsOwnLanguage()
    {
        var fence = Blocks("```mermaid\npie\n```\n")[0];

        Assert.AreEqual(Kinds.Verbatim, Body(fence).Kind);
        Assert.AreEqual("pie\n", Body(fence).Text);
    }

    [TestMethod]
    public void AFormulaOnItsOwnLineIsAFenceSpelledWithDollars()
    {
        var maths = Blocks("$$\n\\frac{x^2}{2}\n$$\n")[0];

        Assert.AreEqual(MarkdownKinds.Math, maths.Kind);

        // The same shape a fence has, because it is the same thing: a delimiter, another language, a delimiter.
        // Nobody writes the language after the $$, so there is no name — which is the only difference.
        Assert.AreEqual("$$", maths.Part(Roles.Open)?.Text);
        Assert.IsNull(maths.Part(Roles.Name));
        Assert.AreEqual(Kinds.Verbatim, Body(maths).Kind);
        Assert.AreEqual("\\frac{x^2}{2}\n", Body(maths).Text);
        Assert.AreEqual("$$\n", maths.Part(Roles.Close)?.Text);
    }

    [TestMethod]
    public void AFormulaInASentenceIsTheDollarsAndWhatIsBetweenThem()
    {
        var words = Blocks("The value $x^2$ and then some.\n")[0];

        var maths = words.Part(Roles.Body)!.Children
            .Where(child => child.Kind == MarkdownKinds.Formula)
            .ToList();

        Assert.AreEqual(1, maths.Count);
        Assert.AreEqual("$", maths[0].Part(Roles.Open)?.Text);
        Assert.AreEqual("x^2", maths[0].Part(Roles.Body)?.Text);
        Assert.AreEqual("$", maths[0].Part(Roles.Close)?.Text);

        // And it still prints as the sentence somebody typed, marks and all.
        Assert.AreEqual("$x^2$", maths[0].Print());
    }

    [TestMethod]
    public void AKindNothingReadsYetIsStillTheSourceItWas()
    {
        var front = Blocks("---\ntitle: A Thing\n---\n\nwords\n")[0];

        Assert.AreEqual(MarkdownKinds.FrontMatter, front.Kind);
        Assert.AreEqual(Kinds.Verbatim, Body(front).Kind);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static ContentNode Read(string source) => MarkdownParser.Reader.Run(MarkdownParser.Read(source));

    private static IReadOnlyList<ContentNode> Blocks(string source) =>
        [.. Read(source).Children.Where(child => child.Role != Roles.Trivia)];

    private static ContentNode Body(ContentNode node) =>
        node.Part(Roles.Body) ?? throw new AssertFailedException($"{node.Kind} has no body");

    private static IReadOnlyList<ContentNode> Every(ContentNode node, string kind) =>
        [.. node.SelfAndDescendants().Where(child => child.Kind == kind)];
}
