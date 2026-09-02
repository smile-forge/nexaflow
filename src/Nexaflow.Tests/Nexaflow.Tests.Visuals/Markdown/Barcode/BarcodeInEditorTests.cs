using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The barcode driven through the editor's block seam — the same seam the formula uses, with none of the
/// formula in the path.
///
/// <para>
/// This is what the seam was widened for. Everything here goes through <c>IEditableBlock</c>: the caret
/// crosses in from the prose, keys reach the value, and the edit lands back in the fenced block it came
/// from. Nothing in <see cref="InlineMarkdownEditor"/> tests for a barcode to make it work, so a third
/// kind of editable block joins by implementing the interface and nothing else.
/// </para>
/// <para>
/// The write-back is the part worth watching. The parser is handed the fence's <em>content</em> and
/// reports the value's offset into that, while the editor splices into the whole block — fence lines
/// included. Asserting on the resulting markdown rather than on the element is what catches the two
/// disagreeing.
/// </para>
///
/// Shows a real (off-screen) window, because the editor builds its document during a render pass.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // spins an off-screen Window; concurrent WPF layout and focus make it flaky
[CoversNode("markdown-inline-editor")]
public class BarcodeInEditorTests
{
    /// <summary>Prose, a barcode, prose — a fenced block between two paragraphs.</summary>
    private const string Document = "before\n\n```barcode\nformat: code128\nvalue: ABC123\n```\n\nafter";

    private const string Value = "ABC123";

    [TestMethod]
    public void RightArrowOffTheEndOfTheTextEntersTheBarcode() => InDocument((editor, rtb) =>
    {
        CaretAtEndOf(rtb, block: 0);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Right);

        Assert.IsInstanceOfType<BarcodeElement>(editor.FocusedBlock,
            "the arrow crossed into it the same way it crosses into a formula");
    });

    [TestMethod]
    public void TypingReachesTheValueAndTheBlockSource() => InDocument((editor, rtb) =>
    {
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.Type(rtb, "9");

        Assert.AreEqual("9" + Value, Barcode(editor).Value,
            "the caret entered at the start, so the character lands before the first one");

        StringAssert.Contains(editor.Markdown, "value: 9" + Value,
            "and the edit goes back into the fence, at the value and not a couple of lines early");
        StringAssert.Contains(editor.Markdown, "format: code128",
            "without disturbing the settings around it");
    });

    [TestMethod]
    public void SeveralKeystrokesEachSpliceAgainstTheLastOne() => InDocument((editor, rtb) =>
    {
        // The run changes size as it is typed into, so the second keystroke has to splice against the
        // length the first one left behind. Getting that wrong doubles or truncates the value, and one
        // character is never enough to show it.
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.Type(rtb, "123");

        Assert.AreEqual("123" + Value, Barcode(editor).Value);
        StringAssert.Contains(editor.Markdown, "value: 123" + Value);
        Assert.AreEqual(1, Occurrences(editor.Markdown, "value:"), "still one value line, not three");
    });

    [TestMethod]
    public void BackspaceTakesACharacterOutOfTheValue() => InDocument((editor, rtb) =>
    {
        EnterBarcodeAtTheEnd(editor, rtb);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Back);

        Assert.AreEqual(Value[..^1], Barcode(editor).Value);
        StringAssert.Contains(editor.Markdown, "value: " + Value[..^1]);
    });

    [TestMethod]
    public void AValueTheFormatCannotCarryStillEditsAndSaysWhy()
    {
        // An EAN-13 wants thirteen digits. Halfway through typing one it never has them, so the block
        // has to keep rendering and keep taking keys while it is wrong — the whole reason the parser
        // reports where the value is and leaves the encoding to the element.
        const string document = "before\n\n```barcode\nformat: ean13\nvalue: 5901234123457\n```\n\nafter";

        UiThread.Run(() => MarkdownEditorHarness.Run(document, (editor, rtb) =>
        {
            EnterBarcodeAtTheEnd(editor, rtb);
            MarkdownEditorHarness.Type(rtb, "7");

            var barcode = Barcode(editor);
            Assert.AreEqual("59012341234577", barcode.Value, "the key was still taken");
            Assert.IsNull(barcode.Pattern, "but there is nothing to encode it as");
            Assert.AreEqual(1, barcode.Diagnostics.Count, "and it says so, over the value they must change");
            StringAssert.Contains(editor.Markdown, "value: 59012341234577",
                "while the source follows the reader through the invalid state");
        }));
    }

    [TestMethod]
    public void ArrowingOffTheEndHandsTheCaretBackToTheDocument() => InDocument((editor, rtb) =>
    {
        EnterBarcodeAtTheEnd(editor, rtb);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Right);   // off the back of the value

        Assert.IsNull(editor.FocusedBlock,
            "the block gave the caret up, and the text after it has it now");
    });

    [TestMethod]
    public void EscapeLeavesTheBarcode() => InDocument((editor, rtb) =>
    {
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Escape);

        Assert.IsNull(editor.FocusedBlock);
    });

    [TestMethod]
    public void EnterDoesNothingRatherThanSplittingTheBlock() => InDocument((editor, rtb) =>
    {
        // A value is one line. Handing Enter back to the document would tear the fence in half around
        // the caret, which is the one outcome nobody wants from pressing it here.
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Enter);

        Assert.AreEqual(Value, Barcode(editor).Value);
        Assert.AreEqual(1, Occurrences(editor.Markdown, "```barcode"), "the fence is intact");
    });

    // ── Harness ─────────────────────────────────────────────────────────────

    private static void InDocument(Action<InlineMarkdownEditor, RichTextBox> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Document, test));

    /// <summary>Arrows in from the prose above, landing at the start of the value.</summary>
    private static void EnterBarcode(InlineMarkdownEditor editor, RichTextBox rtb)
    {
        CaretAtEndOf(rtb, block: 0);
        MarkdownEditorHarness.RaiseKey(rtb, Key.Right);
        Assert.IsInstanceOfType<BarcodeElement>(editor.FocusedBlock, "the caret is in the barcode");
    }

    /// <summary>Arrows in from the prose below, landing after the last character.</summary>
    private static void EnterBarcodeAtTheEnd(InlineMarkdownEditor editor, RichTextBox rtb)
    {
        CaretAtStartOf(rtb, LastProseBlock(rtb));
        MarkdownEditorHarness.RaiseKey(rtb, Key.Left);
        Assert.IsInstanceOfType<BarcodeElement>(editor.FocusedBlock, "the caret is in the barcode");
    }

    private static BarcodeElement Barcode(InlineMarkdownEditor editor)
    {
        var element = editor.FocusedBlock as BarcodeElement;
        Assert.IsNotNull(element, "the barcode still holds the caret");
        return element;
    }

    /// <summary>The index of the prose block after the fence — whatever the splitter made it.</summary>
    private static int LastProseBlock(RichTextBox rtb) =>
        rtb.Document.Blocks.OfType<Paragraph>()
            .Select(b => b.Tag is int tag ? tag : -1)
            .Where(tag => tag >= 0)
            .Max();

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

    private static int Occurrences(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }
}
