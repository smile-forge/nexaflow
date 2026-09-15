using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font.UI;

/// <summary>
/// One-pass UI journey for the Font viewer. Opens the standalone compare tab via <c>--openTab Font</c>
/// (the feature has no default-ribbon button, only <c>CanBeContextItem</c>), then exercises every control
/// in a single pass — the "+ Add font" picker overlay, the installed-font search + pick, the preview
/// textbox, the size slider, the bold/italic/underline toggles, the glyph-map expander and the copy
/// actions — soft-asserting each so one gap doesn't hide the rest, and confirming content actually renders
/// (the added font's name, the details panel). A crash anywhere is caught by <c>crash.log</c> at teardown.
/// <para>Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[CoversNode("font")]
public class FontViewerJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Font";

    [TestMethod]
    [CoversNode("font-compare")]
    [CoversNode("font-picker")]
    public void Font_Controls_RespondInOnePass()
    {
        var view = WaitForId("FontView", 15);
        Assert.IsNotNull(view, "FontView did not open via --openTab Font.");

        // Blank compare mode: the empty-state hint and the add tile, no fonts yet.
        Check("Empty-state hint shown", () => WaitForName("No fonts yet", 6) is not null);
        CheckPresent("Add-font button", "FontAddButton");

        // The picker's footer, before anything is picked: Load from file raises a system file dialog, so it
        // is present only; Cancel has to close the overlay without adding anything.
        CheckDoes("Picker opens", "FontAddButton", () => WaitForId("FontPickerSearch", 10) is not null);
        CheckPresent("Picker load-from-file", "FontPickerLoadFile");
        CheckDoes("Picker cancel closes the overlay", "FontPickerCancel", () => WaitForGone("FontPickerSearch"));
        Check("Cancelling added nothing", () => WaitForName("No fonts yet", 3) is not null);

        // Add an installed font through the picker overlay (Arial ships with Windows).
        Check("Add installed font via picker", () => AddInstalledFont("Arial"));

        // Content actually rendered: the font appears and its details panel is up.
        Check("Added font is shown", () => WaitForName("Arial", 8) is not null);
        CheckPresent("Details panel", "FontDetailsPanel");
        Check("Empty-state hint gone", () =>
            MainWindow.FindFirstDescendant(cf => cf.ByName("No fonts yet")) is null);

        // Preview text + size slider drive the live previews.
        Check("Preview text edits", () =>
        {
            var box = WaitForId("FontPreviewInput", 5);
            if (box is null) return false;
            box.AsTextBox().Text = "Hamburgefonstiv 0123";
            return true;
        });
        Check("Size slider adjusts", () => SetSlider("FontSizeSlider", 40));

        // #1: at the maximum size the row must NOT overflow horizontally — the ✕ remove button has to
        // stay within the list's right edge (the preview wraps down instead of pushing it off-screen).
        Check("Max size keeps the ✕ within bounds (no horizontal overflow)", () =>
        {
            SetSlider("FontSizeSlider", 120);
            Thread.Sleep(250);
            var list = WaitForId("FontList", 5);
            var remove = WaitForId("FontRemoveButton", 5);
            if (list is null || remove is null) return false;
            // The bug pushed the ✕ hundreds of px off-screen; a few px of button/padding is fine.
            return remove.BoundingRectangle.Right <= list.BoundingRectangle.Right + 24;
        });

        // Glyph map builds its (lazy) grid on expand.
        CheckInvoke("Glyph map expander", "FontGlyphMap");

        // Paging: Arial has well over one 500-glyph page, so the pager is shown and starts on page one.
        // Each press is proven by the other button's enablement — Prev only comes alive off the first page.
        Check("Glyph map starts on its first page", () => WaitForId("FontGlyphPrevPage", 5) is { IsEnabled: false });
        CheckDoes("Glyph map next page", "FontGlyphNextPage",
                  () => WaitForFs(() => WaitForId("FontGlyphPrevPage", 1) is { IsEnabled: true }, 3));
        CheckDoes("Glyph map previous page", "FontGlyphPrevPage",
                  () => WaitForFs(() => WaitForId("FontGlyphPrevPage", 1) is { IsEnabled: false }, 3));

        // Copy name only writes the clipboard. Copy path is hidden for an installed font, which has no file —
        // FontFileOpenUiTests asserts it for a font opened from disk.
        CheckInvoke("Copy name", "FontCopyName");
        Check("Copy path is hidden for an installed font", () => WaitForId("FontCopyPath", 1) is null);

        AssertJourney();
    }

    /// <summary>Opens the picker, filters to <paramref name="search"/>, and picks the first matching row.</summary>
    private bool AddInstalledFont(string search)
    {
        var add = WaitForId("FontAddButton", 10);
        if (add is null) return false;
        add.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        var box = WaitForId("FontPickerSearch", 10);
        if (box is null) return false;
        box.AsTextBox().Text = search;
        Thread.Sleep(500);   // let SystemFontFamilies finish loading + the filter apply

        var list = WaitForId("FontPickerList", 8);
        if (list is null) return false;

        // One id on N rows (the row template); the first one after filtering is the match.
        var row = WaitFor(() => list.FindFirstDescendant(cf => cf.ByAutomationId("FontPickerRow")), 8);
        if (row is null) return false;

        row.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    private bool SetSlider(string automationId, double value)
    {
        var slider = WaitForId(automationId, 5);
        if (slider is null) return false;
        try { slider.Patterns.RangeValue.Pattern.SetValue(value); return true; }
        catch { try { slider.Click(); return true; } catch { return false; } }
    }
}
