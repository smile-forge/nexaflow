using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// A block's words with everything else held back: the markup handed over as numbered marks a model cannot break, an answer
/// checked to keep each once, and the block written again with the parser spelling the new words — then read again, to prove
/// its markup still reads as the markup it was.
/// </summary>
[TestClass]
[CoversNode("documents-protected-text")]
public class ProtectedTextTests
{
    private const string Paragraph = "Dogs **bark** at [the moon](https://moon.example \"Moon\") and `ls -la` too.\n";

    private static Nexaflow.Markdown.Ast.ContentPart Block(string source) => DocumentReading.Of(source) is var read ? read.Blocks(0, read.Root.Children.Count).First() : null!;

    [TestMethod]
    public void OnlyTheWordsAreHandedOver()
    {
        var text = ProtectedText.Of(Block(Paragraph));

        foreach (var held in new[] { "https://moon.example", "**", "](" })
            Assert.IsFalse(text.Masked.Contains(held), $"'{held}' is held back");
        StringAssert.Contains(text.Masked, "⟦0 `ls -la`⟧", "inline code is held back, but shown, so it can be placed in the sentence");
        foreach (var words in new[] { "Dogs", "bark", "the moon", "too." })
            StringAssert.Contains(text.Masked, words);
    }

    [TestMethod]
    public void TheSameWordsGiveTheSameBlock()
    {
        var block = Block(Paragraph);

        Assert.AreEqual(block.Print(), ProtectedText.Of(block).Rebuilt(ProtectedText.Of(block).Masked));
    }

    [TestMethod]
    public void AnAnswerMissingAMarkOrHoldingOneTwiceOrOneThatWasNeverThere_IsWrong()
    {
        const string Source = "a ⟦0⟧b⟦1⟧ c";

        Assert.IsNull(ProtectedText.Problem(Source, "x ⟦1⟧y⟦0⟧ z"), "moving them is the answer's own business");
        StringAssert.Contains(ProtectedText.Problem(Source, "x ⟦0⟧y z"), "⟦1⟧ is missing");
        StringAssert.Contains(ProtectedText.Problem(Source, "x ⟦0⟧⟦0⟧y⟦1⟧"), "⟦0⟧ is there more than once");
        StringAssert.Contains(ProtectedText.Problem(Source, "x ⟦0⟧y⟦1⟧⟦7⟧"), "⟦7⟧ stands for nothing");

        Assert.IsNull(ProtectedText.Problem("<0>a <1>b</1></0> c", "c <0><1>B</1> A</0>"), "a pair goes where its words go");
        StringAssert.Contains(ProtectedText.Problem("<0>a <1>b</1></0>", "<0>a <1>b</0></1>"), "closes what <0> did not open");
        StringAssert.Contains(ProtectedText.Problem("<0>a</0>", "a</0>"), "<0> is missing");
    }

    [TestMethod]
    public void NewWordsAreSpelledAsWords_SoNothingInThemBecomesMarkup()
    {
        var block = Block("# Title\n");
        var rebuilt = ProtectedText.Of(block).Rebuilt("*Star* [one]");

        Assert.IsTrue(ProtectedText.Keeps(block, rebuilt, string.Empty), rebuilt);
        Assert.IsFalse(Block(rebuilt).SelfAndDescendants().Any(part => part.Kind is Nexaflow.Markdown.Prose.MarkdownKinds.Emphasis or "strong" or Nexaflow.Markdown.Ast.Kinds.Link),
                       $"the words read as words, not as emphasis or a link: {rebuilt}");
        StringAssert.StartsWith(rebuilt, "# ", "and the heading is still a heading");
    }

    [TestMethod]
    public void MarkupRoundWords_IsATagPairRoundThem_AndAPairTurnedInsideOut_NoLongerReadsAsALink()
    {
        var block = Block(Paragraph);
        var text = ProtectedText.Of(block);
        StringAssert.Contains(text.Masked, "<0>bark</0>", "emphasis round the words it marks");
        StringAssert.Contains(text.Masked, "<1>the moon</1>", "a link round its words, its address held with the pair");

        var inside = text.Masked.Replace("<1>the moon</1>", "</1>the moon<1>");

        StringAssert.Contains(ProtectedText.Problem(text.Masked, inside), "closes what <1> did not open");
        Assert.IsFalse(ProtectedText.Keeps(block, text.Rebuilt(inside), string.Empty), "and written so, it is no link");
    }

    [TestMethod]
    public void LinesAParagraphWasWrappedAt_AreSpaces()
    {
        var text = ProtectedText.Of(Block("one\ntwo\n"));

        Assert.AreEqual("one two", text.Masked);
    }

    [TestMethod]
    public void ABlockOfNothingButMarkup_HasNoWordsToHandOver() =>
        Assert.IsFalse(ProtectedText.Of(Block("![](picture.png)\n")).HasWords);

    [TestMethod]
    public void APairIsNeverCutApart()
    {
        var text = ProtectedText.Of(Block($"Start **{string.Join(" ", Enumerable.Repeat("bold words here.", 30))}** end.\n"));

        var pieces = text.Cut(100);

        Assert.AreEqual(text.Masked, string.Concat(pieces));
        Assert.IsTrue(pieces.All(piece => ProtectedText.Problem(piece, piece) is null), "every piece holds whole pairs");
    }

    [TestMethod]
    public void ShownCode_GoesWhereTheAnswerPutsIt_WrittenAsItWas()
    {
        var block = Block("Open a `.md` file.\n");
        var text = ProtectedText.Of(block);
        Assert.AreEqual("Open a ⟦0 `.md`⟧ file.", text.Masked);

        const string Answer = "Ouvrez un fichier ⟦0 `.MD`⟧.";

        Assert.IsNull(ProtectedText.Problem(text.Masked, Answer), "what a shown mark shows is not the answer's to keep");
        Assert.AreEqual("Ouvrez un fichier `.md`.\n", text.Rebuilt(Answer), "the code moves to where French puts it, as it was written");
    }
}
