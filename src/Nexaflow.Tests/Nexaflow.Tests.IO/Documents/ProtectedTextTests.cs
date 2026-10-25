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

    [TestMethod]
    public void AMarkAnAnswerMadeUp_IsTakenOut_AndOneWrittenAsNoMark_IsAProblem()
    {
        const string Plain = "die Ladung Q erfüllt sie";

        Assert.AreEqual("the charge Q satisfies it", ProtectedText.Mended(Plain, "the charge ⟦1⟧ Q satisfies it"), "a mark it made up stands for nothing");
        Assert.IsNull(ProtectedText.Problem(Plain, ProtectedText.Mended(Plain, "the charge ⟦1⟧ Q satisfies it")));
        Assert.AreEqual("keep ⟦0⟧ here", ProtectedText.Mended("behalte ⟦0⟧ hier", "keep ⟦0⟧ here"), "its own marks stay as it gave them");

        StringAssert.Contains(ProtectedText.Problem("die Gleichungen ⟦3⟧ und ⟦4⟧", "the equations ⟦3,4⟧"), "⟦3,4⟧ is no mark",
            "something written as a mark that is none never reaches the document");
    }

    [TestMethod]
    public void ATagNamedByWords_IsNoMarkOfThePassage_UnlessThePassageWritesIt()
    {
        // What a model does with the pairs it is shown, given a glossary: marks the terms the same way, named by the terms.
        const string Source = "eine gewöhnliche Differentialgleichung (GDG)";

        StringAssert.Contains(ProtectedText.Problem(Source, "an <ordinary differential equation>ordinary differential equation</ordinary differential equation> (GDG)"),
            "<ordinary differential equation> is no mark", "so it is asked for again rather than written into the document as words");
        StringAssert.Contains(ProtectedText.Problem("eine <1>GDG</1>", "an <1><GDG>GDG</GDG></1>"), "<GDG> is no mark", "inside one of its own pairs too");

        Assert.IsNull(ProtectedText.Problem("wenn a < b und c > d", "if a < b and c > d"), "a comparison is not a tag");
        Assert.IsNull(ProtectedText.Problem("der Typ <T>", "the type <T>"), "and a passage that writes one keeps it");
    }

    [TestMethod]
    public void WhatIsWrong_IsTheSentencesRoundIt_BoundedBySentencesTheAnswerHasRight()
    {
        const string source = "A ⟦0⟧ a. B ⟦1⟧ b. C ⟦2⟧ c. D ⟦3⟧ d. E ⟦4⟧ e. F ⟦5⟧ f.";
        const string doubled = "AA ⟦0⟧ aa. BB ⟦1⟧ bb. CC ⟦2⟧ cc. DD ⟦3⟧ ⟦3⟧ dd. EE ⟦4⟧ ee. FF ⟦5⟧ ff.";
        const string missing = "AA ⟦0⟧ aa. BB ⟦1⟧ bb. CC ⟦2⟧ cc. DD dd. EE ⟦4⟧ ee. FF ⟦5⟧ ff.";

        var wrong = ProtectedText.Wrong(source, doubled)!.Value;
        Assert.AreEqual("C ⟦2⟧ c. D ⟦3⟧ d. E ⟦4⟧ e.", source[wrong.SourceFrom..wrong.SourceTo].Trim(), "the sentence holding it, and the right ones either side");
        Assert.AreEqual("CC ⟦2⟧ cc. DD ⟦3⟧ ⟦3⟧ dd. EE ⟦4⟧ ee.", doubled[wrong.AnswerFrom..wrong.AnswerTo].Trim(), "and what translates them");

        var lost = ProtectedText.Wrong(source, missing)!.Value;
        Assert.AreEqual("CC ⟦2⟧ cc. DD dd. EE ⟦4⟧ ee.", missing[lost.AnswerFrom..lost.AnswerTo].Trim(), "a missing mark found by where the source has it");

        Assert.IsNull(ProtectedText.Wrong(source, doubled.Replace("⟦3⟧ ⟦3⟧", "⟦3⟧")), "nothing wrong, nothing to ask for again");
    }
}
