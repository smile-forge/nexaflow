using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The measurement, colour and placement helpers every diagram renderer shares. They were private
/// copies in a dozen renderers before; these tests pin the behaviour the copies had, so a renderer
/// adopting the shared version cannot drift.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class DiagramPrimitivesTests
{
    // ── DiagramText ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    public void Measure_MatchesFormattedTextWidth() => UiThread.Run(() =>
    {
        const string text = "Internet Banking System";
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(DiagramText.BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            12, Brushes.Black, 1.0);
        Assert.AreEqual(ft.Width, DiagramText.Measure(text, 12), 1e-9);
    });

    [TestMethod]
    [TestCategory("UI")]
    public void Measure_BoldIsWiderThanNormal() => UiThread.Run(() =>
    {
        double normal = DiagramText.Measure("Mainframe", 12);
        double bold   = DiagramText.Measure("Mainframe", 12, FontWeights.Bold);
        Assert.IsTrue(bold > normal, $"bold {bold} should exceed normal {normal}");
    });

    [TestMethod]
    [TestCategory("UI")]
    public void MeasureBlock_TakesWidestLineAndCountsThem() => UiThread.Run(() =>
    {
        var (w, lines) = DiagramText.MeasureBlock("short\na much longer line\nmid", 11);
        Assert.AreEqual(3, lines);
        Assert.AreEqual(DiagramText.Measure("a much longer line", 11), w, 1e-9);
    });

    [TestMethod]
    [TestCategory("UI")]
    public void MeasureBlock_SingleLineIsOneLine() => UiThread.Run(() =>
    {
        Assert.AreEqual(1, DiagramText.MeasureBlock("one", 11).lines);
        Assert.AreEqual(1, DiagramText.LineCount(""));          // an empty block is still a line
        Assert.AreEqual(2, DiagramText.LineCount("a\nb"));
    });

    // ── DiagramBrushes ────────────────────────────────────────────────────

    [TestMethod]
    public void ParseCss_AcceptsHexNamedAndRgb()
    {
        Assert.AreEqual(Color.FromRgb(0x4E, 0x79, 0xA7), DiagramBrushes.ParseCss("#4e79a7"));
        Assert.AreEqual(Colors.Red, DiagramBrushes.ParseCss("red"));
        Assert.AreEqual(Color.FromRgb(191, 223, 255), DiagramBrushes.ParseCss("rgb(191, 223, 255)"));
        Assert.AreEqual(Color.FromRgb(1, 2, 3), DiagramBrushes.ParseCss("  rgb(1,2,3)  "));
    }

    [TestMethod]
    public void ParseCss_ReturnsNullForNonColours()
    {
        Assert.IsNull(DiagramBrushes.ParseCss(null));
        Assert.IsNull(DiagramBrushes.ParseCss("   "));
        Assert.IsNull(DiagramBrushes.ParseCss("NOTACOLOUR"));
        Assert.IsNull(DiagramBrushes.ParseCss("rgb(nope)"));
        Assert.IsNull(DiagramBrushes.ParseCss("rgb(1,2)"));      // too few components
    }

    [TestMethod]
    public void Tint_KeepsRgbAndSetsAlpha()
    {
        var b = (SolidColorBrush)DiagramBrushes.Tint(Color.FromRgb(0x10, 0x20, 0x30), 0x40);
        Assert.AreEqual(Color.FromArgb(0x40, 0x10, 0x20, 0x30), b.Color);
        Assert.IsTrue(b.IsFrozen, "brushes must be frozen to be shared safely");
    }

    [TestMethod]
    public void Tint_FromBrushUsesItsColour()
    {
        var source = DiagramBrushes.Frozen(Color.FromRgb(9, 8, 7));
        var b = (SolidColorBrush)DiagramBrushes.Tint(source, 0x80);
        Assert.AreEqual(Color.FromArgb(0x80, 9, 8, 7), b.Color);
    }

    [TestMethod]
    public void ColorOf_FallsBackForNonSolidBrush()
    {
        Assert.AreEqual(Colors.Red, DiagramBrushes.ColorOf(Brushes.Red, Colors.Black));
        Assert.AreEqual(Colors.Black, DiagramBrushes.ColorOf(new LinearGradientBrush(), Colors.Black));
        Assert.AreEqual(Colors.Black, DiagramBrushes.ColorOf(null, Colors.Black));
    }

    [TestMethod]
    public void OnColor_FlipsAtTheLuminanceThreshold()
    {
        // The threshold the renderers already used: > 140 counts as a light background.
        Assert.AreSame(Brushes.Black, DiagramBrushes.OnColor(Colors.White, Brushes.Black, Brushes.White));
        Assert.AreSame(Brushes.White, DiagramBrushes.OnColor(Colors.Black, Brushes.Black, Brushes.White));

        var justUnder = Color.FromRgb(140, 140, 140);
        Assert.AreSame(Brushes.White, DiagramBrushes.OnColor(justUnder, Brushes.Black, Brushes.White));
        var justOver = Color.FromRgb(141, 141, 141);
        Assert.AreSame(Brushes.Black, DiagramBrushes.OnColor(justOver, Brushes.Black, Brushes.White));
    }

    [TestMethod]
    public void Luminance_UsesBt601Weights()
    {
        Assert.AreEqual(0, DiagramBrushes.Luminance(Colors.Black), 1e-9);
        Assert.AreEqual(255, DiagramBrushes.Luminance(Colors.White), 1e-9);
        Assert.IsTrue(DiagramBrushes.Luminance(Colors.Lime) > DiagramBrushes.Luminance(Colors.Blue));
    }

    [TestMethod]
    public void Composite_FullAlphaIsOver_ZeroAlphaIsUnder()
    {
        var over  = Color.FromArgb(0xFF, 200, 100, 50);
        var under = Colors.Black;
        Assert.AreEqual(Color.FromRgb(200, 100, 50), DiagramBrushes.Composite(over, under));

        var clear = Color.FromArgb(0x00, 200, 100, 50);
        Assert.AreEqual(Color.FromRgb(0, 0, 0), DiagramBrushes.Composite(clear, under));
    }

    [TestMethod]
    public void Composite_HalfAlphaOverDarkStaysDark()
    {
        // Why this exists: a bright fill at half alpha over a dark canvas reads dark, so a
        // luminance test must run on the composited colour, not the fill's own.
        var half = Color.FromArgb(0x80, 255, 255, 255);
        var onDark = DiagramBrushes.Composite(half, Colors.Black);
        Assert.IsTrue(DiagramBrushes.Luminance(onDark) < 140, $"composited {onDark} should read as dark");
        var onLight = DiagramBrushes.Composite(half, Colors.White);
        Assert.IsTrue(DiagramBrushes.Luminance(onLight) > 140, $"composited {onLight} should read as light");
    }

    // ── Canvas placement ──────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("UI")]
    public void PlaceAndAt_SetCanvasLeftTopAndReturnTheElement() => UiThread.Run(() =>
    {
        var a = new Rectangle();
        Assert.AreSame(a, a.Place(12, 34));
        Assert.AreEqual(12, Canvas.GetLeft(a), 1e-9);
        Assert.AreEqual(34, Canvas.GetTop(a), 1e-9);

        var b = new Rectangle();
        Assert.AreSame(b, b.At(56, 78));
        Assert.AreEqual(56, Canvas.GetLeft(b), 1e-9);
        Assert.AreEqual(78, Canvas.GetTop(b), 1e-9);
    });
}
