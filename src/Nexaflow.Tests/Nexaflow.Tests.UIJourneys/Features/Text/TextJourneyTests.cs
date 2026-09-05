using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
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
        // Named, not Files("text").First(). That set leads with empty.txt, and whichever file the enumeration
        // happens to yield decides whether half of this journey tests anything: a search over an empty file
        // matches nothing, so IsSearchActive stays false and find next/previous are reported as broken
        // buttons when they are behaving perfectly. Naming the file makes the term below verifiable.
        const string sample = "short_utf8_nobom.txt";
        var view = OpenFileVia(TestSampleData.Path("text"), sample, "As Text", "TextView");
        Assert.IsNotNull(view, "TextView did not open via the 'As Text' action.");

        // Encoding selector — present in the toolbar.
        CheckPresent("Encoding selector", "Text_Encoding");

        // Display toggles — safe, view-only.
        CheckInvoke("Line-numbers toggle", "Text_LineNumbers");
        CheckInvoke("Word-wrap toggle",    "Text_WordWrap");

        // Zoom % and Go-to-line live in the toolbar/footer regardless of edit state. The label is a TextBlock
        // with a MouseLeftButtonUp handler rather than a Button, so it supports no Invoke pattern — the base
        // class falls back to a real click, which is what opens the popup. The presets only exist in the UIA
        // tree while that popup is open, so they are reachable from here and nowhere else in this journey.
        CheckPresent("Go-to-line button", "Text_GoToLine");
        CheckInvoke("Zoom label (opens the preset popup)", "Text_ZoomLabel");
        Check("Zoom popup opens", () => WaitForId("Text_Zoom100", 3) is not null);
        CheckPresent("Zoom 80%",  "Text_Zoom80");
        CheckPresent("Zoom 90%",  "Text_Zoom90");
        CheckPresent("Zoom 110%", "Text_Zoom110");
        CheckPresent("Zoom 130%", "Text_Zoom130");

        // Pick one and require it to land: a preset that opens a popup and changes nothing is the failure a
        // present-only check cannot see. 120% is chosen because it is not the default, so the label has to move.
        CheckDoes("Zoom 120% applies", "Text_Zoom120",
                  () => WaitForId("Text_ZoomLabel", 3)?.Name?.Contains("120") == true);

        // Back to 100% so the rest of the journey runs at the size everything else assumes. The popup has to
        // be reopened first — choosing a preset closes it.
        CheckInvoke("Zoom label (reopen)", "Text_ZoomLabel");
        CheckInvoke("Zoom 100% restores the default", "Text_Zoom100");

        // Find split button: the ▾ menu is present; the main button toggles the bar open. Its controls
        // should all be present, then it closes. (The bar's Border host isn't in the UIA control tree,
        // so gate on the find box itself.)
        CheckPresent("Find split-menu button", "Text_FindMenu");
        CheckInvoke("Find button (toggles bar open)", "Text_Find");
        Check("Find bar opens", () => WaitForId("Text_FindBox", 3) is not null);
        CheckPresent("Find box",        "Text_FindBox");
        CheckPresent("Match-case toggle", "Text_MatchCase");
        CheckPresent("Regex toggle",      "Text_UseRegex");

        // Find next/previous bind IsEnabled to IsSearchActive, so on an empty box they are present and
        // disabled — invoking one there would only prove that a dead button does nothing. Typing a term the
        // sample certainly contains is what makes them live, and then they are worth pressing.
        CheckPresent("Find previous (no search yet)", "Text_FindPrevious");
        CheckPresent("Find next (no search yet)",     "Text_FindNext");

        // The search runs off FindText changing, debounced — Enter is a shortcut, not the trigger. Both
        // buttons bind IsEnabled to IsSearchActive, which only goes true once a run has found something, so
        // the term has to be one the sample really contains.
        Check("Typing a term activates the search", () =>
        {
            var box = WaitForId("Text_FindBox", 3);
            if (box is null) return false;
            box.AsTextBox().Text = "quick";                 // in the sample twice, so next/previous both mean something
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(1200);            // the debounce, then an async pass over the file
            return WaitForId("Text_FindNext", 5)?.IsEnabled == true;
        });

        CheckInvoke("Find next",     "Text_FindNext");
        CheckInvoke("Find previous", "Text_FindPrevious");

        CheckInvoke("Close find bar", "Text_CloseFind");

        // Read-only viewer: only Copy is shown (Cut/Paste are edit-only, hidden via IsEditing). Copy binds to
        // ApplicationCommands.Copy — disabled without a selection — so assert present, and that the edit-only
        // buttons are correctly hidden.
        CheckPresent("Copy button", "Text_Copy");
        Check("Cut/Paste hidden in the read-only viewer",
              () => WaitForId("Text_Cut", 1) is null && WaitForId("Text_Paste", 1) is null);

        // Monitoring toggle — safe to flip.
        CheckInvoke("Monitor toggle", "Text_Monitor");

        // Split-panel toggle — opens the split side panel (a safe UI toggle). Its own two buttons live inside
        // that panel, so they are only reachable once it is open, and the ✕ is pressed last because it shuts
        // the panel that hosts it.
        CheckDoes("Split-panel toggle opens the panel", "Text_SplitToggle",
                  () => WaitForId("Text_SplitNow", 3) is not null);

        // Present, not pressed. "Split now" writes the file out as sibling parts on disk — see
        // TextViewModelTests.Split_QueuesTask_ThatSplitsFileIntoSiblingParts — and a journey that litters the
        // shared sample directory every run is a worse problem than the one it was checking for. Its command
        // is unit-tested where the output can be inspected and thrown away.
        CheckPresent("Split now", "Text_SplitNow");

        CheckDoes("Split panel closes", "Text_SplitClose",
                  () => WaitForId("Text_SplitNow", 2) is null);

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
