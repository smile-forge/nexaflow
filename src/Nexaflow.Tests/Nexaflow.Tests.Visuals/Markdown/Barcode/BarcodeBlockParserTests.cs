using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The block body: which settings are read, and — the distinction this parser exists to draw — which
/// faults stop it being a block at all.
///
/// <para>
/// A value the format cannot carry is deliberately <em>not</em> one of them. It is the part the reader
/// edits in place, so it is invalid every time they are halfway through changing it, and a parser that
/// refused would take the barcode off the page mid-keystroke.
/// </para>
/// </summary>
[TestClass]
[CoversNode("barcode-block-syntax")]
public class BarcodeBlockParserTests
{
    private static BarcodeBlock Parse(string source)
    {
        Assert.IsTrue(BarcodeBlockParser.TryParse(source, out var block, out string? error), error);
        return block!;
    }

    private static string Rejects(string source)
    {
        Assert.IsFalse(BarcodeBlockParser.TryParse(source, out _, out string? error), "expected a rejection");
        Assert.IsFalse(string.IsNullOrWhiteSpace(error), "a rejection should say why");
        return error!;
    }

    [TestMethod]
    public void ReadsTheDocumentedExample()
    {
        var block = Parse(
            """
            format: CODE39
            value: MARKDOWN-39
            width: 2
            height: 80
            displayValue: true
            lineColor: #1d4ed8
            background: #ffffff
            margin: 10
            """);

        Assert.AreEqual(BarcodeSymbology.Code39, block.Format);
        Assert.AreEqual("MARKDOWN-39", block.Value);
        Assert.AreEqual(2, block.BarWidth);
        Assert.AreEqual(80, block.BarHeight);
        Assert.IsTrue(block.DisplayValue);
        Assert.AreEqual(new HexColor(0xFF, 0x1D, 0x4E, 0xD8), block.LineColor);
        Assert.AreEqual(new HexColor(0xFF, 0xFF, 0xFF, 0xFF), block.Background);
        Assert.AreEqual(10, block.Margin);
    }

    [TestMethod]
    public void SettingsDefaultToTheOnesTheOptionNamesComeFrom()
    {
        var block = Parse("format: CODE128\nvalue: MARKDOWN-128");

        Assert.AreEqual(BarcodeBlock.DefaultBarWidth,  block.BarWidth);
        Assert.AreEqual(BarcodeBlock.DefaultBarHeight, block.BarHeight);
        Assert.AreEqual(BarcodeBlock.DefaultFontSize,  block.FontSize);
        Assert.AreEqual(BarcodeBlock.DefaultMargin,    block.Margin);
        Assert.IsTrue(block.DisplayValue);
        Assert.AreEqual(BarcodeTextAlign.Center, block.TextAlign);
        Assert.IsNull(block.LineColor);
        Assert.IsNull(block.Background);
    }

    [TestMethod]
    public void RecordsWhereTheValueSits_SoAnEditGoesBackWhereItCameFrom()
    {
        const string source = "format: CODE128\nvalue: MARKDOWN-128\nheight: 60";
        var block = Parse(source);

        Assert.AreEqual("MARKDOWN-128", source.Substring(block.ValueStart, block.Value.Length),
            "ValueStart should point at the value in the block source");
    }

    [TestMethod]
    public void AnUnencodableValueIsNotAParseFailure()
    {
        // Halfway through typing an EAN-13 the value is three digits, and the block is still a block.
        var block = Parse("format: EAN13\nvalue: 590");

        Assert.AreEqual("590", block.Value);
        Assert.IsFalse(BarcodeEncoder.TryEncode(block.Format, block.Value, out _, out _),
            "this value should indeed not encode — the point is that the parser did not mind");
    }

    [TestMethod]
    public void TextAlignAndDisplayValueAreRead()
    {
        Assert.AreEqual(BarcodeTextAlign.Left,  Parse("format: CODE128\nvalue: X\ntextAlign: left").TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Right, Parse("format: CODE128\nvalue: X\ntextAlign: RIGHT").TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Center, Parse("format: CODE128\nvalue: X\ntextAlign: centre").TextAlign);
        Assert.IsFalse(Parse("format: CODE128\nvalue: X\ndisplayValue: false").DisplayValue);
        Assert.IsFalse(Parse("format: CODE128\nvalue: X\ndisplayValue: no").DisplayValue);
    }

    [TestMethod]
    public void ValueKeepsEverythingAfterTheFirstColon() =>
        Assert.AreEqual("A:B:C", Parse("format: CODE128\nvalue: A:B:C").Value);

    // ── What stops it being a block ────────────────────────────────────────

    [TestMethod]
    public void StructuralFaultsAreRefused()
    {
        StringAssert.Contains(Rejects("value: X"),                              "format");
        StringAssert.Contains(Rejects("format: CODE128"),                       "value");
        StringAssert.Contains(Rejects("format: QRCODE\nvalue: X"),              "QRCODE");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\nheight: tall"), "not a number");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\nwidth: 0"),   "range");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\ncolour: red"), "colour");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\nlineColor: reddish"), "hex colour");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\ntextAlign: middle"), "left, center or right");
        StringAssert.Contains(Rejects("format: CODE128\nvalue: X\ndisplayValue: maybe"), "true or false");
        StringAssert.Contains(Rejects("format: CODE128\njust some prose"),      "just some prose");
        StringAssert.Contains(Rejects("   \n\n"),                               "empty");
    }
}
