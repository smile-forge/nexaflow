using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Settings;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Barcode;

/// <summary>
/// A block read into what it says (<see cref="EncodeBarcode"/>): which settings are read, which line is the value, and — for what
/// stops it being read — which piece of the tree is at fault.
///
/// <para>
/// A value the format cannot carry is deliberately <em>not</em> one of them where the block is written in. It is the part the
/// reader edits in place, so it is invalid every time they are halfway through changing it, and refusing the block would take
/// the barcode off the page mid-keystroke.
/// </para>
/// </summary>
[TestClass]
[CoversNode("barcode-block-syntax")]
public class BarcodeReadingTests
{
    private static ContentNode Encoded(string source, bool writing = false) =>
        new EncodeBarcode(writing).Run(BarcodeParser.Parse(source));

    private static BarcodeBlockNode Read(string source, bool writing = false)
    {
        var tree = Encoded(source, writing);
        Assert.IsInstanceOfType(tree, typeof(BarcodeBlockNode), string.Join("; ", tree.SelfAndDescendants().Select(node => node.Trouble).OfType<string>()));
        return (BarcodeBlockNode)tree;
    }

    /// <summary>The value's characters as written, in the line the bars encode.</summary>
    private static string Value(ContentNode block) =>
        string.Concat(block.SelfAndDescendants().Single(node => node.Role == BarcodeRoles.Encoded)
                           .SelfAndDescendants().Where(node => node.Kind == BarcodeKinds.Character).Select(node => node.Text));

    private static (ContentPart Part, string Reason) Refused(string source)
    {
        var tree = Encoded(source);
        Assert.IsNotInstanceOfType(tree, typeof(BarcodeBlockNode), $"{source}: expected a refusal");

        var part = ContentPart.Of(tree).SelfAndDescendants().Single(part => part.Trouble is not null && !part.Derived);
        Assert.IsFalse(string.IsNullOrWhiteSpace(part.Trouble), "a refusal should say why");
        return (part, part.Trouble!);
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

        Assert.AreEqual(BarcodeSymbology.Code39, block.Settings.Format);
        Assert.AreEqual("MARKDOWN-39", Value(block));
        Assert.AreEqual(2, block.Settings.BarWidth);
        Assert.AreEqual(80, block.Settings.BarHeight);
        Assert.IsTrue(block.Settings.DisplayValue);
        Assert.AreEqual(new HexColor(0xFF, 0x1D, 0x4E, 0xD8), block.Settings.LineColor);
        Assert.AreEqual(new HexColor(0xFF, 0xFF, 0xFF, 0xFF), block.Settings.Background);
        Assert.AreEqual(10, block.Settings.Margin);
    }

    [TestMethod]
    public void SettingsDefaultToTheOnesTheOptionNamesComeFrom()
    {
        var settings = Read("format: CODE128\nvalue: MARKDOWN-128").Settings;

        Assert.AreEqual(BarcodeBlock.DefaultBarWidth,  settings.BarWidth);
        Assert.AreEqual(BarcodeBlock.DefaultBarHeight, settings.BarHeight);
        Assert.AreEqual(BarcodeBlock.DefaultFontSize,  settings.FontSize);
        Assert.AreEqual(BarcodeBlock.DefaultMargin,    settings.Margin);
        Assert.IsTrue(settings.DisplayValue);
        Assert.AreEqual(BarcodeTextAlign.Center, settings.TextAlign);
        Assert.IsNull(settings.LineColor);
        Assert.IsNull(settings.Background);
    }

    [TestMethod]
    public void TheValueIsTheTreesOwnCharacters_SoAnEditGoesBackWhereItCameFrom()
    {
        const string source = "format: CODE128\nvalue: MARKDOWN-128\nheight: 60";
        var line = ContentPart.Of(Read(source)).SelfAndDescendants().Single(part => part.Role == BarcodeRoles.Encoded);
        var written = line.Part(MatrixRoles.Value)!;

        Assert.AreEqual("MARKDOWN-128".Length, written.Children.Count(part => part.Kind == BarcodeKinds.Character), "one piece per character");
        Assert.AreEqual("MARKDOWN-128", source.Substring(written.Start, written.Length), "and the value stands where it is written in the block");
    }

    [TestMethod]
    public void TheLastValueWrittenIsTheOneEncoded() =>
        Assert.AreEqual("SECOND", Value(Read("format: CODE128\nvalue: FIRST\nvalue: SECOND")));

    [TestMethod]
    public void AnUnencodableValueIsNotARefusalWhereItIsWrittenIn()
    {
        // Halfway through typing an EAN-13 the value is three digits, and the block is still a block — with why it will not
        // encode said under the value rather than instead of the barcode.
        var block = Read("format: EAN13\nvalue: 590", writing: true);

        Assert.AreEqual("590", Value(block));
        Assert.IsNotNull(block.Refusal, "and what is wrong with it is said");

        // Only read, the value is what has to be put right, and its source is the only place to do it.
        var (part, reason) = Refused("format: EAN13\nvalue: 590");
        Assert.AreEqual(reason, block.Refusal);
        Assert.AreEqual("590", "format: EAN13\nvalue: 590".Substring(part.Start, part.Length));
    }

    [TestMethod]
    public void TextAlignAndDisplayValueAreRead()
    {
        Assert.AreEqual(BarcodeTextAlign.Left,  Read("format: CODE128\nvalue: X\ntextAlign: left").Settings.TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Right, Read("format: CODE128\nvalue: X\ntextAlign: RIGHT").Settings.TextAlign);
        Assert.AreEqual(BarcodeTextAlign.Center, Read("format: CODE128\nvalue: X\ntextAlign: centre").Settings.TextAlign);
        Assert.IsFalse(Read("format: CODE128\nvalue: X\ndisplayValue: false").Settings.DisplayValue);
        Assert.IsFalse(Read("format: CODE128\nvalue: X\ndisplay-value: no").Settings.DisplayValue, "a key with hyphens is the same key");
    }

    [TestMethod]
    public void ValueKeepsEverythingAfterTheFirstColon() =>
        Assert.AreEqual("A:B:C", Value(Read("format: CODE128\nvalue: A:B:C")));

    [TestMethod]
    public void TheStagePrintsAsWhatWasWritten()
    {
        foreach (var (source, writing) in new[]
                 {
                     ("format: EAN13\nvalue: 590123412345\n", false), ("format: ISBN\nvalue: 978-1-56581-231-4 90000", false),
                     ("format: EAN13\nvalue: 590", true), ("format: CODE39\nvalue:", true), ("format: CODE39\nnonsense", false),
                 })
            Assert.AreEqual(source, Encoded(source, writing).Print(), source);
    }

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
