using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.Json.UI;

/// <summary>
/// One-pass UI journey for the JSON viewer: opens a sample file via the explicit <b>"ShowJsonAction"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — the view-mode
/// toggles (tree / text / table), a tree row's expander, the breadcrumb trail a selection builds, and the
/// Format button — soft-asserting each so a single gap doesn't hide the rest. Format re-indents the doc,
/// which marks it modified and reveals the Save button, so Save is checked last.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("json")]
[CoversNode("json-ui")]
public class JsonJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Json_Toolbar_RespondsInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("json").First());
        var view = OpenFileVia(TestSampleData.Path("json"), file, "ShowJsonAction", "JsonView");
        Assert.IsNotNull(view, "JsonView did not open via the 'As Json' action.");

        // View-mode toggles — always present in the toolbar. Tree is pressed so the rows below are the tree's.
        CheckInvoke("Tree mode toggle",  "Json_TreeMode");
        CheckPresent("Text mode toggle",  "Json_TextMode");
        CheckPresent("Table mode toggle", "Json_TableMode");

        // ── Tree rows and the breadcrumb trail ────────────────────────────────
        // A row's ▶ toggles its children; pressed twice so the tree is left as it was.
        Check("A row's expander toggles, and back", () =>
            WaitForFs(() => CountOf("Json_RowExpand") > 0, 5) && PressNth("Json_RowExpand", 0) && PressNth("Json_RowExpand", 0));
        // Selecting a row builds its path as breadcrumbs, from "$" down; the "$" crumb selects the root.
        Check("Selecting a row builds its breadcrumbs", () =>
            SelectRow("DisplayList", 1) && WaitForFs(() => CountOf("Json_Breadcrumb") > 1, 3));
        Check("The root crumb selects the root, shortening the trail", () =>
            PressNth("Json_Breadcrumb", 0) && WaitForFs(() => CountOf("Json_Breadcrumb") == 1, 3));

        // Format re-indents the document → marks it modified → reveals the Save button.
        CheckInvoke("Format", "Json_Format");
        CheckPresent("Save (visible after Format modifies)", "Json_Save");

        AssertJourney();
    }

    /// <summary>Selects the list's <paramref name="index"/>th realised row.</summary>
    private bool SelectRow(string listId, int index)
    {
        var rows = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(listId))
                             ?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
        if (rows is null || rows.Length <= index) return false;
        rows[index].Patterns.SelectionItem.PatternOrDefault?.Select();
        Wait.UntilInputIsProcessed();
        return true;
    }
}
