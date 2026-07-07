using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
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
public class TextJourneyTests : UiJourneyTestBase
{
    [TestMethod]
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

        // Clipboard buttons in the read-only viewer: only Copy is shown (Cut/Paste are edit-only, hidden
        // via IsEditing). Copy binds to ApplicationCommands.Copy, which is disabled without a selection, so
        // assert it's present rather than invoking it — and verify the edit-only buttons are correctly hidden.
        CheckPresent("Copy button", "Text_Copy");
        Check("Cut/Paste hidden in the read-only viewer",
              () => WaitForId("Text_Cut", 1) is null && WaitForId("Text_Paste", 1) is null);

        // Monitoring toggle — safe to flip.
        CheckInvoke("Monitor toggle", "Text_Monitor");

        // Split-panel toggle — opens the split side panel (a safe UI toggle), checked last.
        CheckInvoke("Split-panel toggle", "Text_SplitToggle");

        AssertJourney();
    }
}
