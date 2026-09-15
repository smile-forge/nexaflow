using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo.UI;

/// <summary>
/// One-pass UI journey for the Environment Variables page (PageKind "SystemEnvVars", root
/// <c>EnvVarsView</c>): refresh, the Add prompt, the scope picker and filter, and the PATH-style entry editor.
/// <para>
/// <b>Nothing here writes a variable.</b> Add only asks for a name, and its prompt is cancelled. Save and
/// Delete are present-checked and never pressed. The entry editor is exercised on PATHEXT — a ';' list on
/// every Windows machine — but only changes the value held in the editor. Each step is proven on that value,
/// and a final Refresh throws the edit away rather than trusting that nothing saved it.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("sysinfo-envvars")]
public class EnvironmentVariablesJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "SystemEnvVars";

    /// <summary>An entry no real PATHEXT carries, so finding it in the value proves the journey put it there.</summary>
    private const string JourneyEntry = ".NXJOURNEY";

    [TestMethod]
    public void EnvVars_Controls_RespondInOnePass()
    {
        var view = WaitForId("EnvVarsView", 15);
        Assert.IsNotNull(view, "EnvVarsView did not open via --openTab SystemEnvVars.");

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

        AssertJourney();
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
