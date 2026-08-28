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
/// Deliberately driven through <c>IEditableBlock</c> rather than through the formula, because the score
/// and the diagrams are next: a block that takes a caret joins this by implementing that interface, with
/// nothing in the editor to change.
/// </para>
///
/// Shows a real (off-screen) window, because the editor builds its document during a render pass.
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
        RunInDocument((editor, rtb) =>
        {
            CaretAtEndOf(rtb, block: 0);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Right);

            var formula = FocusedFormula(editor);
            Assert.AreEqual(0, formula.Caret,
                "you stepped onto its first character, which is where the next step would have gone");
        });
    }

    [TestMethod]
    public void LeftArrowBackOutOfTheTextEntersTheFormulaAtItsEnd()
    {
        RunInDocument((editor, rtb) =>
        {
            CaretAtStartOf(rtb, block: 2);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Left);

            var formula = FocusedFormula(editor);
            Assert.AreEqual(Formula.Length, formula.Caret,
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
        RunInDocument((editor, rtb) =>
        {
            CaretAtEndOf(rtb, block: 0);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Down);
            Assert.AreEqual(0, FocusedFormula(editor).Caret, "down from the line above");
        });

        RunInDocument((editor, rtb) =>
        {
            CaretAtStartOf(rtb, block: 2);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Up);
            Assert.AreEqual(0, FocusedFormula(editor).Caret, "and up from the line below");
        });
    }

    [TestMethod]
    public void AnArrowThatStaysWithinTheTextLeavesTheFormulaAlone()
    {
        RunInDocument((editor, rtb) =>
        {
            // Mid-line, so the step is to the next character rather than out of the block. Crossing
            // from here would snatch the caret out of a word every time a formula sat nearby.
            CaretAtStartOf(rtb, block: 0);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Right);

            Assert.IsNull(editor.FocusedFormula, "the caret is still in the text it was in");
        });
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static void RunInDocument(System.Action<InlineMarkdownEditor, RichTextBox> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Document, test));

    private static FormulaElement FocusedFormula(InlineMarkdownEditor editor)
    {
        var formula = editor.FocusedFormula;
        Assert.IsNotNull(formula, "the arrow key handed the caret to the formula");
        return formula;
    }

    private static void CaretAtEndOf(RichTextBox rtb, int block) => PlaceCaret(rtb, block, atEnd: true);

    private static void CaretAtStartOf(RichTextBox rtb, int block) => PlaceCaret(rtb, block, atEnd: false);

    private static void PlaceCaret(RichTextBox rtb, int block, bool atEnd)
    {
        var para = rtb.Document.Blocks
            .OfType<Paragraph>()
            .FirstOrDefault(b => b.Tag is int tag && tag == block);
        Assert.IsNotNull(para, $"block {block} rendered as prose");

        rtb.CaretPosition = atEnd
            ? para.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
            : para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
    }
}
