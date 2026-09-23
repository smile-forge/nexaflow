using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Arrowing between prose and the rendered content embedded in it.
///
/// <para>
/// A flow document treats an embedded element as one indivisible position, so left-arrowing back along
/// a line hops a whole formula as though it were a single character — you cannot get into it without
/// the mouse. The editor crosses the boundary deliberately instead, and crucially at the place the
/// reader was coming from: right into its start, left into its end, and <em>down</em> into whatever sits
/// under the column the caret was already in. Landing anywhere else is a jump nobody asked for, and it
/// is the difference between a formula that reads as part of the text and one that reads as an object
/// dropped into it.
/// </para>
/// <para>
/// In one document these are steps like any other: the formula is pieces of the same laid tree as the
/// words either side of it, so the caret steps into it from a word as it steps from one word to the next,
/// and nothing has to hand it over.
/// </para>
///
/// Shows a real (off-screen) window, because keys come from one.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("markdown-inline-editor")]
public class CaretCrossingTests
{
    /// <summary>Prose, a formula, prose — the Text tab's shape.</summary>
    private const string Document = "before\n\n$$\nx + y + z + w\n$$\n\nafter";

    private const string Formula = "x + y + z + w";

    [TestMethod]
    public void RightArrowOffTheEndOfTheTextEntersTheFormulaAtItsStart()
    {
        RunInDocument(editor =>
        {
            MarkdownEditorHarness.CaretAtEndOf(editor, block: 0);
            MarkdownEditorHarness.RaiseKey(editor, Key.Right);

            Assert.AreEqual(Starts(editor), InFormula(editor).Caret,
                "you stepped onto its first character, which is where the next step would have gone");
        });
    }

    [TestMethod]
    public void LeftArrowBackOutOfTheTextEntersTheFormulaAtItsEnd()
    {
        RunInDocument(editor =>
        {
            MarkdownEditorHarness.CaretAtStartOf(editor, block: 2);
            MarkdownEditorHarness.RaiseKey(editor, Key.Left);

            Assert.AreEqual(Starts(editor) + Formula.Length, InFormula(editor).Caret,
                "coming back along the line puts you after its last character, not before its first");
        });
    }

    [TestMethod]
    public void EitherVerticalArrowEntersTheFormulaAtItsStart()
    {
        // A line step goes to where the line begins, and this whole formula is that line — so unlike
        // left and right, up and down agree with each other. Not the column the caret was in either:
        // landing part-way along would drop the reader into the middle of a subscript they were only
        // passing over.
        RunInDocument(editor =>
        {
            MarkdownEditorHarness.CaretAtEndOf(editor, block: 0);
            MarkdownEditorHarness.RaiseKey(editor, Key.Down);
            Assert.AreEqual(Starts(editor), InFormula(editor).Caret, "down from the line above");
        });

        RunInDocument(editor =>
        {
            MarkdownEditorHarness.CaretAtStartOf(editor, block: 2);
            MarkdownEditorHarness.RaiseKey(editor, Key.Up);
            Assert.AreEqual(Starts(editor), InFormula(editor).Caret, "and up from the line below");
        });
    }

    [TestMethod]
    public void AnArrowThatStaysWithinTheTextLeavesTheFormulaAlone()
    {
        RunInDocument(editor =>
        {
            // Mid-line, so the step is to the next character rather than out of the block. Crossing
            // from here would snatch the caret out of a word every time a formula sat nearby.
            MarkdownEditorHarness.CaretAtStartOf(editor, block: 0);
            MarkdownEditorHarness.RaiseKey(editor, Key.Right);

            Assert.IsFalse(editor.InFormula(), "the caret is still in the text it was in");
        });
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static void RunInDocument(System.Action<MarkdownSurface> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Document, test));

    /// <summary>The formula, having checked the caret is in it.</summary>
    private static DocumentBlock InFormula(MarkdownSurface editor)
    {
        var formula = MarkdownEditorHarness.Block(editor);
        Assert.IsTrue(editor.InFormula(), $"the arrow key put the caret in the formula, but it is at {editor.Shown.Caret}");
        return formula;
    }

    /// <summary>Where the formula's first character is in the document.</summary>
    private static int Starts(MarkdownSurface editor) => Document.IndexOf(Formula, System.StringComparison.Ordinal);
}
