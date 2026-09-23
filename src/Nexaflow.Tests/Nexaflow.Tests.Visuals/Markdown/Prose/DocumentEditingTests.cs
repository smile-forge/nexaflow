using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// Writing in a whole document on one element: what a key means depends on the language the caret is in.
///
/// <para>
/// <strong>Editing is shared, and a language may say otherwise for its own source.</strong> From the caret up the layout
/// to the part it stands in, and up the syntax tree to the first part another language was written in — that language
/// is asked. A formula spells its commands, a diagram escapes what a place cannot hold and starts its next line, and
/// markdown's own words are written as they are typed. Anywhere a language has nothing to say, a key does to the
/// characters what a key does.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class DocumentEditingTests
{
    // ── Markdown's own words ────────────────────────────────────────────────

    [TestMethod]
    public void WhatIsTypedIsWhatIsOnThePage() => UiThread.Run(() =>
    {
        // A word processor that keeps its documents as markdown: an asterisk typed is an asterisk, so it is written
        // where markdown would read it as itself, and the words around it are not set heavy.
        var element = Element("Some plain text here.", caret: 5);

        foreach (var character in "**bold**") element.Type(character);

        Assert.AreEqual(@"Some \*\*bold\*\*plain text here.", element.Source);
    });

    [TestMethod]
    public void AndWhatWouldStartABlockIsWrittenAsItselfToo() => UiThread.Run(() =>
    {
        var element = Element("words", caret: 0);

        element.Type('#');
        element.Type(' ');

        Assert.AreEqual(@"\# words", element.Source, "the line is not a heading because a hash was typed at its start");
    });

    [TestMethod]
    public void ButInsideAWordNothingNeedsSaying() => UiThread.Run(() =>
    {
        var element = Element("snake", caret: 5);

        foreach (var character in "_case") element.Type(character);

        Assert.AreEqual("snake_case", element.Source);
    });

    [TestMethod]
    public void InCodeACharacterIsOnlyACharacter() => UiThread.Run(() =>
    {
        var element = Element("```\nx\n```\n", caret: 5);

        element.Type('*');

        Assert.AreEqual("```\nx*\n```\n", element.Source, "nothing inside code is read, so nothing needs writing behind a backslash");
    });

    [TestMethod]
    public void EnterInAParagraphStartsTheNextOne() => UiThread.Run(() =>
    {
        var element = Element("one two", caret: 3);

        Settle(element, "\n");

        Assert.AreEqual("one\n\n two", element.Source);
    });

    [TestMethod]
    public void EnterInAListStartsTheNextItem() => UiThread.Run(() =>
    {
        var element = Element("- one\n", caret: 5);

        Settle(element, "\n");

        Assert.AreEqual("- one\n- \n", element.Source);
    });

    [TestMethod]
    public void AndInANumberedOneCountsOn() => UiThread.Run(() =>
    {
        var element = Element("1. one\n", caret: 6);

        Settle(element, "\n");

        Assert.AreEqual("1. one\n2. \n", element.Source);
    });

    [TestMethod]
    public void EnterOnAnItemWithNothingInItEndsTheList() => UiThread.Run(() =>
    {
        var element = Element("- one\n", caret: 5);

        Settle(element, "\n");
        Settle(element, "\n");

        Assert.AreEqual("- one\n\n", element.Source);
    });

    [TestMethod]
    public void BackspaceAtTheStartOfAParagraphJoinsItToTheOneBefore() => UiThread.Run(() =>
    {
        var element = Element("one\n\ntwo", caret: 5);

        element.Backspace();

        Assert.AreEqual("onetwo", element.Source);
    });

    // ── A formula in the document ───────────────────────────────────────────

    [TestMethod]
    public void AFormulaInTheDocumentSpellsItsCommandsAsAFormulaOnItsOwnDoes() => UiThread.Run(() =>
    {
        const string source = "$$\nx\n$$\n";
        var element = Element(source, caret: 4);

        foreach (var character in @"\alpha") element.Type(character);

        Assert.AreEqual("$$\nx\\alpha\n$$\n", element.Source, "a backslash is not written behind another inside maths");
        Assert.AreEqual((4, 6), element.ShownAsWritten, "the command is shown as it is spelled");
        Assert.IsFalse(Sourced(element), "and the formula around it is still set, not turned into its characters");

        Settle(element, " ");

        Assert.AreEqual("$$\nx\\alpha \n$$\n", element.Source);
        Assert.IsNull(element.ShownAsWritten, "a space ends the command");
    });

    [TestMethod]
    public void BackspaceAtTheStartOfAFormulaGoesNoFurther() => UiThread.Run(() =>
    {
        const string source = "$$\nx\n$$\n";
        var element = Element(source, caret: 3);

        element.Backspace();

        Assert.AreEqual(source, element.Source, "past the start is the delimiter that says it is a formula");
    });

    // ── A diagram in the document ───────────────────────────────────────────

    private const string Pie = "Pets:\n\n```mermaid\npie showData\n    \"Dogs\" : 30\n```\n";

    [TestMethod]
    public void EnterInADiagramInTheDocumentStartsItsNextLine() => UiThread.Run(() =>
    {
        var element = Element(Pie, caret: Pie.IndexOf("30", System.StringComparison.Ordinal) + 2);

        Settle(element, "\n");

        StringAssert.Contains(element.Source, "\"Dogs\" : 30\n    \"\" : \n```", "the pie's next slice, not a line of the document");
    });

    [TestMethod]
    public void AndAQuoteTypedIntoALabelIsWrittenAsTheDiagramWritesOne() => UiThread.Run(() =>
    {
        var element = Element(Pie, caret: Pie.IndexOf("Dogs", System.StringComparison.Ordinal) + 4);

        element.Type('"');

        StringAssert.Contains(element.Source, "\"Dogs#quot;\" : 30");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static MarkdownElement Element(string source, int caret)
    {
        var element = new MarkdownElement(source, StyleFormat.Dark);

        element.Measure(new Size(480, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 480, element.DesiredSize.Height));
        element.TakeCaret(caret);

        Assert.AreEqual(caret, element.Caret, "precondition: the caret is where the test put it");

        return element;
    }

    private static void Settle(MarkdownElement element, string separator) => ((IEditableBlock)element).Commit(separator);

    private static bool Sourced(MarkdownElement element) =>
        element.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LayoutText.SourceKind);
}
