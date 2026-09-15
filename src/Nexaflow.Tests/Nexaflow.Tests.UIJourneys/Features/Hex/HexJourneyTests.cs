using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.Hex.UI;

/// <summary>
/// One-pass UI journey for the Hex (binary) viewer: opens a binary sample via the explicit <b>"As Hex"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar controls — edit-mode
/// toggles, goto, undo/redo, save, and the evaluate-pane toggle — soft-asserting each so a single gap doesn't
/// hide the rest. The buffer opens read-only; the one edit made (to reach the save menu) is undone, and Save is never pressed, so the sample file is never written.
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

        // Save options — the split button's drop half, bound to IsModified, so dead on a clean buffer. Reaching
        // its menu takes one real edit; Undo takes it back, and neither menu entry is ever pressed, so the sample
        // on disk is never written.
        Check("Save options is disabled on a clean buffer", () => WaitForId("Hex_SaveOptions", 5) is { IsEnabled: false });
        Check("An overwrite edit makes the buffer dirty", MakeOneEdit);
        CheckDoes("Save options opens the save menu", "Hex_SaveOptions",
                  () => WaitForFs(() => FindInAppWindows("Hex_SaveMenuSave") is not null, 3));
        Check("The save menu offers Save As", () => FindInAppWindows("Hex_SaveMenuSaveAs") is not null);
        Check("The save menu closes", CloseSaveMenu);
        CheckDoes("Undo reverts the edit", "Hex_Undo",
                  () => WaitForFs(() => WaitForId("Hex_SaveOptions", 1) is { IsEnabled: false }, 3));
        CheckInvoke("Back to read-only", "Hex_ModeReadOnly");

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
        Check("Zoom starts at 100%", () => ZoomLabelReads("Hex", "100%"));
        CheckDoes("Zoom 120% applies", "Hex_Zoom120", () => ZoomLabelReads("Hex", "120%"));

        CheckInvoke("Zoom label (reopen)", "Hex_ZoomLabel");
        CheckDoes("Zoom 100% restores the default", "Hex_Zoom100", () => ZoomLabelReads("Hex", "100%"));

        AssertJourney();
    }

    /// <summary>
    /// Switches to overwrite, clicks into the byte grid and types one byte. The grid is a custom-drawn panel with
    /// no automation peer, so it is clicked by position: a quarter of the way across the view, just under the
    /// toolbar — any byte will do, since a click in the address column lands on its row's first byte.
    /// </summary>
    private bool MakeOneEdit()
    {
        var view = WaitForId("HexView", 5);
        var gotoBox = WaitForId("Hex_Goto", 5);
        var overwrite = WaitForId("Hex_ModeOverwrite", 5);
        if (view is null || gotoBox is null || overwrite is null) return false;

        overwrite.Click();
        Wait.UntilInputIsProcessed();

        var area = view.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(area.Left + area.Width / 4, gotoBox.BoundingRectangle.Bottom + 40));
        Wait.UntilInputIsProcessed();

        Keyboard.Type("00");
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => WaitForId("Hex_SaveOptions", 1) is { IsEnabled: true }, 3);
    }

    /// <summary>
    /// Closes the save menu by unchecking its toggle through the Toggle pattern. A mouse click would not do: the
    /// popup is StaysOpen=False, so a click on the toggle closes it on the way down and re-opens it on the way up.
    /// </summary>
    private bool CloseSaveMenu()
    {
        var toggle = WaitForId("Hex_SaveOptions", 2);
        var pattern = toggle?.Patterns.Toggle.PatternOrDefault;
        if (pattern is not null && pattern.ToggleState.Value == ToggleState.On) pattern.Toggle();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => FindInAppWindows("Hex_SaveMenuSave") is null, 3);
    }
}
