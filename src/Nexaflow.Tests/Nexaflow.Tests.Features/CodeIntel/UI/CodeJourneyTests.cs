using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel.UI;

/// <summary>
/// The one UI journey for the "As Code" page — the integration test registered at the feature's UI node.
/// It opens a structured C# sample via the explicit <b>"As Code"</b> ActionStrip button and drives every
/// interactive surface in a single pass: the editor toolbar (encoding / EOL selectors, line-number toggle,
/// Save), the floating command panel, the status bar's line-operations button, and the code-map collapse /
/// reopen pair. Each check is soft, so one gap doesn't hide the rest.
///
/// Individual controls are asserted by view-model unit tests at their own leaf nodes; this proves the
/// wiring holds end-to-end. Nothing here mutates the sample: the toggles are view-only and Save is
/// presence-checked (a clean buffer leaves it disabled).
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("code")]
public class CodeJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("code-ui")]
    public void Code_Controls_RespondInOnePass()
    {
        // hello.cs has real structure, so the code-map panel opens and its toggle is on-screen.
        var file = "hello.cs";
        Check("code sample exists", () => TestSampleData.Files("code").Any(p => Path.GetFileName(p) == file));

        var view = OpenFileVia(TestSampleData.Path("code"), file, "As Code", "CodeView");
        Assert.IsNotNull(view, "CodeView did not open via the 'As Code' action.");

        // ── Editor toolbar ────────────────────────────────────────────────────
        CheckPresent("Encoding selector", "Editor_Encoding");
        CheckPresent("Line-ending selector", "Editor_Eol");
        CheckInvoke("Line-numbers toggle", "Editor_LineNumbers");   // view-only, safe to flip
        CheckInvoke("Line-numbers toggle (back)", "Editor_LineNumbers");

        // Save is disabled on a freshly-opened, unmodified buffer — assert that rather than clicking it,
        // so the journey never writes to the shared sample file.
        var save = CheckPresent("Save", "Editor_Save");
        Check("Save is disabled while the buffer is clean", () => save is not null && !save.IsEnabled);

        // ── Status bar ────────────────────────────────────────────────────────
        CheckPresent("File size", "Editor_FileSize");
        CheckPresent("Line operations button", "Editor_LineCommands");

        // ── Floating command panel ────────────────────────────────────────────
        // Encode/Decode are selection-scoped and hidden without a selection; Checksum is always offered.
        CheckPresent("Checksum command group", "Editor_Cmd_Checksum");

        // ── Code map ──────────────────────────────────────────────────────────
        CheckDoes("Code map: collapse", "Code_MapToggle", () => WaitForId("Code_MapReopen", 3) is not null);
        CheckDoes("Code map: reopen", "Code_MapReopen", () => WaitForId("Code_MapToggle", 3) is not null);

        AssertJourney();
    }
}
