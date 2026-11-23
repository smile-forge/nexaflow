using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;
using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.SystemInfo.UI;

/// <summary>
/// One-pass UI journey for the System Information dashboard (PageKind "SystemInfo", root
/// <c>SystemInfoView</c>). Like the Scratchpad it opens from the ribbon rather than a file, so the
/// journey uses <see cref="UITestBase.TryOpenTabWithElement"/> to click ribbon buttons until the view
/// root appears, then exercises the dashboard's single interactive control — the "Refresh" button.
/// <para>
/// The dashboard has no in-view page navigation (the Services / Environment-Variables pages are their
/// own tabs, not sub-pages of this view) and no service-mutating or privileged controls — those live in
/// the separate Services view. So the only safe control to invoke here is Refresh, which merely re-queues
/// the off-thread WMI gather; it's soft-asserted so a gap doesn't hide the rest of the pass.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("sysinfo")]
public class SystemInfoJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "SystemInfo";

    /// <summary>The dashboard, then the Services and Environment Variables pages opened from it — three pages of one feature, one launch.</summary>
    [TestMethod]
    [CoversNode("sysinfo-ui")]
    [CoversNode("sysinfo-services")]
    [CoversNode("sysinfo-envvars")]
    public void SystemInfo_Journey()
    {
        RunStep("the dashboard", DashboardControls);
        RunStep("the Services page", ServicesControls);
        RunStep("the Environment Variables page", EnvVarsControls);
        AssertJourney();
    }

    private void DashboardControls()
    {
        var view = WaitForId("SystemInfoView", 15);
        Assert.IsNotNull(view, "SystemInfoView did not open via --openTab SystemInfo.");

        // Refresh is safe — it only re-queues the background system-info gather.
        CheckInvoke("Refresh", "SysInfo_Refresh");

        // The Local AI card offers a check of the machine where the workspace runs models of Nexaflow's own, and which
        // machines that is differs: a desktop with no AI folder is offered none, the sandbox is offered one. It loads
        // those models and has each do its work, gigabytes no journey fetches, so it is present-checked and never pressed.
        Check("A Local AI check, where offered, is the machine check", () =>
            !Exists("SysInfo_SectionAction")
            || WaitForId("SysInfo_SectionAction", 2)?.Name == "Check this machine");
    }

    /// <summary>
    /// The Services page (root <c>ServicesView</c>): refresh, then the row actions.
    /// <para>
    /// <b>No row action is pressed.</b> Start, Stop, Restart, Pause and Resume act on a real Windows service
    /// through the elevation bridge. They are stamped on every row, so each check finds the first row's, and they
    /// are present-checked only; which of them is enabled depends on that service's state, which is the grid's
    /// concern (covered headlessly by ServicesSurfaceTests), not this journey's.
    /// </para>
    /// </summary>
    private void ServicesControls()
    {
        var view = OpenPageViaSlash("serv", "ServicesView", 15);
        Assert.IsNotNull(view, "ServicesView did not open from the slash palette.");

        CheckInvoke("Refresh", "Services_Refresh");

        // The service list is gathered off-thread, so the first row gets a longer wait than the rest.
        CheckPresent("Start",   "Services_Start", 20);
        CheckPresent("Stop",    "Services_Stop");
        CheckPresent("Restart", "Services_Restart");
        CheckPresent("Pause",   "Services_Pause");
        CheckPresent("Resume",  "Services_Resume");
    }

    /// <summary>An entry no real PATHEXT carries, so finding it in the value proves the journey put it there.</summary>
    private const string JourneyEntry = ".NXJOURNEY";

    /// <summary>
    /// The Environment Variables page (root <c>EnvVarsView</c>): refresh, the Add prompt, the scope picker and filter, and
    /// the PATH-style entry editor.
    /// <para>
    /// <b>Nothing here writes a variable.</b> Add only asks for a name, and its prompt is cancelled. Save and
    /// Delete are present-checked and never pressed. The entry editor is exercised on PATHEXT — a ';' list on
    /// every Windows machine — but only changes the value held in the editor. Each step is proven on that value,
    /// and a final Refresh throws the edit away rather than trusting that nothing saved it.
    /// </para>
    /// </summary>
    private void EnvVarsControls()
    {
        var view = OpenPageViaSlash("environment", "EnvVarsView", 15);
        Assert.IsNotNull(view, "EnvVarsView did not open from the slash palette.");

        CheckInvoke("Refresh", "EnvVars_Refresh");

        // Add asks the shell for a name; nothing is written until that prompt is confirmed, and it never is.
        CheckDoes("Add asks for a name", "EnvVars_Add", () => WaitForId("ShellPromptCancel", 5) is not null);
        CheckDoes("Cancelling the name prompt closes it", "ShellPromptCancel", () => WaitForGone("ShellPromptCancel"));

        Check("Machine scope can be picked", () => SelectScope("Machine"));
        Check("Filtering to PATHEXT selects a list variable", SelectPathExt);

        // Both write the variable — Machine scope through the elevation bridge.
        CheckPresent("Save", "EnvVars_Save");
        CheckPresent("Delete", "EnvVars_Delete");

        // The entry editor. Up/down are stamped on every row, so they are pressed by position: down on the
        // first row swaps the first two entries, up on the second swaps them back.
        Check("A new entry can be typed", () => TypeInto("EnvVars_NewEntry", JourneyEntry));
        CheckDoes("Add entry appends it to the value", "EnvVars_AddEntry",
                  () => WaitForFs(() => Value().EndsWith(";" + JourneyEntry, StringComparison.OrdinalIgnoreCase), 3));

        var withEntry = "";
        Check("Move down reorders the entries", () =>
        {
            withEntry = Value();
            return PressNth("EnvVars_EntryDown", 0) && WaitForFs(() => Value() != withEntry, 3);
        });
        Check("Move up puts them back", () => PressNth("EnvVars_EntryUp", 1) && WaitForFs(() => Value() == withEntry, 3));
        Check("Remove takes the journey entry out", () =>
            PressNth("EnvVars_EntryRemove", -1) && WaitForFs(() => !Value().Contains(JourneyEntry), 3));

        CheckInvoke("Refresh discards the edit", "EnvVars_Refresh");
        Check("No value on the page holds the journey entry", () => WaitForFs(() => !Value().Contains(JourneyEntry), 5));
    }

    private string Value() => WaitForId("EnvVars_Value", 2)?.AsTextBox().Text ?? "";

    private bool SelectScope(string scope)
    {
        var combo = WaitForId("EnvVars_Scope", 5)?.AsComboBox();
        if (combo is null) return false;
        combo.Select(scope);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => combo.SelectedItem?.Text == scope, 3);
    }

    /// <summary>
    /// Filters to PATHEXT and clicks the row that leaves, then waits for the entry editor — which only shows for
    /// a ';' list — so a machine where the pick went wrong fails here rather than at every editor step.
    /// </summary>
    private bool SelectPathExt()
    {
        if (!TypeInto("EnvVars_Filter", "PATHEXT")) return false;

        var list = WaitForId("EnvVars_List", 5);
        var row = list is null ? null : WaitFor(() => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)), 5);
        if (row is null) return false;
        row.Click();
        Wait.UntilInputIsProcessed();

        return WaitForFs(() => Value().Contains(".EXE", StringComparison.OrdinalIgnoreCase), 5)
            && WaitForId("EnvVars_AddEntry", 3) is not null;
    }
}
