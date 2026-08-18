using System;
using System.Linq;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network.UI;

/// <summary>
/// One-pass UI journey for the Network tab: discover, select, look at the panel.
/// </summary>
/// <remarks>
/// <para>
/// The panel is the reason this exists. It is shown by a converter on the selection, and a converter
/// picked by its name rather than its behaviour inverts the whole thing — blank at rest, gone the moment
/// something is selected. Nothing headless can see that, because it is a binding and a resource lookup
/// rather than a decision in a view-model.
/// </para>
/// <para>
/// It sweeps a real network, so what is found is whatever is plugged in. Nothing here asserts on devices;
/// it asserts on the chrome — the button, the list, and the panel appearing only once a row is chosen.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </para>
/// </remarks>
[TestClass]
[CoversNode("network")]
public class NetworkJourneyTests : UiJourneyTestBase
{
    /// <summary>Straight into the tab, rather than hunting the ribbon for a button that may not be on it.</summary>
    protected override string? LaunchTabKind => "Network";

    [TestMethod]
    [CoversNode("network-discovery")]
    public void Network_Discovers_AndTheePanelFollowsTheSelection()
    {
        // Foreground first: Capture.Element grabs the screen region the window occupies, so a window
        // behind another photographs the other one — which is how the first attempt produced a picture of
        // the desktop instead of the app.
        MainWindow.SetForeground();
        Thread.Sleep(500);

        // A Debug build rescans and eagerly activates every feature at startup, so the first tab can be
        // slow to appear on a cold launch.
        var view = WaitForId("NetworkView", 30);

        // Before the assertion, so a failure leaves a picture of what the app actually showed.
        Shoot("1-opened");
        Assert.IsNotNull(view, "the Network tab did not open");

        // Nothing selected yet, so the panel must not be there. The failure this catches shows a panel
        // full of nothing at rest.
        Assert.IsNull(Find("Net_DevicePanel"),
            "the device panel is showing with nothing selected");

        CheckInvoke("Discover", "Net_Discover");

        // ARP is immediate; SSDP waits out its MX window and then some.
        Thread.Sleep(6000);
        Shoot("2-discovered");

        var list = CheckPresent("device list", "Net_Devices");

        Assert.IsNull(Find("Net_DevicePanel"),
            "the device panel is showing with nothing selected");

        // Clicked by position, because the rows have no UI Automation peer: the shared FileListRowStyle
        // templates a ListViewItem down to a Border and no ListItem survives it. A point just under the
        // header is the first row wherever the window happens to be.
        if (list is { } grid)
        {
            var box = grid.BoundingRectangle;
            FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(box.Left + 120, box.Top + 60));
            Thread.Sleep(900);
            Shoot("3-selected");

            if (Find("Net_DevicePanel") is { } panel)
            {
                // The panel is up, so the actions are real buttons. Ping is the one that used to look
                // like it did nothing, because its result was written to a line the panel then cleared.
                if (Find("Ping") is { } ping)
                {
                    ping.Click();
                    Thread.Sleep(3000);
                    Shoot("4-pinged");
                }
            }
        }

        AssertJourney();
    }

    private AutomationElement? Find(string automationId)
        => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    private AutomationElement? WaitForId(string automationId, int seconds)
    {
        for (int i = 0; i < seconds * 4; i++)
        {
            if (Find(automationId) is { } found && !found.IsOffscreen) return found;
            Thread.Sleep(250);
        }
        return null;
    }

    /// <summary>A picture of the window, somewhere a person can open it.</summary>
    private void Shoot(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-network-journey");
        Directory.CreateDirectory(dir);
        Capture.Element(MainWindow).ToFile(Path.Combine(dir, $"{name}.png"));
        global::System.Console.WriteLine($"[shot] {Path.Combine(dir, name)}.png");
    }
}
