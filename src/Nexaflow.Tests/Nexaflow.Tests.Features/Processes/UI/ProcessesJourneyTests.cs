using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes.UI;

/// <summary>
/// One-pass UI journey for the Processes (Process Explorer) toolbar/filter row. Complements
/// <see cref="ProcessesViewTests"/> (which already covers opening the tab from the ribbon and opening
/// a process-detail tab from the row context menu) by exercising the safe, non-destructive view
/// controls in a single pass: the filter box, its clear button, the tree grouping toggle + expand /
/// collapse, the live auto-refresh toggle, and refresh-now.
/// <para>
/// Every control touched here is <b>read-only w.r.t. the system</b> — they re-shape or re-sample the
/// on-screen list; none terminates or re-prioritises a process. The destructive kill / kill-tree /
/// set-priority commands live only in the per-row right-click menu (not the toolbar) and are
/// deliberately neither tagged nor invoked, so this journey can never affect a real process.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("processes")]
public class ProcessesJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Processes";

    [TestMethod]
    [CoversNode("processes-ui")]
    public void Processes_ToolbarControls_RespondInOnePass()
    {
        var view = WaitForId("ProcList", 15);
        Assert.IsNotNull(view, "ProcList did not open via --openTab Processes.");

        // ── Tree grouping (default on) — safe, view-only regrouping. Toggle off then the expand/collapse
        //    buttons are only shown while tree mode is on, so exercise them before flipping it off. ──
        CheckInvoke("Expand all",   "Proc_ExpandAll");
        CheckInvoke("Collapse all", "Proc_CollapseAll");
        CheckInvoke("Tree toggle",  "Proc_ToggleTree");   // flips to flat list

        // ── Live auto-refresh toggle + manual refresh — both only re-sample the process list. ──
        CheckInvoke("Live toggle", "Proc_ToggleLive");
        CheckInvoke("Refresh now", "Proc_Refresh");

        // ── Filter box — a read-only, in-memory name/PID/company/path filter. Typing mutates nothing on
        //    disk or in the OS; the clear (✕) button only appears once the box is non-empty. ──
        var filter = CheckPresent("Filter box", "Proc_Filter");
        if (filter is not null)
        {
            filter.AsTextBox().Text = "svchost";
            System.Threading.Thread.Sleep(200);
            CheckInvoke("Clear filter", "Proc_ClearFilter");   // now visible → resets the filter
        }

        // ── Destructive controls (kill / kill-tree / set-priority) are NOT in the toolbar — they only
        //    exist in the per-row context menu and are intentionally untagged, so there is nothing safe
        //    to present-check here without right-clicking a real process row. Left uninvoked by design. ──

        AssertJourney();
    }
}
