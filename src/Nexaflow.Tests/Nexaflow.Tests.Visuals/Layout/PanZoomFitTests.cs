using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Tests.Visuals.Layout;

/// <summary>
/// How far a fit enlarges content smaller than its viewport: not at all by default — a small diagram blown up to fill a
/// panel looks broken — and as far as the host allows, never past the zoom's own ceiling, for a picture worked on.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("images-edit-zoom")]
public class PanZoomFitTests
{
    private static readonly Size Viewport = new(400, 300);

    /// <summary>A 20 by 10 piece of content on a viewport, laid out and fitted.</summary>
    private static double FittedScale(double? fitUpTo = null, double? maxScale = null)
    {
        var surface = new PanZoomSurface();
        if (maxScale is { } most) surface.MaxScale = most;
        if (fitUpTo is { } upTo) surface.FitUpTo = upTo;
        surface.SetSurfaceContent(new Border { Width = 20, Height = 10 }, new Size(20, 10));
        surface.Measure(Viewport);
        surface.Arrange(new Rect(Viewport));
        surface.UpdateLayout();
        surface.FitToContent();
        return surface.View.Scale;
    }

    [TestMethod]
    public void SmallContent_KeepsItsOwnSize_UnlessTheHostLetsAFitEnlargeIt() => UiThread.Run(() =>
    {
        Assert.AreEqual(1, FittedScale(), 1e-9, "by default a fit never enlarges");
        Assert.AreEqual(8, FittedScale(fitUpTo: 8, maxScale: 16), 1e-9, "as far as the host allows");
        Assert.AreEqual(20, FittedScale(fitUpTo: 100, maxScale: 100), 1e-9, "and no further than fills the viewport");
        Assert.AreEqual(4, FittedScale(fitUpTo: 100, maxScale: 4), 1e-9, "never past the zoom's ceiling");
    });
}
