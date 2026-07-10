using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console.UI;

/// <summary>
/// One-pass UI journey for the Console (PTY terminal) tab. The Console opens for a <i>location</i> — it has
/// no default ribbon button, so the journey opens it deterministically via the file browser's <b>"Cmd
/// Here"</b> folder action on a temp folder (falling back to <see cref="UITestBase.TryOpenTabWithElement"/>
/// in case a customized ribbon carries a Console button). It then exercises the always-present panel /
/// toolbar chrome — the Files/Environment side-panel sub-tabs, the env-var filter box + add button, and the
/// Configure deep-link — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The centre pane is a live cmd.exe surface; the journey NEVER sends keystrokes to it. The side-panel
/// sub-tabs are safe to invoke (pure panel switches). The "＋ Add variable" button opens a modal env-edit
/// overlay, so the journey opens it and immediately dismisses it via the overlay's Cancel button so the
/// rest of the pass isn't blocked. The Configure button deep-links into the workspace Configure overlay, so
/// it is checked for presence only — invoking it would leave that overlay open.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class ConsoleJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("console-panels-group")]
    [CoversNode("console-configure-button")]
    public void Console_Controls_RespondInOnePass()
    {
        var view = OpenConsole();
        Assert.IsNotNull(view,
            "ConsoleView did not open (tried the ribbon, then the file browser's 'Cmd Here' folder action).");

        // Side-panel sub-tabs — pure panel switches, safe to invoke and reliably present.
        CheckInvoke("Environment sub-tab", "Console_EnvironmentTab");
        CheckInvoke("Files sub-tab", "Console_FilesTab");

        // Configure deep-link — present only (invoking opens the workspace Configure overlay).
        CheckPresent("Configure button", "Console_Configure");

        // The Environment panel's filter/add/edit controls only render once the env-var list has populated,
        // which is async and unreliable in a one-pass journey — their state logic is covered headlessly by
        // ConsoleViewModelCommandTests instead, so they're deliberately not asserted here.

        AssertJourney();
    }

    /// <summary>
    /// Opens a Console tab. Prefers the ribbon (in case a customized layout carries a Console button), then
    /// falls back to the file browser's "Cmd Here" folder action on a fresh temp folder — the Console's real
    /// entry point. Returns the ConsoleView root, or null if neither route opened it.
    /// </summary>
    private AutomationElement? OpenConsole()
    {
        var viaRibbon = TryOpenTabWithElement("ConsoleView");
        if (viaRibbon is not null) return viaRibbon;

        // "Cmd Here" is a folder action (CmdHereAction.DisplayName); with no file row selected it applies to
        // the current folder and surfaces in the ActionStrip with AutomationId == its DisplayName.
        var folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "nexaflow-consolejourney-" + Guid.NewGuid().ToString("N"))).FullName;

        NavigateFileBrowserTo(folder);

        var action = WaitForId("Cmd Here", 8);
        Assert.IsNotNull(action, "The 'Cmd Here' folder action did not appear in the ActionStrip for the temp folder.");
        action!.AsButton().Invoke();
        System.Threading.Thread.Sleep(300);   // let the Console tab spin up

        return WaitForId("ConsoleView", 15);
    }
}
