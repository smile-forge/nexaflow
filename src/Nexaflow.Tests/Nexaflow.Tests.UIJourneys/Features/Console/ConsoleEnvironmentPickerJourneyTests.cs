using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console.UI;

/// <summary>
/// UI journey for the Console's launch-time environment picker. The picker appears only when a console is
/// opened for a location with no remembered environment <i>and</i> more than one is configured — a fresh config
/// has one — so this journey seeds two, and <see cref="ConsoleJourneyTests"/> keeps the one-environment default
/// so its own pass is never interrupted by the picker.
/// <para>
/// Cancel is pressed on one new location; an environment is picked with "always use it here" ticked on another,
/// which is proven by the binding landing in the journey's throwaway config on disk. The centre pane is a live
/// cmd.exe surface and is never typed into.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("console")]
public class ConsoleEnvironmentPickerJourneyTests : UiJourneyTestBase
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

    [TestMethod]
    [CoversNode("console-ui")]
    public void Console_EnvironmentPicker_CancelsAndRemembers()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        // ── Cancel: the console falls back to the first environment ───────────────
        Check("Cmd Here on a new location asks which environment", () =>
            CmdHere(NewFolder()) && WaitForFs(() => CountOf("Console_EnvPickOption") == 2, 15));
        CheckDoes("Cancel dismisses the picker", "Console_EnvPickerCancel", () => WaitForGone("Console_EnvPickerCancel"));

        // ── Pick, remembered for the location ─────────────────────────────────────
        var remembered = NewFolder();
        Check("Cmd Here on another location asks again", () =>
            CmdHere(remembered) && WaitForFs(() => CountOf("Console_EnvPickOption") == 2, 15));
        Check("'Always use this environment here' ticks", () => SetToggle("Console_EnvPickerAlwaysUse", true));
        Check("Picking the second environment dismisses the picker", () =>
            PressNth("Console_EnvPickOption", 1) && WaitForGone("Console_EnvPickerCancel"));
        Check("The choice is remembered for that location", () =>
            WaitForFs(() => ConsoleConfigText().Contains(Path.GetFileName(remembered)), 5));

        AssertJourney();
    }

    private static string NewFolder() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "nexaflow-envpicker-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Back to the file browser, into <paramref name="folder"/>, and its "Cmd Here" folder action.</summary>
    private bool CmdHere(string folder)
    {
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_FileSystem"))?.Click();
        NavigateFileBrowserTo(folder);
        var action = WaitForId("Cmd Here", 8);
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
}
