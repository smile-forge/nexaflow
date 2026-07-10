using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Scratchpad.UI;

/// <summary>
/// One-pass UI journey for the Scratchpad (virtual corkboard). Unlike the file-backed viewers, the
/// Scratchpad opens from a ribbon button (PageKind "Scratchpad", label "📌 Scratchpad" in
/// default-ribbon.json), so the journey uses <see cref="UITestBase.TryOpenTabWithElement"/> to click
/// ribbon buttons until the view root (<c>ScratchpadView</c>) appears, then exercises the always-present
/// toolbar / status-bar controls — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The "＋ New" and "⤡ Fit" toolbar buttons and the Recycle-Bin toggle are safe to invoke (they only
/// mutate the in-memory board / toggle an overlay panel). The bin-options split arrow and the zoom-label
/// both open a menu / popup, so they're checked for presence only — invoking them would leave a popup open
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

        // Bin-options split arrow and the zoom label open a menu / popup — presence only.
        CheckPresent("Recycle Bin options", "Scratchpad_BinOptions");
        CheckPresent("Zoom label", "Scratchpad_ZoomLabel");

        AssertJourney();
    }
}
