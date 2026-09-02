using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The rendered barcode: that it draws, that its value edits in place with the bars following, and that
/// a value the format cannot carry keeps the block on the page instead of taking it off.
///
/// <para>
/// That last one is the behaviour worth guarding. Editing means passing through invalid states — an
/// EAN-13 is unreadable at every length but thirteen — so a barcode that vanished while being retyped
/// would be unusable. It has to stay, marked.
/// </para>
/// <para>
/// Every body runs wholly inside one <see cref="UiThread.Run"/>: a WPF object belongs to the thread that
/// made it, so an element cannot be built on one and asserted on from another.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("barcode-render")]
[CoversNode("barcode-editing")]
public class BarcodeElementTests
{
    private const string Code128 = "format: CODE128\nvalue: MARKDOWN-128";

    /// <summary>Builds an element. Only ever called from inside a <see cref="UiThread.Run"/> body.</summary>
    private static BarcodeElement Element(string source)
    {
        Assert.IsTrue(BarcodeBlockParser.TryParse(source, out var block, out string? error), error);
        return new BarcodeElement(block!, MarkdownPalette.Dark);
    }

    private static void Arrive(BarcodeElement element, BlockExit edge) =>
        ((IEditableBlock)element).TakeCaretArriving(new CaretArrival(edge, CaretStep.Character, null));

    [TestMethod]
    public void BarcodeIsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("barcode"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("BARCODE"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("barcodes"));
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
        Assert.IsInstanceOfType(DiagramRenderer.Render("barcode", Code128, MarkdownPalette.Dark),
                                typeof(BarcodeElement)));

    [TestMethod]
    public void MeasuresToTheSymbolPlusItsQuietZone() => UiThread.Run(() =>
    {
        var element = Element($"{Code128}\nwidth: 3\nmargin: 12\ndisplayValue: false");
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.IsNotNull(element.Pattern);
        Assert.AreEqual(element.Pattern!.Width * 3 + 12 * 2, element.DesiredSize.Width, 0.5);
    });

    [TestMethod]
    public void TypingChangesTheValueAndTheBarsFollow() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: 1234");
        int before = element.Pattern!.Width;

        int raised = 0;
        element.ValueChanged += (_, _) => raised++;

        Arrive(element, BlockExit.After);
        element.Type('5');
        element.Type('6');

        Assert.AreEqual("123456", element.Value);
        Assert.AreEqual(2, raised, "each keystroke should tell the host to write the value back");
        Assert.AreNotEqual(before, element.Pattern!.Width, "more digits should mean a wider symbol");
    });

    [TestMethod]
    public void BackspaceAndDeleteWorkTheValueDown() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: ABCD");

        Arrive(element, BlockExit.After);
        Assert.IsTrue(element.Backspace());
        Assert.AreEqual("ABC", element.Value);

        Arrive(element, BlockExit.Before);
        Assert.IsTrue(element.Delete());
        Assert.AreEqual("BC", element.Value);
    });

    [TestMethod]
    public void BackspaceAtTheStartHandsTheKeyBack() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: A");
        Arrive(element, BlockExit.Before);

        // Nothing to the left of the caret: the document takes the key and joins the block to what is
        // before it, rather than the barcode swallowing it.
        Assert.IsFalse(element.Backspace());
    });

    [TestMethod]
    public void TypingOverASelectionReplacesIt() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: ABCD");

        ((IEditableBlock)element).SelectRange(1, 2);
        element.Type('X');

        Assert.AreEqual("AXD", element.Value);
    });

    [TestMethod]
    public void TheCaretLeavingAnEndTellsTheHost() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: AB");
        var exits = new List<BlockExit>();
        ((IEditableBlock)element).Exited += (_, side) => exits.Add(side);

        Arrive(element, BlockExit.Before);
        element.MoveCaret(forward: false);

        Arrive(element, BlockExit.After);
        element.MoveCaret(forward: true);

        CollectionAssert.AreEqual(new[] { BlockExit.Before, BlockExit.After }, exits);
    });

    // ── The value that will not encode ─────────────────────────────────────

    [TestMethod]
    public void AnUnencodableValueStaysOnThePage_Marked() => UiThread.Run(() =>
    {
        // Three digits into an EAN-13 — exactly where a reader is a moment after starting to type one.
        var element = Element("format: EAN13\nvalue: 590");

        Assert.IsNull(element.Pattern, "there is no valid symbol for this value");

        var diagnostics = ((IEditableBlock)element).Diagnostics;
        Assert.AreEqual(1, diagnostics.Count, "the value should be marked");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        StringAssert.Contains(diagnostics[0].Message, "12 digits");

        // And the reason is a hover away, which is the only place it can go on a picture.
        Assert.IsNotNull(element.ToolTip);
        StringAssert.Contains(element.ToolTip!.ToString()!, "12 digits");

        // It still takes barcode-shaped room, so the page does not jump as the value is corrected.
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.IsTrue(element.DesiredSize.Width > 50, "an invalid barcode should still be barcode-sized");
    });

    [TestMethod]
    public void TypingBackToAValidValueClearsTheMark() => UiThread.Run(() =>
    {
        var element = Element("format: EAN13\nvalue: 590123412345");
        Assert.AreEqual(0, ((IEditableBlock)element).Diagnostics.Count, "this value is fine");

        Arrive(element, BlockExit.After);
        element.Backspace();
        Assert.AreEqual(1, ((IEditableBlock)element).Diagnostics.Count, "eleven digits is not an EAN-13");

        element.Type('5');
        Assert.AreEqual(0, ((IEditableBlock)element).Diagnostics.Count, "and back again");
        Assert.IsNull(element.ToolTip);
    });

    [TestMethod]
    public void AnEmptyValueIsMarkedRatherThanBlank() => UiThread.Run(() =>
    {
        var element = Element("format: CODE128\nvalue: A");
        Arrive(element, BlockExit.After);
        element.Backspace();

        Assert.AreEqual(string.Empty, element.Value);
        Assert.AreEqual(1, ((IEditableBlock)element).Diagnostics.Count);
        StringAssert.Contains(element.ToolTip!.ToString()!, "needs a value");
    });

    [TestMethod]
    public void TheLabelShowsWhatWasEncoded_CheckDigitAndAll() => UiThread.Run(() =>
    {
        // A real EAN-13 label carries the check digit, so leaving it off the value must not leave it off
        // the print.
        var element = Element("format: EAN13\nvalue: 590123412345");
        Assert.AreEqual("5901234123457", element.Pattern!.Text);
    });

    // ── A block that is not a block ────────────────────────────────────────

    [TestMethod]
    public void AStructuralFaultFallsBackToTheSource() => UiThread.Run(() =>
    {
        const string source = "format: NOTAFORMAT\nvalue: X";
        var element = DiagramRenderer.Render("barcode", source, MarkdownPalette.Dark);

        Assert.IsNotInstanceOfType(element, typeof(BarcodeElement));

        string text = AllText(element);
        StringAssert.Contains(text, "NOTAFORMAT");
        StringAssert.Contains(text, source, "the source should be shown when nothing else can be");
    });

    private static string AllText(DependencyObject root)
    {
        var text = new System.Text.StringBuilder();

        void Walk(DependencyObject node)
        {
            if (node is TextBlock tb) text.Append(tb.Text).Append('\n');
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i));

            if (node is Border { Child: { } child }) Walk(child);
            if (node is Panel panel) foreach (UIElement e in panel.Children) Walk(e);
        }

        Walk(root);
        return text.ToString();
    }
}
