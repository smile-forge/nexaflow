using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// A block's tree read into what it says: which settings are read, and — for what stops it being read — which piece of the tree
/// is at fault.
///
/// <para>
/// A value the format cannot carry is deliberately <em>not</em> one of them. It is the part the reader edits in place, so it is
/// invalid every time they are halfway through changing it, and a reader that refused would take the barcode off the page
/// mid-keystroke.
/// </para>
/// </summary>
[TestClass]
[CoversNode("barcode-block-syntax")]
public class BarcodeBlockReaderTests
{
    private static BarcodeBlock Read(string source)
    {
        Assert.IsTrue(BarcodeBlockReader.TryRead(ContentPart.Of(BarcodeParser.Parse(source)), out var block, out var wrong), wrong.Reason);
        return block!;
    }

    private static (ContentPart Part, string Reason) Refused(string source)
    {
        Assert.IsFalse(BarcodeBlockReader.TryRead(ContentPart.Of(BarcodeParser.Parse(source)), out _, out var wrong), "expected a refusal");
        Assert.IsFalse(string.IsNullOrWhiteSpace(wrong.Reason), "a refusal should say why");
        return wrong;
    }

    [TestMethod]
    public void ReadsTheDocumentedExample()
    {
        var block = Read(
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
        var block = Read("format: CODE128\nvalue: MARKDOWN-128");

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
    public void TheValueIsTheTreesOwnCharacters_SoAnEditGoesBackWhereItCameFrom()
    {
        const string source = "format: CODE128\nvalue: MARKDOWN-128\nheight: 60";
        var block = Read(source);

        Assert.AreEqual("MARKDOWN-128".Length, block.Characters.Count, "one piece per character");
        Assert.AreEqual("MARKDOWN-128", source.Substring(block.Written!.Start, block.Written.Length),
                        "and the value stands where it is written in the block");
    }

    [TestMethod]
    public void AnUnencodableValueIsNotARefusal()
    {
        // Halfway through typing an EAN-13 the value is three digits, and the block is still a block.
        var block = Read("format: EAN13\nvalue: 590");

        Assert.AreEqual("590", block.Value);
        Assert.IsFalse(BarcodeEncoder.TryEncode(block.Format, block.Value, out _, out _),
            "this value should indeed not encode — the point is that the reader did not mind");
    }

    [TestMethod]
    public void TextAlignAndDisplayValueAreRead()
    {
        Assert.AreEqual(BarcodeTextAlign.Left,  Read("format: CODE128\nvalue: X\ntextAlign: left").TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Right, Read("format: CODE128\nvalue: X\ntextAlign: RIGHT").TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Center, Read("format: CODE128\nvalue: X\ntextAlign: centre").TextAlign);
        Assert.IsFalse(Read("format: CODE128\nvalue: X\ndisplayValue: false").DisplayValue);
        Assert.IsFalse(Read("format: CODE128\nvalue: X\ndisplay-value: no").DisplayValue, "a key with hyphens is the same key");
    }

    [TestMethod]
    public void ValueKeepsEverythingAfterTheFirstColon() =>
        Assert.AreEqual("A:B:C", Read("format: CODE128\nvalue: A:B:C").Value);

    // ── What stops it being read, and where ────────────────────────────────

    [TestMethod]
    public void WhatStopsItBeingReadIsSaidAgainstThePieceAtFault()
    {
        foreach (var (source, said, at) in new[]
                 {
                     ("value: X", "format", "value: X"),
                     ("format: CODE128", "value", "format: CODE128"),
                     ("format: QRCODE\nvalue: X", "QRCODE", "QRCODE"),
                     ("format: CODE128\nvalue: X\nheight: tall", "not a number", "tall"),
                     ("format: CODE128\nvalue: X\nwidth: 0", "range", "0"),
                     ("format: CODE128\nvalue: X\ncolour: red", "colour", "colour"),
                     ("format: CODE128\nvalue: X\nlineColor: reddish", "hex colour", "reddish"),
                     ("format: CODE128\nvalue: X\ntextAlign: middle", "left, center or right", "middle"),
                     ("format: CODE128\nvalue: X\ndisplayValue: maybe", "true or false", "maybe"),
                     ("format: CODE128\njust some prose", "just some prose", "just some prose"),
                     ("   \n\n", "empty", "   \n\n"),
                 })
        {
            var (part, reason) = Refused(source);

            StringAssert.Contains(reason, said, source);
            Assert.AreEqual(at, source.Substring(part.Start, part.Length), $"{source}: said against what is wrong");
        }
    }
}
