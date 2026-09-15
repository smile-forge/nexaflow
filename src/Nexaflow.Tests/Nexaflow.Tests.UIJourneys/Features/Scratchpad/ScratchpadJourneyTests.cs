using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.Scratchpad.UI;

/// <summary>
/// One-pass UI journey for the Scratchpad (virtual corkboard). Unlike the file-backed viewers, the
/// Scratchpad opens from a ribbon button (PageKind "Scratchpad", label "📌 Scratchpad" in
/// default-ribbon.json), so the journey uses <see cref="UITestBase.TryOpenTabWithElement"/> to click
/// ribbon buttons until the view root (<c>ScratchpadView</c>) appears, then exercises the always-present
/// toolbar / status-bar controls, the zoom presets, the note mini-ribbon, and a note's trip through the
/// Recycle Bin — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The "＋ New" and "⤡ Fit" toolbar buttons and the Recycle-Bin toggle are safe to invoke (they only
/// mutate the in-memory board / toggle an overlay panel). The bin-options split arrow
/// opens a menu, so it is checked for presence only — invoking it would leave a menu open
/// over the rest of the pass.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("scratchpad")]
public class ScratchpadJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Scratchpad";

    [TestMethod]
    [CoversNode("scratchpad-ui")]
    public void Scratchpad_Controls_RespondInOnePass()
    {
        var view = WaitForId("ScratchpadView", 15);
        Assert.IsNotNull(view, "ScratchpadView did not open via --openTab Scratchpad.");

        // Toolbar — add + fit are safe to invoke; each only touches the in-memory board.
        CheckInvoke("Add note", "Scratchpad_AddNote");
        CheckInvoke("Zoom to fit", "Scratchpad_ZoomToFit");

        // Recycle-Bin toggle just shows/hides an overlay panel — invoke it, then toggle back off.
        CheckInvoke("Recycle Bin toggle", "Scratchpad_RecycleBin");
        CheckInvoke("Recycle Bin toggle (hide)", "Scratchpad_RecycleBin");

        // Bin-options split arrow opens a menu — presence only.
        CheckPresent("Recycle Bin options", "Scratchpad_BinOptions");

        // ── Zoom presets ──────────────────────────────────────────────────────────
        // The label is a TextBlock with a click handler, so the base class falls back to a real click to open the
        // popup. Each preset closes it, so the label is clicked again before the second pick.
        CheckInvoke("Zoom label (opens the preset popup)", "Scratchpad_ZoomLabel");
        CheckPresent("Zoom 50%",  "Scratchpad_Zoom50");
        CheckPresent("Zoom 75%",  "Scratchpad_Zoom75");
        CheckPresent("Zoom 125%", "Scratchpad_Zoom125");
        CheckDoes("Zoom 150% applies", "Scratchpad_Zoom150", () => WaitForFs(() => ZoomLabelReads("Scratchpad", "150%"), 3));
        CheckInvoke("Zoom label (reopen)", "Scratchpad_ZoomLabel");
        CheckDoes("Zoom 100% applies", "Scratchpad_Zoom100", () => WaitForFs(() => ZoomLabelReads("Scratchpad", "100%"), 3));

        // ── The note mini-ribbon ──────────────────────────────────────────────────
        // Opened by a right-click on a note header. Every button acts on that note and closes the ribbon, so each is
        // pressed on a freshly opened ribbon and proven by the ribbon going away. The colour, shape and z-order it sets
        // are drawn rather than exposed to UI Automation; ScratchpadViewModelTests asserts those.
        foreach (var id in RibbonButtons)
            Check($"Note ribbon: {id} acts and closes the ribbon", () => PressInNoteRibbon(id));

        // ── Remove, the bin, restore, delete ──────────────────────────────────────
        // The store honours NEXAFLOW_CONFIG_DIR, so this is the journey's own isolated board — and Delete is still
        // only pressed when the bin holds exactly the one note this journey put there.
        var notes = 0;
        Check("Removing a note takes it off the board", () =>
        {
            notes = NoteCount();
            return notes > 0 && PressFirst("Scratchpad_NoteRemove") && WaitForFs(() => NoteCount() == notes - 1, 3);
        });
        CheckInvoke("Recycle Bin (show)", "Scratchpad_RecycleBin");
        CheckDoes("Restore puts the note back on the board", "Scratchpad_BinRestore",
                  () => WaitForFs(() => NoteCount() == notes, 3));
        Check("Removing it again sends it back to the bin", () =>
            PressFirst("Scratchpad_NoteRemove") && WaitForFs(() => NoteCount() == notes - 1, 3));
        var binHoldsOnlyOurs = false;
        Check("The bin holds only the journey's note", () =>
            binHoldsOnlyOurs = WaitForFs(() => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("Scratchpad_BinDelete")).Length == 1, 3));
        if (binHoldsOnlyOurs)
            CheckDoes("Delete removes it from the bin for good", "Scratchpad_BinDelete", () => WaitForGone("Scratchpad_BinDelete"));
        CheckInvoke("Recycle Bin (hide)", "Scratchpad_RecycleBin");

        AssertJourney();
    }

    /// <summary>Every button on the note mini-ribbon, in the order the ribbon lays them out.</summary>
    private static readonly string[] RibbonButtons =
    [
        "Scratchpad_NoteColorYellow", "Scratchpad_NoteColorBlue", "Scratchpad_NoteColorGreen", "Scratchpad_NoteColorPink",
        "Scratchpad_NoteColorOrange", "Scratchpad_NoteColorPurple", "Scratchpad_NoteColorWhite",
        "Scratchpad_NoteShapeSquare", "Scratchpad_NoteShapeRounded", "Scratchpad_NoteShapeDiagonals", "Scratchpad_NoteShapeBubble",
        "Scratchpad_NoteFront", "Scratchpad_NoteBack",
    ];

    /// <summary>Notes on the board — one remove button each.</summary>
    private int NoteCount() => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("Scratchpad_NoteRemove")).Length;

    private bool PressFirst(string automationId)
    {
        var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        if (el is null) return false;
        el.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// Right-clicks the first note's header. The header is drawn inside the note with no automation peer of its
    /// own, so it is found through the note's remove button, which sits at the header's right end: a point a
    /// little to its left is header.
    /// </summary>
    private bool OpenNoteRibbon()
    {
        var remove = WaitForId("Scratchpad_NoteRemove", 5);
        if (remove is null) return false;
        var r = remove.BoundingRectangle;
        Mouse.RightClick(new System.Drawing.Point(r.Left - 40, r.Top + r.Height / 2));
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => FindInAppWindows("Scratchpad_NoteFront") is { IsOffscreen: false }, 3);
    }

    private bool PressInNoteRibbon(string automationId)
    {
        if (!OpenNoteRibbon()) return false;
        var button = FindInAppWindows(automationId);
        if (button is null) return false;
        button.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => FindInAppWindows(automationId) is not { IsOffscreen: false }, 3);
    }
}
