using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The <c>aztec</c> block and its drawing: the block body, dispatch through the one fenced-block
/// router, and the picture read back off the drawn geometry.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("aztec-render")]
[CoversNode("aztec-block-syntax")]
public class AztecRendererTests
{
    [TestMethod]
    public void AztecIsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("aztec"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("Aztec"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("aztec-code"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("azteca"));
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("aztec", "type: text\ntext: hello", MarkdownPalette.Dark);

        Assert.IsInstanceOfType(element, typeof(Border));
        Assert.IsInstanceOfType(((Border)element).Child, typeof(System.Windows.Shapes.Path));
    });

    [TestMethod]
    public void TakesTheQrTypeVocabulary()
    {
        var block = Parse("type: wifi\nssid: Net\npassword: pw\nsecurity: WPA");
        Assert.AreEqual("WIFI:T:WPA;S:Net;P:pw;;", block.Payload);
    }

    [TestMethod]
    public void ReadsItsOwnShapeSettings()
    {
        var block = Parse("type: text\ntext: x\nformat: full\nlayers: 3\necc: 40\neci: 26\ncellSize: 3\nmargin: 2");

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
        var block = Parse("type: text\ntext: x");

        Assert.AreEqual(AztecFormat.Auto, block.Options.Format);
        Assert.IsNull(block.Options.Layers);
        Assert.AreEqual(AztecOptions.DefaultErrorCorrectionPercent, block.Options.ErrorCorrectionPercent);
        Assert.IsNull(block.Options.Eci);
    }

    /// <summary>
    /// The brackets come off and a separator closes each variable-length element that is not last.
    /// AI 01 is a fixed fourteen characters and so needs none — which is the half of the rule that
    /// is easy to get wrong in the direction nobody notices.
    /// </summary>
    [TestMethod]
    public void AGs1BlockWritesTheWireFormAndFlagsIt()
    {
        var block = Parse("type: gs1\ndata: (01)04150123456782(10)LOT7(21)SN9");

        Assert.IsTrue(block.Options.Gs1);
        Assert.AreEqual("0104150123456782" + "10LOT7" + Gs1ElementString.Separator + "21SN9",
                        block.Payload);
    }

    [TestMethod]
    public void RefusesSettingsOutsideTheStandard()
    {
        AssertRefused("type: text\ntext: x\nformat: tiny",         "not an Aztec format");
        AssertRefused("type: text\ntext: x\nlayers: 40",            "1–32");
        AssertRefused("type: text\ntext: x\nformat: compact\nlayers: 5", "format: full");
        AssertRefused("type: text\ntext: x\necc: 99",               "0–95");
        AssertRefused("type: text\ntext: x\nlayers: two",           "not a whole number");
        AssertRefused("type: text\ntext: x\nrows: 3",               "not a field");
        AssertRefused("type: nonsense\ntext: x",                    "Unknown Aztec type");
        AssertRefused("text: x",                                    "no `type:` line");
        AssertRefused("just some prose",                            "not a `key: value` line");
    }

    /// <summary>The drawn geometry reads back as the symbol the encoder built.</summary>
    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        var block = Parse("type: text\ntext: An Aztec Code\ncellSize: 4\nmargin: 2");
        Assert.IsTrue(AztecEncoder.TryEncode(block.Payload, block.Options, out var symbol, out string? error), error);

        var border = (Border)WpfAztecRenderer.Render(symbol!, block, MarkdownPalette.Light);
        var path   = (System.Windows.Shapes.Path)border.Child;

        Assert.AreEqual(symbol!.Size * 4.0, path.Width, 0.001);
        Assert.AreEqual(symbol.Size * 4.0, path.Height, 0.001);

        var drawn = Sample(path.Data!, symbol.Size, 4);
        for (int y = 0; y < symbol.Size; y++)
            for (int x = 0; x < symbol.Size; x++)
                Assert.AreEqual(symbol[x, y], drawn[x, y], $"module {x},{y}");

        Assert.AreEqual("An Aztec Code", AztecTestDecoder.Decode(new Sampled(drawn)).Text);
    });

    [TestMethod]
    public void ABadBlock_ShowsTheReason_InsteadOfThrowing() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("aztec", "type: text", MarkdownPalette.Dark);

        Assert.IsNotNull(element);
        StringAssert.Contains(Text(element), "text:");
    });

    // ── Helpers ────────────────────────────────────────────────────────────

    private static AztecBlock Parse(string source)
    {
        Assert.IsTrue(AztecBlockParser.TryParse(source, out var block, out string? error), error);
        return block!;
    }

    private static void AssertRefused(string source, string expected)
    {
        Assert.IsFalse(AztecBlockParser.TryParse(source, out _, out string? error), $"'{source}' was accepted");
        StringAssert.Contains(error!, expected, $"'{source}' said: {error}");
    }

    /// <summary>Which modules the geometry actually covers, by hit-testing the middle of each one.</summary>
    private static bool[,] Sample(Geometry geometry, int size, double cell)
    {
        var modules = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                modules[x, y] = geometry.FillContains(new Point((x + 0.5) * cell, (y + 0.5) * cell));
        return modules;
    }

    private static string Text(DependencyObject root)
    {
        var sb = new System.Text.StringBuilder();

        void Walk(DependencyObject node)
        {
            if (node is TextBlock text) sb.Append(text.Text).Append('\n');
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }

        Walk(root);
        return sb.ToString();
    }

    private sealed class Sampled(bool[,] modules) : IModuleMatrix
    {
        public int Width  => modules.GetLength(0);
        public int Height => modules.GetLength(1);
        public bool this[int x, int y] => modules[x, y];
    }
}
