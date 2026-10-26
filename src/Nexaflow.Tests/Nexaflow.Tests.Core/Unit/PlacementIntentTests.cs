using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which placement changes a window keeps as where the user means it to be, across the display changes Windows
/// answers by moving windows itself: a monitor asleep or detached, a resume before the dock's monitors are back, a
/// monitor returning at another resolution. Driven as the event sequences the tracker feeds it, with no window.
/// </summary>
[TestClass]
[CoversNode("win-cux-placement-restore")]
public class PlacementIntentTests
{
    private static readonly MonitorArea Laptop =
        new("laptop", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), IsPrimary: true);

    private static readonly MonitorArea External =
        new("dell", new PixelRect(1920, 0, 4480, 1440), new PixelRect(1920, 0, 4480, 1400), IsPrimary: false);

    private static readonly IReadOnlyList<MonitorArea> Docked   = [Laptop, External];
    private static readonly IReadOnlyList<MonitorArea> Undocked = [Laptop];

    // A placement on a monitor, its rectangle given relative to the monitor's origin.
    private static WindowPlacement On(MonitorArea monitor, int x, int y, int width, int height, bool maximized = false)
        => new(new PixelRect(monitor.Bounds.Left + x, monitor.Bounds.Top + y,
                             monitor.Bounds.Left + x + width, monitor.Bounds.Top + y + height), maximized, monitor);

    [TestMethod]
    public void WithTheDisplaysSteady_EveryChangeIsKept()
    {
        var intent = new PlacementIntent();

        intent.Observed(On(External, 100, 100, 1200, 800), Docked, byUser: false);
        intent.Observed(On(External, 100, 100, 1600, 900), Docked, byUser: true);
        intent.Observed(On(External, 100, 100, 1600, 900, maximized: true), Docked, byUser: false);   // Win+Up: no command

        Assert.AreEqual(On(External, 100, 100, 1600, 900, maximized: true), intent.Intended);
        Assert.AreEqual(PlacementIntent.Phase.Settled, intent.State);
    }

    [TestMethod]
    public void AMonitorThatDetaches_KeepsTheWindowMeantForIt_WhicheverMessageComesFirst()
    {
        var meant = On(External, 200, 150, 1600, 1000, maximized: true);

        // Windows moves the window off before the display change reaches it...
        var early = new PlacementIntent();
        early.Observed(meant, Docked, byUser: false);
        early.Observed(On(Laptop, 0, 0, 1600, 1000), Undocked, byUser: false);
        Assert.AreEqual(meant, early.Intended, "a move after the monitor went is not the user's");
        Assert.AreEqual(PlacementIntent.Phase.AwaitingMonitor, early.State);

        // ...or after it.
        var late = new PlacementIntent();
        late.Observed(meant, Docked, byUser: false);
        late.TopologyChanging();
        late.Observed(On(Laptop, 0, 0, 1600, 1000), Undocked, byUser: false);
        Assert.IsNull(late.TopologySettled(Undocked, On(Laptop, 0, 0, 1600, 1000)), "nothing to go back to yet");
        Assert.AreEqual(meant, late.Intended);
        Assert.AreEqual(PlacementIntent.Phase.AwaitingMonitor, late.State);
    }

    [TestMethod]
    public void ADisplayAsleepAndAwake_WithNothingMoved_PutsNothingBack()
    {
        var intent = new PlacementIntent();
        var meant  = On(External, 200, 150, 1600, 1000);
        intent.Observed(meant, Docked, byUser: false);

        intent.TopologyChanging();
        Assert.IsNull(intent.TopologySettled(Docked, meant));
        Assert.AreEqual(PlacementIntent.Phase.Settled, intent.State);
    }

    [TestMethod]
    public void OpenedBeforeTheDockIsBack_TheWindowGoesToItsMonitorWhenItArrives()
    {
        var meant  = On(External, 200, 150, 1600, 1000);
        var intent = new PlacementIntent();

        var opened = intent.Open(meant, Undocked);
        Assert.AreEqual("laptop", opened.Monitor.Id, "it opens on a monitor that is there");
        Assert.AreEqual(PlacementIntent.Phase.AwaitingMonitor, intent.State);

        // Windows nudging it while it waits changes nothing.
        intent.Observed(opened with { Normal = opened.Normal.Offset(10, 10) }, Undocked, byUser: false);
        Assert.AreEqual(meant, intent.Intended);

        intent.TopologyChanging();
        var back = intent.TopologySettled(Docked, opened);
        Assert.AreEqual(meant, back, "the monitor is back, laid out as it was, so the window goes back exactly");
        Assert.AreEqual(PlacementIntent.Phase.Settled, intent.State);
    }

    [TestMethod]
    public void MovedByTheUserWhileItsMonitorIsMissing_TheWindowStaysWhereTheyPutIt()
    {
        var intent = new PlacementIntent();
        intent.Open(On(External, 200, 150, 1600, 1000), Undocked);

        var chosen = On(Laptop, 50, 40, 1400, 900);
        intent.Observed(chosen, Undocked, byUser: true);
        Assert.AreEqual(chosen, intent.Intended);

        intent.TopologyChanging();
        Assert.IsNull(intent.TopologySettled(Docked, chosen), "no jump back once the user has placed it");
        Assert.AreEqual(chosen, intent.Intended, "and the laptop placement is what the next launch opens at");
    }

    [TestMethod]
    public void AResumeWhereWindowsHasAlreadyPutItBack_PutsNothingBack()
    {
        var intent = new PlacementIntent();
        var meant  = On(External, 200, 150, 1600, 1000);
        intent.Observed(meant, Docked, byUser: false);

        intent.TopologyChanging();                                         // suspend
        intent.Observed(On(Laptop, 0, 0, 1600, 1000), Undocked, byUser: false);
        Assert.IsNull(intent.TopologySettled(Docked, meant));

        Assert.AreEqual(meant, intent.Intended);
    }

    [TestMethod]
    public void AMonitorBackAtAnotherResolution_TakesTheWindowInProportion_AndWhollyInside()
    {
        var intent = new PlacementIntent();
        intent.Observed(On(External, 1280, 700, 1280, 700), Docked, byUser: false);   // the bottom-right quarter

        var smaller = External with { Bounds = new PixelRect(1920, 0, 3840, 1080), WorkArea = new PixelRect(1920, 0, 3840, 1040) };
        intent.TopologyChanging();
        var back = intent.TopologySettled([Laptop, smaller], On(Laptop, 0, 0, 1280, 700));

        Assert.IsNotNull(back);
        Assert.AreEqual("dell", back.Monitor.Id);
        Assert.AreEqual(new PixelRect(1920 + 960, 520, 1920 + 1920, 1040), back.Normal);
    }
}
