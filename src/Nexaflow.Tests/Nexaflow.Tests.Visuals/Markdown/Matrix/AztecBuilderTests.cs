using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The <c>aztec</c> block and its layout: the block's fields, dispatch through the one fenced-block router, the
/// symbol laid from the middle out, and the picture read back off the pixels.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("aztec-render")]
[CoversNode("aztec-block-syntax")]
public class AztecBuilderTests
{
    [TestMethod]
    public void AztecIsADiagramLanguage()
    {
        Assert.IsTrue(ContentLanguages.Reads("aztec"));
        Assert.IsTrue(ContentLanguages.Reads("Aztec"));
        Assert.IsTrue(ContentLanguages.Reads("aztec-code"));
        Assert.IsFalse(ContentLanguages.Reads("azteca"));
    }

    [TestMethod]
    public void AnAztecFenceIsDrawnByItsLanguageWithNowhereInItToWrite() => UiThread.Run(() =>
    {
        var element = Alone.Drawn("aztec", "type: text\ntext: hello", StyleFormat.Dark);

        element.Measure(new Size(600, double.PositiveInfinity));
        Assert.IsFalse(element.AcceptsCaret, "a symbol has nowhere in it to write");
    });

    [TestMethod]
    public void TakesTheQrTypeVocabulary()
    {
        var block = Read("type: wifi\nssid: Net\npassword: pw\nsecurity: WPA");
        Assert.AreEqual("WIFI:T:WPA;S:Net;P:pw;;", block.Payload);
    }

    [TestMethod]
    public void ReadsItsOwnShapeSettings()
    {
        var block = Read("type: text\ntext: x\nformat: full\nlayers: 3\necc: 40\neci: 26\ncellSize: 3\nmargin: 2");

        Assert.AreEqual(AztecFormat.Full, block.Options.Format);
        Assert.AreEqual(3, block.Options.Layers);
        Assert.AreEqual(40, block.Options.ErrorCorrectionPercent);
        Assert.AreEqual(26, block.Options.Eci);
        Assert.AreEqual(3, block.Settings.CellSize);
        Assert.AreEqual(2, block.Settings.Margin);
    }

    [TestMethod]
    public void DefaultsToAutoAndTheAdvisedErrorCorrection()
    {
        var block = Read("type: text\ntext: x");

        Assert.AreEqual(AztecFormat.Auto, block.Options.Format);
        Assert.IsNull(block.Options.Layers);
        Assert.AreEqual(AztecOptions.DefaultErrorCorrectionPercent, block.Options.ErrorCorrectionPercent);
        Assert.IsNull(block.Options.Eci);
    }

    /// <summary>
    /// The brackets come off and a separator closes each variable-length element that is not last. AI 01 is a fixed
    /// fourteen characters and so needs none — which is the half of the rule that is easy to get wrong in the
    /// direction nobody notices.
    /// </summary>
    [TestMethod]
    public void AGs1BlockWritesTheWireFormAndFlagsIt()
    {
        var block = Read("type: gs1\ndata: (01)04150123456782(10)LOT7(21)SN9");

        Assert.IsTrue(block.Options.Gs1);
        Assert.AreEqual("0104150123456782" + "10LOT7" + Gs1ElementString.Separator + "21SN9", block.Payload);
    }

    [TestMethod]
    public void RefusesSettingsOutsideTheStandard()
    {
        AssertRefused("type: text\ntext: x\nformat: tiny",               "not an Aztec format");
        AssertRefused("type: text\ntext: x\nlayers: 40",                 "1–32");
        AssertRefused("type: text\ntext: x\nformat: compact\nlayers: 5", "format: full");
        AssertRefused("type: text\ntext: x\necc: 99",                    "0–95");
        AssertRefused("type: text\ntext: x\nlayers: two",                "not a whole number");
        AssertRefused("type: text\ntext: x\nrows: 3",                    "not a field");
        AssertRefused("type: nonsense\ntext: x",                         "Unknown Aztec type");
        AssertRefused("text: x",                                         "no `type:` line");
        AssertRefused("just some prose",                                 "not a `key: value` line");
    }

    [TestMethod]
    public void ACompactSymbolIsItsBullseye_ItsModeMessage_AndItsData() => UiThread.Run(() =>
    {
        var laid = Build("type: text\ntext: hello\nformat: compact\ncellSize: 1\nmargin: 0");

        // Nine modules across, sitting in the middle of the symbol; and no reference grid, which only a full-range
        // symbol carries.
        var finder = MatrixLayouts.Of(laid, AztecBuilder.Finder).Single();
        Assert.AreEqual(9, finder.Bounds.Width);
        Assert.AreEqual(laid.Size.Width / 2, finder.Bounds.X + finder.Bounds.Width / 2);

        Assert.AreEqual(1, MatrixLayouts.Of(laid, AztecBuilder.ModeMessage).Length);
        Assert.AreEqual(0, MatrixLayouts.Of(laid, AztecBuilder.ReferenceGrid).Length);
    });

    [TestMethod]
    public void AFullRangeSymbolCarriesItsReferenceGrid() => UiThread.Run(() =>
    {
        var laid = Build("type: text\ntext: hello\nformat: full\ncellSize: 1\nmargin: 0");

        Assert.AreEqual(13, MatrixLayouts.Of(laid, AztecBuilder.Finder).Single().Bounds.Width);
        Assert.AreEqual(1, MatrixLayouts.Of(laid, AztecBuilder.ReferenceGrid).Length);
    });

    /// <summary>The picture reads back, off the pixels, as the symbol the encoder built.</summary>
    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        const string source = "type: text\ntext: An Aztec Code\ncellSize: 4\nmargin: 2";

        var block = Read(source);
        Assert.IsTrue(AztecEncoder.TryEncode(block.Payload, block.Options, out var symbol, out string? error), error);

        var drawn = MatrixLayouts.ReadBack(Alone.Drawn("aztec", source, StyleFormat.Light),
                                           symbol!.Size, symbol.Size, block.Settings);

        for (int y = 0; y < symbol.Size; y++)
            for (int x = 0; x < symbol.Size; x++)
                Assert.AreEqual(symbol[x, y], drawn[x, y], $"module {x},{y}");

        Assert.AreEqual("An Aztec Code", AztecTestDecoder.Decode(drawn).Text);
    });

    [TestMethod]
    public void ABadBlock_IsShownAsWritten_WithTheReason() => UiThread.Run(() =>
    {
        var laid = Build("type: text");

        StringAssert.Contains(laid.Trouble.Single().Message, "text:");
        Assert.IsTrue(laid.ShowsSource, "a code is only read, so what will not read is put right in its source");
        Assert.AreEqual(1, MatrixLayouts.Of(laid, SourceShown.Reason).Length, "with why, under it");
    });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Laid Build(string source) => AztecBuilder.Lay(source, StyleFormat.Light);

    private static AztecBlock Read(string source)
    {
        Assert.IsTrue(AztecBlockReader.TryRead(MatrixParser.Parse(source), out var block, out string? error), error);
        return block!;
    }

    private static void AssertRefused(string source, string expected)
    {
        Assert.IsFalse(AztecBlockReader.TryRead(MatrixParser.Parse(source), out _, out string? error), $"'{source}' was accepted");
        StringAssert.Contains(error!, expected, $"'{source}' said: {error}");
    }
}
