using System.Windows;
using Nexaflow.Icons;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>
/// A resource icon takes the room its glyph would, whether or not it has a picture to draw, so a picture arriving from
/// the shell worker never moves what is laid out around it — and that room is the one <see cref="IconGlyph"/> takes, so
/// a template swapping one for the other is laid out as before.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[CoversNode("icons-resource-icon")]
public class ResourceIconTests
{
    [TestMethod]
    public void APicture_TakesTheGlyphsRoom() => UiThread.Run(() =>
    {
        var glyph   = Measured(new ResourceIcon { Icon = IconRef.Emoji("🔗"), FontSize = 18 });
        var picture = Measured(new ResourceIcon { Icon = IconRef.Emoji("🔗"), Resource = new ShellIconRef(@"C:\Apps\app.exe", 0), FontSize = 18 });

        Assert.AreEqual(glyph, picture, "the picture's arrival must not move the layout");
        Assert.IsTrue(glyph.Width >= 18 && glyph.Height >= 18, "and the room holds a picture FontSize wide");
    });

    [TestMethod]
    [DataRow("🔗")]
    [DataRow("fluent:arrow_clockwise")]
    public void IsSizedAsIconGlyphIs(string icon) => UiThread.Run(() =>
    {
        var glyph    = Measured(new IconGlyph { Icon = IconRef.Parse(icon), FontSize = 24 });
        var resource = Measured(new ResourceIcon { Icon = IconRef.Parse(icon), FontSize = 24 });

        Assert.AreEqual(glyph.Height, resource.Height, 0.5, "the ribbon's buttons keep their height");
        Assert.AreEqual(Math.Max(glyph.Width, 24), resource.Width, 0.5);
    });

    private static Size Measured(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }
}
