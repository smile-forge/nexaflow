using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Windows.Media;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

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
        Assert.AreEqual(LayoutColor.Of(0x4E, 0x79, 0xA7), PortableDiagramColour.ParseCss("#4e79a7"));
        Assert.AreEqual(LayoutColors.Red, PortableDiagramColour.ParseCss("red"));
        Assert.AreEqual(LayoutColor.Of(191, 223, 255), PortableDiagramColour.ParseCss("rgb(191, 223, 255)"));
        Assert.AreEqual(LayoutColor.Of(1, 2, 3), PortableDiagramColour.ParseCss("  rgb(1,2,3)  "));
    }

    [TestMethod]
    public void ParseCss_ReturnsNullForNonColours()
    {
        Assert.IsNull(PortableDiagramColour.ParseCss(null));
        Assert.IsNull(PortableDiagramColour.ParseCss("   "));
        Assert.IsNull(PortableDiagramColour.ParseCss("NOTACOLOUR"));
        Assert.IsNull(PortableDiagramColour.ParseCss("rgb(nope)"));
        Assert.IsNull(PortableDiagramColour.ParseCss("rgb(1,2)"));      // too few components
    }

    [TestMethod]
    public void Tint_KeepsRgbAndSetsAlpha()
    {
        var b = (SolidColorBrush)PortableDiagramColour.Tint(WpfHandles.Medium, LayoutColor.Of(0x10, 0x20, 0x30), 0x40).Brush();
        Assert.AreEqual(Color.FromArgb(0x40, 0x10, 0x20, 0x30), b.Color);
        Assert.IsTrue(b.IsFrozen, "brushes must be frozen to be shared safely");
    }

    [TestMethod]
    public void Tint_FromBrushUsesItsColour()
    {
        var source = WpfHandles.Medium.Ink(LayoutColor.Of(9, 8, 7));
        var b = (SolidColorBrush)PortableDiagramColour.Tint(WpfHandles.Medium, source, 0x80).Brush();
        Assert.AreEqual(Color.FromArgb(0x80, 9, 8, 7), b.Color);
    }

    [TestMethod]
    public void ColorOf_FallsBackForNonSolidBrush()
    {
        Assert.AreEqual(LayoutColors.Red, PortableDiagramColour.ColorOf(Brushes.Red.Ink(), LayoutColors.Black));
        Assert.AreEqual(LayoutColors.Black, PortableDiagramColour.ColorOf(new LinearGradientBrush().Ink(), LayoutColors.Black));
        Assert.AreEqual(LayoutColors.Black, PortableDiagramColour.ColorOf(null, LayoutColors.Black));
    }

    [TestMethod]
    public void OnColor_FlipsAtTheLuminanceThreshold()
    {
        // The threshold the renderers already used: > 140 counts as a light background.
        var black = Brushes.Black.Ink();
        var white = Brushes.White.Ink();

        Assert.AreSame(black, PortableDiagramColour.OnColor(LayoutColors.White, black, white));
        Assert.AreSame(white, PortableDiagramColour.OnColor(LayoutColors.Black, black, white));

        var justUnder = LayoutColor.Of(140, 140, 140);
        Assert.AreSame(white, PortableDiagramColour.OnColor(justUnder, black, white));
        var justOver = LayoutColor.Of(141, 141, 141);
        Assert.AreSame(black, PortableDiagramColour.OnColor(justOver, black, white));
    }

    [TestMethod]
    public void Luminance_UsesBt601Weights()
    {
        Assert.AreEqual(0, PortableDiagramColour.Luminance(LayoutColors.Black), 1e-9);
        Assert.AreEqual(255, PortableDiagramColour.Luminance(LayoutColors.White), 1e-9);
        Assert.IsTrue(PortableDiagramColour.Luminance(LayoutColors.Lime) > PortableDiagramColour.Luminance(LayoutColors.Blue));
    }

    [TestMethod]
    public void Composite_FullAlphaIsOver_ZeroAlphaIsUnder()
    {
        var over  = new LayoutColor(0xFF, 200, 100, 50);
        var under = LayoutColors.Black;
        Assert.AreEqual(LayoutColor.Of(200, 100, 50), PortableDiagramColour.Composite(over, under));

        var clear = new LayoutColor(0x00, 200, 100, 50);
        Assert.AreEqual(LayoutColor.Of(0, 0, 0), PortableDiagramColour.Composite(clear, under));
    }

    [TestMethod]
    public void Composite_HalfAlphaOverDarkStaysDark()
    {
        // Why this exists: a bright fill at half alpha over a dark canvas reads dark, so a
        // luminance test must run on the composited colour, not the fill's own.
        var half = new LayoutColor(0x80, 255, 255, 255);
        var onDark = PortableDiagramColour.Composite(half, LayoutColors.Black);
        Assert.IsTrue(PortableDiagramColour.Luminance(onDark) < 140, $"composited {onDark} should read as dark");
        var onLight = PortableDiagramColour.Composite(half, LayoutColors.White);
        Assert.IsTrue(PortableDiagramColour.Luminance(onLight) > 140, $"composited {onLight} should read as light");
    }

}
