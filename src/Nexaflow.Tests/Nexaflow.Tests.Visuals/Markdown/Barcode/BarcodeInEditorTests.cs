using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Editing;

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

    [TestMethod]
    public void SpaceIsTypedIntoTheValueRatherThanEscapingToTheProse()
    {
        // Space used to be handed back to the document on the theory that it would come round again as
        // text input. It did not: the editor took it first, put it in the next paragraph it could find,
        // and took the caret there with it — so typing a space anywhere in a value jumped you out of it.
        const string document = "before\n\n```barcode\nformat: CODE128\nvalue: AB\n```\n\nafter";

        UiThread.Run(() => MarkdownEditorHarness.Run(document, (editor, rtb) =>
        {
            EnterBarcodeAtTheEnd(editor, rtb);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Space);

            Assert.IsInstanceOfType<BarcodeElement>(editor.FocusedBlock, "the caret stayed in the barcode");
            Assert.AreEqual("AB ", Barcode(editor).Value);
            StringAssert.Contains(editor.Markdown, "value: AB ");
            StringAssert.Contains(editor.Markdown, "\nafter", "and the prose below is untouched");
        }));
    }

    [TestMethod]
    public void HomeAndEndGoToTheEndsOfTheValue() => InDocument((editor, rtb) =>
    {
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.RaiseKey(rtb, Key.End);
        MarkdownEditorHarness.Type(rtb, "9");

        Assert.AreEqual(Value + "9", Barcode(editor).Value, "End went to the end of the value, not of the line");

        MarkdownEditorHarness.RaiseKey(rtb, Key.Home);
        MarkdownEditorHarness.Type(rtb, "0");

        Assert.AreEqual("0" + Value + "9", Barcode(editor).Value);
    });

    [TestMethod]
    public void UndoRestoresAnEditMadeInsideTheBlock() => InDocument((editor, rtb) =>
    {
        // Typing into a block never recorded an undo step — the two older editing paths both snapshot and
        // this third one was added beside them without it, so an edit made in a rendered block was the one
        // kind of edit undo could not see.
        EnterBarcode(editor, rtb);
        MarkdownEditorHarness.Type(rtb, "42");
        StringAssert.Contains(editor.Markdown, "value: 42" + Value);

        editor.Undo();

        StringAssert.Contains(editor.Markdown, "value: " + Value, "back to what it stood at before the typing");
        Assert.IsFalse(editor.Markdown.Contains("value: 42"), "and the edit is gone rather than half-undone");
    });

    [TestMethod]
    public void CutTakesWhatTheBlockHasSelected() => InDocument((editor, rtb) =>
    {
        // Cut wanted a source-mode session, which a value edited in place never has — so the command sat
        // disabled over a selection the reader could see perfectly well, and the shortcut did nothing.
        EnterBarcode(editor, rtb);
        ((IEditableBlock)Barcode(editor)).SelectRange(0, 3);

        ApplicationCommands.Cut.Execute(null, rtb);

        Assert.AreEqual(Value[3..], Barcode(editor).Value, "the selected characters went");
        StringAssert.Contains(editor.Markdown, "value: " + Value[3..], "and the source followed");
    });

    [TestMethod]
    public void EditingAPublicationActsOnTheValueAndNotOnWhatIsPrinted()
    {
        // An ISBN's value carries hyphens the symbol never prints, and the symbol carries a check digit
        // the value never typed — so the two strings do not line up character for character. The caret
        // was placed against the printed number, so a backspace deleted whatever happened to be at that
        // offset in the value, which was nothing to do with where the reader could see the caret.
        const string document = "before\n\n```barcode\nformat: ISBN\nvalue: 978-1-56581-231-4\n```\n\nafter";

        UiThread.Run(() => MarkdownEditorHarness.Run(document, (editor, rtb) =>
        {
            EnterBarcodeAtTheEnd(editor, rtb);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Back);

            Assert.AreEqual("978-1-56581-231-", Barcode(editor).Value, "the last character of the value went");
            StringAssert.Contains(editor.Markdown, "value: 978-1-56581-231-");
        }));
    }

    [TestMethod]
    public void EveryCharacterOfTheValueIsReachableByArrowingThroughIt()
    {
        // UPC-E completes what it is given — a number system digit on the front, a check digit on the
        // end — so what it prints is longer than what was typed. A caret walking the printed number ran
        // past the ends of the value, which is what made deleting look like it was inventing characters.
        const string value = "01234565";
        const string document = "before\n\n```barcode\nformat: UPCE\nvalue: " + value + "\n```\n\nafter";

        UiThread.Run(() => MarkdownEditorHarness.Run(document, (editor, rtb) =>
        {
            EnterBarcodeAtTheEnd(editor, rtb);

            for (int i = 0; i < value.Length; i++)
            {
                MarkdownEditorHarness.RaiseKey(rtb, Key.Left);
                Assert.IsNotNull(editor.FocusedBlock, $"still inside the value after {i + 1} steps");
            }

            MarkdownEditorHarness.RaiseKey(rtb, Key.Left);
            Assert.IsNull(editor.FocusedBlock, "and the step off the front hands the caret back");
        }));
    }

    [TestMethod]
    public void DeletingFromAUpcEShortensTheValueByExactlyOne()
    {
        const string document = "before\n\n```barcode\nformat: UPCE\nvalue: 01234565\n```\n\nafter";

        UiThread.Run(() => MarkdownEditorHarness.Run(document, (editor, rtb) =>
        {
            EnterBarcodeAtTheEnd(editor, rtb);
            MarkdownEditorHarness.RaiseKey(rtb, Key.Back);

            Assert.AreEqual("0123456", Barcode(editor).Value);
            StringAssert.Contains(editor.Markdown, "value: 0123456\n");
        }));
    }

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
