using System;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Coverage for the seam between <see cref="InlineMarkdownEditor"/> and a formula inside it: a symbol
/// lands in the formula rather than under it, and typing into it reaches the document's own model.
///
/// The bug these exist for is the one the palette shipped with: inserting went through
/// <c>InsertMarkdownAtCaret</c>, which appends a whole new block after the caret's, so pressing a
/// symbol pushed the expression onto the next line and lost the caret. There was no such thing as
/// "the caret inside a formula" for it to aim at.
///
/// Interactive desktop only — the editor builds its document during a render pass, so it has to be in
/// a shown window. Run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("latex-document-seam")]
public class FormulaInEditorTests
{
    private const string OneFormula = "$$\nx+\n$$";

    /// <summary>The editor builds its document during a render pass, and WPF needs an STA thread.</summary>
    private static void InEditor(string markdown, Action<InlineMarkdownEditor, RichTextBox> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(markdown, test));

    [TestMethod]
    public void APaletteSymbolLandsInsideTheFormula() => InEditor(OneFormula, (editor, _) =>
    {
        Assert.IsTrue(editor.InsertLatexAtCaret(@"\alpha "), "the block is a formula, so there is one to type into");

        StringAssert.Contains(editor.Markdown, @"x+\alpha ");
        Assert.AreEqual(1, CountBlocks(editor.Markdown),
            "and it stays one block — the old path appended a second one under it");
    });

    [TestMethod]
    public void ATemplateLeavesTheCaretInsideIt() => InEditor(OneFormula, (editor, _) =>
    {
        editor.InsertLatexAtCaret(@"\frac{}{}", caretBack: 3);

        StringAssert.Contains(editor.Markdown, @"x+\frac{}{}");
        Assert.AreEqual(1, CountBlocks(editor.Markdown));
    });

    [TestMethod]
    public void WrappingBracketsTheFormula() => InEditor(OneFormula, (editor, _) =>
    {
        // The braces are all that is written. An argument left empty becomes a hole when it is parsed,
        // and the hole draws itself — so the source stays what the reader asked for.
        Assert.IsTrue(editor.WrapLatexAtCaret(@"\sqrt{", "}"));
        StringAssert.Contains(editor.Markdown, @"\sqrt{}");
    });

    [TestMethod]
    public void TypingReachesTheFormulaAndTheModel() => InEditor(OneFormula, (editor, rtb) =>
    {
        // Adopt the formula the way a palette press does, then type as a keyboard would.
        editor.InsertLatexAtCaret("2");
        MarkdownEditorHarness.Type(rtb, "+3");

        StringAssert.Contains(editor.Markdown, "x+2+3",
            "keys typed while a formula holds the caret must reach it, and reach the block model");
    });

    [TestMethod]
    public void ADocumentWithoutAFormulaSaysSo() => InEditor("just prose\n", (editor, _) =>
        Assert.IsFalse(editor.InsertLatexAtCaret(@"\alpha"),
            "so the caller can fall back to inserting it as text"));

    /// <summary>How many <c>$$</c>-fenced blocks the markdown holds.</summary>
    private static int CountBlocks(string markdown) =>
        markdown.Split("$$").Length / 2;
}
