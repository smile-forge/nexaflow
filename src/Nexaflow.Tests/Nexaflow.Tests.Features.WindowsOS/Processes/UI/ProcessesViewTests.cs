using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes.UI;

/// <summary>
/// Smoke-drives the Processes feature in the real shell: opens the tab from the ribbon and the detail
/// view from a double-click. The value here is that both XAML views are actually loaded at runtime, so
/// a missing theme resource or a malformed template fails the test instead of silently shipping.
/// Requires an interactive desktop — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("details-view")]
public class ProcessesViewTests : UITestBase
{
    [TestMethod]
    public void ProcessesTab_Opens_AndPopulates()
    {
        var list = OpenProcessesTab();
        Assert.IsTrue(WaitForRows(list) > 0, "The process list should populate with rows after the first sample.");
        Assert.IsFalse(App.HasExited, "App crashed after opening the Processes tab.");
    }

    [TestMethod]
    public void ProcessDetail_Opens_FromContextMenu()
    {
        var list = OpenProcessesTab();
        Assert.IsTrue(WaitForRows(list) > 0, "Need at least one process row to open detail.");

        var row = Rows(list).FirstOrDefault();
        Assert.IsNotNull(row, "Process list should have a row.");

        // Right-click → "View details" exercises the row context menu's command bindings and opens detail.
        row.RightClick();
        var viewDetails = WaitForDesktopName("View details");
        Assert.IsNotNull(viewDetails, "Right-clicking a process row should show a 'View details' menu item.");
        viewDetails!.Click();

        // The ProcessDetail tab's presence means the detail view loaded without throwing
        // (a XAML failure would crash the app, which we also assert against).
        Assert.IsNotNull(WaitForId("TabItem_ProcessDetail"),
            "Choosing 'View details' should open the process detail tab.");
        Assert.IsFalse(App.HasExited, "App crashed after opening process detail.");
    }

    /// <summary>Clicks the Processes ribbon button directly (it's in the default ribbon), rather than
    /// cycling through every ribbon button — no unrelated views get loaded.</summary>
    private AutomationElement OpenProcessesTab()
    {
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        Assert.IsNotNull(ribbon, "RibbonControl not found.");

        // The button's content is an icon+label StackPanel (so the Button itself has no Name); match the
        // "Processes" label text, falling back to the button whose tooltip (HelpText) is "Processes".
        var target = ribbon.FindFirstDescendant(cf => cf.ByName("Processes"))
            ?? ribbon.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                     .FirstOrDefault(b => (b.HelpText ?? "").Contains("Processes", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(target, "Processes button not found in the ribbon (expected from default-ribbon.json).");
        target!.Click();

        var list = WaitForId("ProcList");
        Assert.IsNotNull(list, "Processes list (ProcList) should appear after clicking the Processes ribbon button.");
        return list!;
    }

    private static AutomationElement[] Rows(AutomationElement list)
    {
        var rows = list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        return rows.Length > 0 ? rows : list.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
    }

    private static int WaitForRows(AutomationElement list)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            var n = Rows(list).Length;
            if (n > 0) return n;
            System.Threading.Thread.Sleep(200);
        }
        return 0;
    }

    private AutomationElement? WaitForId(string automationId)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is not null && !el.IsOffscreen) return el;
            System.Threading.Thread.Sleep(150);
        }
        return null;
    }

    // Context-menu popups live in their own window, so search the desktop, not the shell window.
    private AutomationElement? WaitForDesktopName(string name)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            var el = Automation.GetDesktop().FindFirstDescendant(cf => cf.ByName(name));
            if (el is not null) return el;
            System.Threading.Thread.Sleep(150);
        }
        return null;
    }
}
