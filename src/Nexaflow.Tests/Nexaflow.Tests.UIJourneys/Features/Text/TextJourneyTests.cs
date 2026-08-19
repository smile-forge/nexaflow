using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Text.UI;

/// <summary>
/// One-pass UI journey for the Text viewer: opens a text sample via the explicit <b>"As Text"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — the
/// line-number / word-wrap display toggles, the clipboard buttons, the monitoring toggle and the
/// split-panel toggle — soft-asserting each so a single gap doesn't hide the rest. The editor is
/// read-only, so the clipboard commands and toggles do not mutate the sample file.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("text-viewer")]
public class TextJourneyTests : UiJourneyTestBase
{
    // One app-launch journey exercising the interactive UI as a whole — the integration test registered at
    // the feature's UI node. Individual controls are covered by VM unit tests at their leaf nodes.
    [TestMethod]
    [CoversNode("ui")]
    public void Text_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("text").First());
        var view = OpenFileVia(TestSampleData.Path("text"), file, "As Text", "TextView");
        Assert.IsNotNull(view, "TextView did not open via the 'As Text' action.");

        // Encoding selector — present in the toolbar.
        CheckPresent("Encoding selector", "Text_Encoding");

        // Display toggles — safe, view-only.
        CheckInvoke("Line-numbers toggle", "Text_LineNumbers");
        CheckInvoke("Word-wrap toggle",    "Text_WordWrap");

        // Zoom % and Go-to-line live in the toolbar/footer regardless of edit state.
        CheckPresent("Zoom label",   "Text_ZoomLabel");
        CheckPresent("Go-to-line button", "Text_GoToLine");

        // Find split button: the ▾ menu is present; the main button toggles the bar open. Its controls
        // should all be present, then it closes. (The bar's Border host isn't in the UIA control tree,
        // so gate on the find box itself.)
        CheckPresent("Find split-menu button", "Text_FindMenu");
        CheckInvoke("Find button (toggles bar open)", "Text_Find");
        Check("Find bar opens", () => WaitForId("Text_FindBox", 3) is not null);
        CheckPresent("Find box",        "Text_FindBox");
        CheckPresent("Match-case toggle", "Text_MatchCase");
        CheckPresent("Regex toggle",      "Text_UseRegex");
        CheckInvoke("Close find bar", "Text_CloseFind");

        // Read-only viewer: only Copy is shown (Cut/Paste are edit-only, hidden via IsEditing). Copy binds to
        // ApplicationCommands.Copy — disabled without a selection — so assert present, and that the edit-only
        // buttons are correctly hidden.
        CheckPresent("Copy button", "Text_Copy");
        Check("Cut/Paste hidden in the read-only viewer",
              () => WaitForId("Text_Cut", 1) is null && WaitForId("Text_Paste", 1) is null);

        // Monitoring toggle — safe to flip.
        CheckInvoke("Monitor toggle", "Text_Monitor");

        // Split-panel toggle — opens the split side panel (a safe UI toggle).
        CheckInvoke("Split-panel toggle", "Text_SplitToggle");

        // Edit toggle → editing mode, which reveals the edit-only Save / Cut / Paste toolbar buttons.
        CheckInvoke("Edit toggle", "Text_EditToggle");
        Check("Save appears in edit mode",  () => WaitForId("Text_Save",  3) is not null);
        Check("Cut appears in edit mode",   () => WaitForId("Text_Cut",   3) is not null);
        Check("Paste appears in edit mode", () => WaitForId("Text_Paste", 3) is not null);
        Check("Undo appears in edit mode",  () => WaitForId("Text_Undo",  3) is not null);
        Check("Redo appears in edit mode",  () => WaitForId("Text_Redo",  3) is not null);
        CheckPresent("Save button (edit mode)", "Text_Save");

        AssertJourney();
    }
}
