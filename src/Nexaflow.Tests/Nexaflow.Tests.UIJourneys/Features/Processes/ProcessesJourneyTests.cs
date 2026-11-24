using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;
using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.Processes.UI;

/// <summary>
/// The Processes (Process Explorer) journey: the page opens with the app, its list fills, the safe,
/// non-destructive toolbar controls are exercised (the filter box, its clear button, the tree grouping
/// toggle + expand / collapse, the live auto-refresh toggle, refresh-now), and a detail tab is opened
/// from a row's context menu on a process the journey started itself.
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

    /// <summary>The Processes page and its detail tab, one launch: the toolbar and filter row, then a process this journey starts itself.</summary>
    [TestMethod]
    [CoversNode("processes-ui")]
    [CoversNode("details-view")]
    public void Processes_Journey()
    {
        RunStep("the toolbar and filter row, and the list filling", ToolbarControls);
        RunStep("a process's detail tab, opened from its row", ProcessDetailControls);
        AssertJourney();
    }

    private void ToolbarControls()
    {
        var view = WaitForId("ProcList", 15);
        Assert.IsNotNull(view, "ProcList did not open via --openTab Processes.");
        // Rows land after the first sample; until then expand/collapse have nothing to act on.
        Assert.IsTrue(WaitForFs(() => CountOf("Proc_RowExpand") > 0, 15), "The process list never filled.");

        // ── Tree grouping (default on) — safe, view-only regrouping. Toggle off then the expand/collapse
        //    buttons are only shown while tree mode is on, so exercise them before flipping it off. ──
        CheckInvoke("Expand all",   "Proc_ExpandAll");
        CheckInvoke("Collapse all", "Proc_CollapseAll");
        // A row's ▶ toggles just its branch; pressed twice so the list is left as it was.
        Check("A row's expander toggles, and back", () => PressNth("Proc_RowExpand", 0) && PressNth("Proc_RowExpand", 0));
        CheckInvoke("Tree toggle",  "Proc_ToggleTree");   // flips to flat list

        // ── Live auto-refresh toggle + manual refresh — both only re-sample the process list. ──
        CheckInvoke("Live toggle", "Proc_ToggleLive");
        CheckInvoke("Refresh now", "Proc_Refresh");

        // A step leaves the page as it found it: the next one starts a process and needs the list to be live and
        // grouped as it was at launch to see it arrive.
        CheckInvoke("Live toggle back", "Proc_ToggleLive");
        CheckInvoke("Tree toggle back", "Proc_ToggleTree");

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
    }

    private void ProcessDetailControls()
    {
        Assert.IsNotNull(WaitForId("ProcList", 15), "ProcList did not open via --openTab Processes.");

        // The detail tab is opened on a process this test starts itself, so Kill can be pressed for real without
        // touching anything the machine was running.
        using var target = Process.Start(new ProcessStartInfo("ping", "-n 600 127.0.0.1")
        {
            CreateNoWindow  = true,
            UseShellExecute = false,
        })!;
        try
        {
            var pid = target.Id.ToString();
            Check("Filtering by PID finds the started process", () =>
                TypeInto("Proc_Filter", pid) && WaitForFs(() => RowFor(pid) is not null, 8));
            Check("View details opens its detail tab", () =>
            {
                var row = RowFor(pid);
                if (row is null) return false;
                row.RightClick();
                Wait.UntilInputIsProcessed();
                return PickMenuItemById("Proc_ViewDetails") && WaitForId("ProcessDetail_Kill", 10) is not null;
            });

            // ── Header: sampling ──────────────────────────────────────────────────
            CheckInvoke("Live toggle", "ProcessDetail_ToggleLive");
            CheckInvoke("Refresh now", "ProcessDetail_Refresh");

            // ── General: the copy buttons only write the clipboard ────────────────
            CheckInvoke("Copy path", "ProcessDetail_CopyPath");
            CheckInvoke("Copy command line", "ProcessDetail_CopyCommandLine");

            // ── Handles: loading them elevates, which raises UAC, so it is only checked for ──
            Check("The Handles section opens", () => SelectDetailSection("Handles"));
            CheckExists("Load handles (admin)", "ProcessDetail_LoadHandles");
            Check("The General section opens again", () => SelectDetailSection("General"));

            // ── Kill: declined once, then confirmed ───────────────────────────────
            Check("Kill asks first", () =>
                PressNth("ProcessDetail_Kill", 0) && WaitForId("Chrome_ConfirmCancel", 5) is not null);
            CheckDoes("Declining leaves the process running", "Chrome_ConfirmCancel",
                      () => WaitForGone("Chrome_ConfirmCancel") && !target.HasExited);
            Check("Kill asks again", () =>
                PressNth("ProcessDetail_Kill", 0) && WaitForId("Chrome_ConfirmOk", 5) is not null);
            CheckDoes("Confirming terminates it", "Chrome_ConfirmOk", () => WaitForFs(() => target.HasExited, 5));
        }
        finally
        {
            if (!target.HasExited) target.Kill();
        }
    }

    /// <summary>The list row whose cells include this PID — a PID filter is a substring match, so it can list more.</summary>
    private AutomationElement? RowFor(string pid) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ProcList"))
                  ?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem).Or(cf.ByControlType(ControlType.DataItem)))
                  .FirstOrDefault(r => r.FindFirstDescendant(cf => cf.ByName(pid)) is not null);

    /// <summary>Selects one of the detail view's vertical tabs by its header.</summary>
    private bool SelectDetailSection(string header)
    {
        var item = MainWindow.FindAllDescendants(cf => cf.ByName(header))
                             .Select(e => e.Patterns.SelectionItem.PatternOrDefault)
                             .FirstOrDefault(p => p is not null);
        if (item is null) return false;
        item.Select();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => item.IsSelected.Value, 3);
    }
}
