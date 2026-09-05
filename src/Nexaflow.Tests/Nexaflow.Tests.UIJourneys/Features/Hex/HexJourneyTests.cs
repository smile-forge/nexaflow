using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Hex.UI;

/// <summary>
/// One-pass UI journey for the Hex (binary) viewer: opens a binary sample via the explicit <b>"As Hex"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar controls — edit-mode
/// toggles, goto, undo/redo, save, and the evaluate-pane toggle — soft-asserting each so a single gap doesn't
/// hide the rest. The buffer opens read-only, so invoking these controls does not mutate the sample file.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("hex")]
[CoversNode("hex-ui")]
public class HexJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Hex_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("binary").First());
        var view = OpenFileVia(TestSampleData.Path("binary"), file, "As Hex", "HexView");
        Assert.IsNotNull(view, "HexView did not open via the 'As Hex' action.");

        // Goto — jump to a hex offset (safe, read-only navigation).
        Check("Goto box present", () => WaitForId("Hex_Goto", 5) is not null);
        CheckInvoke("Goto go button", "Hex_GotoGo");

        // Edit-mode toggles (opens read-only; flip through and back to read-only).
        CheckInvoke("Insert mode toggle", "Hex_EditMode");
        CheckInvoke("Overwrite mode toggle", "Hex_ModeOverwrite");
        CheckInvoke("Read-only mode toggle", "Hex_ModeReadOnly");

        // Undo / Redo — present but *disabled* on a freshly-opened clean buffer (nothing to undo/redo),
        // so present-check only; invoking a disabled button would throw ElementNotEnabledException.
        CheckPresent("Undo", "Hex_Undo");
        CheckPresent("Redo", "Hex_Redo");

        // Save — present (disabled on a clean buffer; present-check only to avoid any write).
        CheckPresent("Save", "Hex_Save");

        // Evaluate-pane toggle.
        CheckInvoke("Evaluate pane toggle", "Hex_EvalPane");

        // Zoom, in the status bar. The label is a TextBlock with a MouseLeftButtonUp handler rather than a
        // Button, so it supports no Invoke pattern — the base class falls back to a real click, which is what
        // opens the popup. The presets only exist in the UIA tree while it is open.
        CheckInvoke("Zoom label (opens the preset popup)", "Hex_ZoomLabel");
        Check("Zoom popup opens", () => WaitForId("Hex_Zoom100", 3) is not null);
        CheckPresent("Zoom 80%",  "Hex_Zoom80");
        CheckPresent("Zoom 130%", "Hex_Zoom130");

        // Require a preset to land: zooming re-measures the cell, so this is also the check that a resized grid
        // still renders rather than throwing on the way.
        CheckDoes("Zoom 120% applies", "Hex_Zoom120",
                  () => WaitForId("Hex_ZoomLabel", 3)?.Name?.Contains("120") == true);
        CheckInvoke("Zoom label (reopen)", "Hex_ZoomLabel");
        CheckInvoke("Zoom 100% restores the default", "Hex_Zoom100");

        AssertJourney();
    }
}
