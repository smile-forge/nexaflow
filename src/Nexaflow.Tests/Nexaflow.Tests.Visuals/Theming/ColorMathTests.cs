using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Theming;

/// <summary>The picker's square and strips drag in HSV and write RGB, so the conversion must round-trip exactly; the
/// contrast readout must agree with WCAG's own reference points.</summary>
[TestClass]
[CoversNode("vcommon-color-picker")]
public class ColorMathTests
{
    [TestMethod]
    [DataRow(255, 0, 0)]
    [DataRow(0, 255, 0)]
    [DataRow(0, 0, 255)]
    [DataRow(79, 142, 247)]
    [DataRow(18, 18, 18)]
    [DataRow(255, 255, 255)]
    [DataRow(200, 120, 33)]
    public void Hsv_RoundTripsRgb(int r, int g, int b)
    {
        var colour = Color.FromRgb((byte)r, (byte)g, (byte)b);
        Assert.AreEqual(colour, HsvColor.FromColor(colour).ToColor());
    }

    [TestMethod]
    public void Hsv_PrimaryHues()
    {
        Assert.AreEqual(0,   HsvColor.FromColor(Colors.Red).H, 1e-9);
        Assert.AreEqual(120, HsvColor.FromColor(Color.FromRgb(0, 255, 0)).H, 1e-9);
        Assert.AreEqual(240, HsvColor.FromColor(Colors.Blue).H, 1e-9);
    }

    [TestMethod]
    public void Hsv_KeepsAlpha() => Assert.AreEqual(0x40, new HsvColor(10, 1, 1).ToColor(0x40).A);

    [TestMethod]
    public void Hsv_HueWrapsAround() => Assert.AreEqual(new HsvColor(0, 1, 1).ToColor(), new HsvColor(360, 1, 1).ToColor());

    [TestMethod]
    public void Contrast_BlackOnWhiteIs21() => Assert.AreEqual(21, ColorContrast.Ratio(Colors.Black, Colors.White), 1e-9);

    [TestMethod]
    public void Contrast_SameColourIs1() => Assert.AreEqual(1, ColorContrast.Ratio(Colors.Teal, Colors.Teal), 1e-9);

    [TestMethod]
    public void Contrast_IsSymmetric()
        => Assert.AreEqual(ColorContrast.Ratio(Colors.Navy, Colors.Gold), ColorContrast.Ratio(Colors.Gold, Colors.Navy), 1e-9);
}
