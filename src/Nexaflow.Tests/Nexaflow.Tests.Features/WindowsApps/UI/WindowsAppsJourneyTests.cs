using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsApps.UI;

/// <summary>
/// The one UI journey for the Installed Apps tab — the integration test registered at the feature's UI
/// node. It opens from the ribbon (there is no file to double-click), waits for the discovery scan to
/// replace "Discovering…" with the app count, then exercises the read-only chrome: the app list and the
/// Refresh button.
/// <para>
/// The per-row "⋯" menu is deliberately never opened here: every entry that isn't Open Location
/// (Uninstall, Remove from list, Modify) mutates the machine's installed-software state, and Move /
/// Advanced options need a real Store package to act on. They are asserted headlessly at their own leaf
/// nodes instead — where the confirmation gate can be declined and the package backend can be faked.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("windowsapps")]
public class WindowsAppsJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "WindowsApps";

    [TestMethod]
    [CoversNode("windowsapps-ui")]
    public void WindowsApps_Controls_RespondInOnePass()
    {
        var view = WaitForId("WindowsAppsView", 15);
        Assert.IsNotNull(view, "WindowsAppsView did not open via --openTab WindowsApps.");

        // The scan is a background task; the list is the thing that proves it landed.
        CheckPresent("App list", "WindowsApps_List", seconds: 30);

        // Refresh re-runs the scan — safe, read-only, and the list must survive it.
        CheckDoes("Refresh", "WindowsApps_Refresh", () => WaitForId("WindowsApps_List", 30) is not null);

        AssertJourney();
    }

    /// <summary>
    /// The one interactive pass over the Advanced options pane. Everything touched here is read-only —
    /// opening the pane only queries the deployment manager for drives and add-ons; the buttons that
    /// would change the machine (Terminate / Repair / Reset / Move) are asserted to be <i>present</i>,
    /// never invoked. Their behaviour is covered headlessly at their own leaf nodes.
    /// </summary>
    [TestMethod]
    [CoversNode("windowsapps-advanced")]
    public void WindowsApps_AdvancedOptionsPane_OpensForAStoreAppAndClosesAgain()
    {
        Assert.IsNotNull(WaitForId("WindowsAppsView", 15), "WindowsAppsView did not open.");
        var list = WaitForId("WindowsApps_List", 30);
        Assert.IsNotNull(list, "The app list never appeared.");

        if (!OpenAdvancedOptionsOnAStoreRow(list!))
            Assert.Inconclusive("No Microsoft Store app was visible in the list to open Advanced options on.");

        CheckPresent("Advanced pane", "WindowsApps_AdvancedPane", seconds: 15);
        CheckPresent("Background permission dropdown", "WindowsApps_BackgroundMode");
        CheckPresent("Terminate", "WindowsApps_Terminate");
        CheckPresent("Repair", "WindowsApps_Repair");
        CheckPresent("Reset", "WindowsApps_Reset");
        CheckPresent("Add-ons list", "WindowsApps_AddOns", seconds: 20);

        // The pane hands the width back to the list when dismissed.
        CheckDoes("Close advanced options", "WindowsApps_AdvancedClose",
                  () => WaitFor(() => WaitForId("WindowsApps_AdvancedPane", 1), 3) is null);

        AssertJourney();
    }

    /// <summary>
    /// Walks the realised rows' "⋯" buttons until one offers "Advanced options" — that entry only exists
    /// on a Store row, and the list is a name-sorted mix of Win32 and Store. Scrolls on to the next screen
    /// of rows when a screenful yields none. Returns false when the whole sweep found no Store app.
    /// </summary>
    private bool OpenAdvancedOptionsOnAStoreRow(AutomationElement list)
    {
        // The list element exists before the scan lands — wait for actual rows, not just the ListView.
        if (WaitFor(() => RowMenuButtons(list).FirstOrDefault(), seconds: 40) is null) return false;

        var tried = new HashSet<int>();
        for (var screen = 0; screen < 6; screen++)
        {
            foreach (var button in RowMenuButtons(list))
            {
                if (!tried.Add(button.BoundingRectangle.Top)) continue;   // already visited this row

                try { button.Click(); } catch { continue; }
                Wait.UntilInputIsProcessed();
                Thread.Sleep(150);

                // A WPF context menu is its own popup window, so it isn't under the shell window.
                var entry = WaitFor(
                    () => Automation.GetDesktop().FindFirstDescendant(cf => cf.ByName("Advanced options")), 1);
                if (entry is not null)
                {
                    entry.Click();
                    Wait.UntilInputIsProcessed();
                    return true;
                }

                Keyboard.Press(VirtualKeyShort.ESCAPE);   // a Win32 row — close and try the next
                Thread.Sleep(100);
            }

            list.Click();                                  // focus the list so paging applies to it
            Keyboard.Press(VirtualKeyShort.NEXT);          // Page Down → the next screenful of rows
            Thread.Sleep(250);
        }
        return false;
    }

    private static AutomationElement[] RowMenuButtons(AutomationElement list)
    {
        try
        {
            return list.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                       .Where(b => b.Name == "⋯")
                       .ToArray();
        }
        catch { return []; }   // the tree churns while the list virtualises — the caller retries
    }
}
