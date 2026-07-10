using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass UI journey for the always-present <b>shell chrome</b> — the window frame that surrounds
/// every tab: the ribbon, the AI input bar, the breadcrumb bar, the tab strip, the Messages
/// (Notifications) inbox, the Ribbon editor, and the modal Options overlay. Amortises the ~20s app
/// launch by exercising every frame control in a single pass with <b>soft</b> checks, so one missing
/// or broken control doesn't abort coverage of the rest.
/// <para>
/// The chrome lives in <c>Nexaflow.Core</c>, but this journey drives the <i>running</i> app via FlaUI
/// (by AutomationId), so it needs no Core project reference and belongs alongside the other
/// FlaUI journeys in Tests.Features.
/// </para>
/// <para>
/// <b>Invoke vs present-only.</b> The three overlays that appear over the frame (Messages inbox,
/// Ribbon editor, Options) are each opened <i>and closed</i> by re-invoking the same chrome button:
/// their commands are pure toggles — <c>ToggleNotifications</c>, <c>ToggleEdit</c>, <c>ToggleOptions</c>
/// — so the second invoke reliably dismisses the overlay (Options is MODAL, so leaving it open would
/// block the rest of the pass). The ribbon, AI box, breadcrumb bar, tab strip and the default tab are
/// present-only (opening/closing extra tabs and splitting panes is already covered by Core's TabTests).
/// </para>
/// <para>
/// <b>Out of scope</b> (deliberately not driven here): the AI response overlay, plan-approval and
/// tool-approval surfaces — they require a live, non-deterministic LLM run. The window caption
/// min/max/close buttons are OS chrome, not app chrome. Toast/confirmation/prompt overlays are
/// transient and event-driven, so they have no always-present control to key off.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[NoCoverage("shell-chrome journey")]
public class ShellChromeJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void ShellChrome_Controls_RespondInOnePass()
    {
        // The shell always launches a default FileSystem tab; wait on its file browser so the frame
        // is fully realised before we probe the chrome around it.
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        // ── Always-present frame — present-only (these never open a modal) ──
        CheckPresent("Ribbon",          "RibbonControl");
        CheckPresent("AI input box",    "AiInputBox");
        CheckPresent("Breadcrumb bar",  "Chrome_BreadcrumbBar");
        CheckPresent("Tab strip",       "TabStrip");

        // The default FileSystem tab item lives in the strip (tab items are tagged TabItem_<PageKind>).
        CheckPresent("Default file-system tab", "TabItem_FileSystem");

        // ── Messages / Notifications inbox — open then dismiss via the same toggle ──
        // ToggleNotificationsCommand flips NotificationsOpen. The panel itself is a Border (no reliable
        // UIA peer in WPF), so we verify the toggle button responds rather than probing the Border.
        CheckInvoke("Messages button (open)",  "NotificationsButton");
        CheckInvoke("Messages button (close)", "NotificationsButton");

        // ── Ribbon editor — open via the ribbon Edit button, verify, close via the same toggle ──
        // Chrome_RibbonEdit fires ToggleEditCommand (IsEditOpen = !IsEditOpen); re-invoking closes it.
        CheckInvoke("Ribbon Edit button (open)",  "Chrome_RibbonEdit");
        CheckPresent("Ribbon editor overlay",     "Chrome_RibbonEditor");
        CheckInvoke("Ribbon Edit button (close)", "Chrome_RibbonEdit");

        // ── Options overlay (MODAL) — open, verify, close via the same toggle so it can't block ──
        // Chrome_OptionsButton fires ToggleOptionsCommand (OptionsOpen = !OptionsOpen); re-invoking closes it.
        CheckInvoke("Options button (open)",  "Chrome_OptionsButton");
        CheckPresent("Options overlay",       "Chrome_OptionsPanel");
        CheckInvoke("Options button (close)", "Chrome_OptionsButton");

        AssertJourney();
    }
}
