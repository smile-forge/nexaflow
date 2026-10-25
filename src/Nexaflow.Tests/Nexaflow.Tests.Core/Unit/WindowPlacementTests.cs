using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core.Models;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What a window's <c>WINDOWPLACEMENT</c> reads as, and how a saved placement is put onto the monitors attached
/// now: as it was on its own monitor, in proportion on another, never outside the work area.
/// </summary>
[TestClass]
[CoversNode("win-cux-placement-restore")]
public class WindowPlacementTests
{
    // A taskbar along the top: workspace coordinates are 40px above screen coordinates on this monitor.
    private static readonly MonitorArea TopTaskbar =
        new("laptop", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 40, 1920, 1080), IsPrimary: true);

    private static readonly MonitorArea Right =
        new("dell", new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), IsPrimary: false);

    private static readonly PixelRect Restore = new(100, 100, 1100, 800);

    [TestMethod]
    public void Maximised_AndMinimisedFromMaximised_AreMaximised_AnyOtherMinimisedIsNormal()
    {
        var maximized = WindowPlacement.FromNative(WindowPlacement.SW_SHOWMAXIMIZED, 0, Restore, null, TopTaskbar);
        var restoresToMax = WindowPlacement.FromNative(WindowPlacement.SW_SHOWMINIMIZED, WindowPlacement.WPF_RESTORETOMAXIMIZED,
                                                       Restore, new PixelRect(-32000, -32000, -31840, -31972), TopTaskbar);
        var minimized = WindowPlacement.FromNative(WindowPlacement.SW_SHOWMINIMIZED, 0, Restore,
                                                   new PixelRect(-32000, -32000, -31840, -31972), TopTaskbar);

        Assert.IsTrue(maximized.Maximized);
        Assert.IsTrue(restoresToMax.Maximized);
        Assert.IsFalse(minimized.Maximized);
        Assert.AreEqual(Restore, minimized.Normal, "a minimised window's own rectangle is never its placement");
    }

    [TestMethod]
    public void ASnappedWindow_IsPlacedWhereItShows_InWorkspaceCoordinates()
    {
        // Snapped to the left half below the top taskbar: on screen (0,40)-(960,1080), restore rect elsewhere.
        var snapped = WindowPlacement.FromNative(WindowPlacement.SW_SHOWNORMAL, 0, Restore,
                                                 new PixelRect(0, 40, 960, 1080), TopTaskbar);

        Assert.AreEqual(new PixelRect(0, 0, 960, 1040), snapped.Normal);
        Assert.IsFalse(snapped.Maximized);
    }

    [TestMethod]
    public void OnItsOwnMonitorLaidOutAsItWas_APlacementIsUnchanged_EvenPartlyOffIt()
    {
        var spanning = new WindowPlacement(new PixelRect(1500, 100, 2600, 900), false, TopTaskbar);

        var fitted = spanning.FitTo([TopTaskbar, Right], out bool onOwnMonitor);

        Assert.IsTrue(onOwnMonitor);
        Assert.AreEqual(spanning, fitted, "a window stretched across two monitors stays stretched");
    }

    [TestMethod]
    public void WithItsMonitorGone_APlacementGoesToTheMonitorNowInItsPlace_WhollyInside()
    {
        var gone       = new MonitorArea("benq", Right.Bounds, Right.WorkArea, IsPrimary: false);
        var onGone     = new WindowPlacement(new PixelRect(3000, 200, 4000, 900), true, gone);
        var replacement = new MonitorArea("dell", new PixelRect(1920, 0, 3200, 720), new PixelRect(1920, 0, 3200, 690), IsPrimary: false);

        var fitted = onGone.FitTo([TopTaskbar, replacement], out bool onOwnMonitor);

        Assert.IsFalse(onOwnMonitor);
        Assert.AreEqual("dell", fitted.Monitor.Id, "the monitor now occupying its place on the desktop");
        Assert.IsTrue(fitted.Maximized);
        Assert.IsTrue(fitted.Normal.Left >= replacement.Bounds.Left && fitted.Normal.Right <= replacement.Bounds.Right);
        Assert.IsTrue(fitted.Normal.Top  >= replacement.Bounds.Top  && fitted.Normal.Height <= replacement.WorkArea.Height);
    }

    [TestMethod]
    public void WithNothingInItsPlace_APlacementGoesToThePrimary()
    {
        var gone   = new MonitorArea("benq", new PixelRect(-2560, 0, 0, 1440), new PixelRect(-2560, 0, 0, 1400), IsPrimary: false);
        var onGone = new WindowPlacement(new PixelRect(-2000, 100, -800, 900), false, gone);

        var fitted = onGone.FitTo([TopTaskbar, Right], out _);

        Assert.AreEqual("laptop", fitted.Monitor.Id);
    }

    [TestMethod]
    public void AnEntryWithNoSizeOrNoMonitor_IsNotUsable()
    {
        Assert.IsTrue(new WindowPlacement(Restore, false, Right).IsUsable);
        Assert.IsFalse(new WindowPlacement(new PixelRect(10, 10, 10, 500), false, Right).IsUsable);
        Assert.IsFalse(new WindowPlacement(Restore, false, null!).IsUsable);
        Assert.IsFalse(new WindowPlacement(Restore, false, Right with { Id = "" }).IsUsable);
    }
}
