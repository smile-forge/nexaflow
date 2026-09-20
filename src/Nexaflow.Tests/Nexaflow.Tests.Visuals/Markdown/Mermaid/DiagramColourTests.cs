using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Windows.Media;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The colour arithmetic every diagram shares, under <c>DiagramInk</c>: what a colour somebody wrote comes to, how
/// bright it is, what a translucent one shows as over what is behind it, and which of two inks reads over it.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class DiagramColourTests
{

    [TestMethod]
    public void ParseCss_AcceptsHexNamedAndRgb()
    {
        Assert.AreEqual(Color.FromRgb(0x4E, 0x79, 0xA7), DiagramColour.ParseCss("#4e79a7"));
        Assert.AreEqual(Colors.Red, DiagramColour.ParseCss("red"));
        Assert.AreEqual(Color.FromRgb(191, 223, 255), DiagramColour.ParseCss("rgb(191, 223, 255)"));
        Assert.AreEqual(Color.FromRgb(1, 2, 3), DiagramColour.ParseCss("  rgb(1,2,3)  "));
    }

    [TestMethod]
    public void ParseCss_ReturnsNullForNonColours()
    {
        Assert.IsNull(DiagramColour.ParseCss(null));
        Assert.IsNull(DiagramColour.ParseCss("   "));
        Assert.IsNull(DiagramColour.ParseCss("NOTACOLOUR"));
        Assert.IsNull(DiagramColour.ParseCss("rgb(nope)"));
        Assert.IsNull(DiagramColour.ParseCss("rgb(1,2)"));      // too few components
    }

    [TestMethod]
    public void Tint_KeepsRgbAndSetsAlpha()
    {
        var b = (SolidColorBrush)DiagramColour.Tint(Color.FromRgb(0x10, 0x20, 0x30), 0x40);
        Assert.AreEqual(Color.FromArgb(0x40, 0x10, 0x20, 0x30), b.Color);
        Assert.IsTrue(b.IsFrozen, "brushes must be frozen to be shared safely");
    }

    [TestMethod]
    public void Tint_FromBrushUsesItsColour()
    {
        var source = DiagramColour.Frozen(Color.FromRgb(9, 8, 7));
        var b = (SolidColorBrush)DiagramColour.Tint(source, 0x80);
        Assert.AreEqual(Color.FromArgb(0x80, 9, 8, 7), b.Color);
    }

    [TestMethod]
    public void ColorOf_FallsBackForNonSolidBrush()
    {
        Assert.AreEqual(Colors.Red, DiagramColour.ColorOf(Brushes.Red, Colors.Black));
        Assert.AreEqual(Colors.Black, DiagramColour.ColorOf(new LinearGradientBrush(), Colors.Black));
        Assert.AreEqual(Colors.Black, DiagramColour.ColorOf(null, Colors.Black));
    }

    [TestMethod]
    public void OnColor_FlipsAtTheLuminanceThreshold()
    {
        // The threshold the renderers already used: > 140 counts as a light background.
        Assert.AreSame(Brushes.Black, DiagramColour.OnColor(Colors.White, Brushes.Black, Brushes.White));
        Assert.AreSame(Brushes.White, DiagramColour.OnColor(Colors.Black, Brushes.Black, Brushes.White));

        var justUnder = Color.FromRgb(140, 140, 140);
        Assert.AreSame(Brushes.White, DiagramColour.OnColor(justUnder, Brushes.Black, Brushes.White));
        var justOver = Color.FromRgb(141, 141, 141);
        Assert.AreSame(Brushes.Black, DiagramColour.OnColor(justOver, Brushes.Black, Brushes.White));
    }

    [TestMethod]
    public void Luminance_UsesBt601Weights()
    {
        Assert.AreEqual(0, DiagramColour.Luminance(Colors.Black), 1e-9);
        Assert.AreEqual(255, DiagramColour.Luminance(Colors.White), 1e-9);
        Assert.IsTrue(DiagramColour.Luminance(Colors.Lime) > DiagramColour.Luminance(Colors.Blue));
    }

    [TestMethod]
    public void Composite_FullAlphaIsOver_ZeroAlphaIsUnder()
    {
        var over  = Color.FromArgb(0xFF, 200, 100, 50);
        var under = Colors.Black;
        Assert.AreEqual(Color.FromRgb(200, 100, 50), DiagramColour.Composite(over, under));

        var clear = Color.FromArgb(0x00, 200, 100, 50);
        Assert.AreEqual(Color.FromRgb(0, 0, 0), DiagramColour.Composite(clear, under));
    }

    [TestMethod]
    public void Composite_HalfAlphaOverDarkStaysDark()
    {
        // Why this exists: a bright fill at half alpha over a dark canvas reads dark, so a
        // luminance test must run on the composited colour, not the fill's own.
        var half = Color.FromArgb(0x80, 255, 255, 255);
        var onDark = DiagramColour.Composite(half, Colors.Black);
        Assert.IsTrue(DiagramColour.Luminance(onDark) < 140, $"composited {onDark} should read as dark");
        var onLight = DiagramColour.Composite(half, Colors.White);
        Assert.IsTrue(DiagramColour.Luminance(onLight) > 140, $"composited {onLight} should read as light");
    }

}
