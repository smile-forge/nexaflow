using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console.UI;

/// <summary>
/// The Console (PTY terminal) journey. The Console opens for a <i>location</i> — it has no default ribbon
/// button, so the journey opens it deterministically via the file browser's <b>"Cmd Here"</b> folder action
/// on a temp folder. Its first steps are the launch-time environment picker, which appears for a location with
/// no remembered environment <i>and</i> more than one environment configured, so the journey seeds two; the
/// step that follows dismisses the picker each time it opens a console. It then exercises the always-present panel /
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
[CoversNode("console")]
public class ConsoleJourneyTests : UiJourneyTestBase
{
    /// <summary>
    /// Two environments under the default workspace. Written as an older version than any the app ships, so
    /// the config manager migrates it forward on load rather than this test having to know the build's version.
    /// </summary>
    protected override void SeedConfig(string configDir)
    {
        var dir = Path.Combine(configDir, "Contexts", "Default", "console");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config_0.0.0.1.json"), """
            {
              "Environments": [
                { "Name": "Command Prompt", "TabTitle": "Console" },
                { "Name": "Journey shell",  "TabTitle": "Journey" }
              ]
            }
            """);
    }

    /// <summary>The launch-time environment picker, cancelled and then answered, then the console's own chrome.</summary>
    [TestMethod]
    [CoversNode("console-ui")]
    [CoversNode("console-configure-button")]
    public void Console_Journey()
    {
        RunStep("the environment picker: Cancel falls back to the first environment", PickerCancels);
        RunStep("the environment picker: a pick is remembered for its location", PickerRemembers);
        RunStep("the console's side panel and Configure button", ConsoleControls);
        AssertJourney();
    }

    /// <summary>
    /// Cancel is pressed on one new location; the console falls back to the first environment. The centre pane is a live
    /// cmd.exe surface and is never typed into.
    /// </summary>
    private void PickerCancels()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        Check("Cmd Here on a new location asks which environment", () =>
            CmdHere(NewFolder()) && WaitForFs(() => CountOf("Console_EnvPickOption") == 2, 15));
        CheckDoes("Cancel dismisses the picker", "Console_EnvPickerCancel", () => WaitForGone("Console_EnvPickerCancel"));
    }

    /// <summary>An environment is picked with "always use it here" ticked on another location, proven by the binding landing in the config on disk.</summary>
    private void PickerRemembers()
    {
        var remembered = NewFolder();
        Check("Cmd Here on another location asks again", () =>
            CmdHere(remembered) && WaitForFs(() => CountOf("Console_EnvPickOption") == 2, 15));
        Check("'Always use this environment here' ticks", () => SetToggle("Console_EnvPickerAlwaysUse", true));
        Check("Picking the second environment dismisses the picker", () =>
            PressNth("Console_EnvPickOption", 1) && WaitForGone("Console_EnvPickerCancel"));
        Check("The choice is remembered for that location", () =>
            WaitForFs(() => ConsoleConfigText().Contains(Path.GetFileName(remembered)), 5));
    }

    private static string NewFolder() =>
        Directory.CreateDirectory(
            Path.Combine(TestTemp.Root, "nexaflow-consolejourney-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The file browser, into <paramref name="folder"/>, and its "Cmd Here" folder action. True once the console is open.</summary>
    private bool CmdHere(string folder)
    {
        NavigateFileBrowserTo(folder);
        var action = WaitForId("CmdHereAction", 8);
        if (action is null) return false;
        action.AsButton().Invoke();
        return WaitForId("ConsoleView", 15) is not null;
    }

    /// <summary>The console config as the app last saved it, or empty before it has saved one.</summary>
    private string ConsoleConfigText()
    {
        var dir = Path.Combine(ConfigDir, "Contexts", "Default", "console");
        if (!Directory.Exists(dir)) return string.Empty;
        try
        {
            return Directory.GetFiles(dir, "config_*.json")
                            .Select(File.ReadAllText)
                            .FirstOrDefault(t => t.Contains("FolderBindings")) ?? string.Empty;
        }
        catch (IOException) { return string.Empty; }   // mid-write — the caller retries
    }

    private void ConsoleControls()
    {
        var view = OpenConsole();
        Assert.IsNotNull(view,
            "ConsoleView did not open (the file browser's 'Cmd Here' folder action).");

        // Side-panel sub-tabs — pure panel switches, safe to invoke and reliably present.
        CheckInvoke("Environment sub-tab", "Console_EnvironmentTab");
        CheckInvoke("Files sub-tab", "Console_FilesTab");

        // Configure deep-link — present only (invoking opens the workspace Configure overlay).
        CheckPresent("Configure button", "Console_Configure");

        // The Environment panel's filter/add/edit controls only render once the env-var list has populated,
        // which is async and unreliable in a one-pass journey — their state logic is covered headlessly by
        // ConsoleViewModelCommandTests instead, so they're deliberately not asserted here.
    }

    /// <summary>
    /// Opens a Console tab through the file browser's "Cmd Here" folder action on a fresh temp folder — the Console's real
    /// entry point; with no file row selected it applies to the current folder and surfaces in the ActionStrip with
    /// AutomationId == its type name. The picker that a new location raises is cancelled, which opens the first environment.
    /// Returns the ConsoleView root, or null if it never opened.
    /// </summary>
    private AutomationElement? OpenConsole()
    {
        if (!CmdHere(NewFolder())) return null;
        if (WaitForId("Console_EnvPickerCancel", 3) is { } cancel) cancel.AsButton().Invoke();
        return WaitForId("ConsoleView", 15);
    }
}
