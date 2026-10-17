using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass UI journey for the <b>workspace</b> configure panel — the per-workspace counterpart of Options,
/// reached from the workspace selector's right-click menu rather than the chrome's Options button.
/// <para>
/// OptionsJourneyTests covers the global sections; everything scoped to a workspace (its identity, its
/// startup tabs, the AI ability grid, and every <c>[WorkspaceScopedConfig]</c> editor such as Console and
/// Projects) lives in this panel instead, and was undriven. It shares the Options overlay's section
/// scrolling, which is why it derives from <see cref="OptionsOverlayJourney"/>.
/// </para>
/// <para>
/// <b>Never pressed:</b> a workspace in the switcher (switching — even to this workspace — reconfigures the runtime
/// and closes every tab), the add-column panel's Add (it needs a configured provider and model), and the pickers'
/// OK (it needs something picked). The app runs against a throwaway config dir, so what the panel applies on the
/// way out is discarded with it.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[NoCoverage("workspace configure journey")]
public class WorkspaceConfigJourneyTests : OptionsOverlayJourney
{
    /// <summary>An AI folder of its own, empty and thrown away with the config dir: the Local column's models are never here, so
    /// the grid offers them for download whatever this machine holds.</summary>
    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = Path.Combine(ConfigDir, "ai") };

    [TestMethod]
    public void WorkspaceConfig_SectionsAndEditors_RespondInOnePass()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        // ── The selector ──────────────────────────────────────────────────────────────
        Check("The workspace selector opens the switcher", () =>
            PressNth("Workspace_Selector", 0) && WaitFor(() => FindInAppWindows("Workspace_SwitchItem"), 5) is not null);

        // A right-click is a click outside the switcher, so it closes that popup on the way to the menu.
        // "Use Tabset as Default" saves the open tabs as the startup set, which gives Default tabs a row to remove.
        Check("Use Tabset as Default captures the open tabs", () => FromSelectorMenu("Use Tabset as Default"));
        Check("Configure workspace… opens the panel", () =>
            FromSelectorMenu("Configure workspace") && WaitForId("WorkspacePanel_Close", 10) is not null);

        var sections = WaitForId("WorkspacePanel_SectionList", 5);
        Assert.IsNotNull(sections, "The workspace panel's section list did not appear.");

        // ── Identity ──────────────────────────────────────────────────────────────────
        SelectSection(sections!, "Workspace", "identity_Swatch");
        Check("A colour swatch recolours the workspace", () => PressNth("identity_Swatch", 0));
        Check("Export opens the in-app folder picker", () =>
            PressNth("identity_Export", 0) && WaitFor(() => FindInAppWindows("FolderBrowser_Cancel"), 10) is not null);
        Check("The folder picker offers OK", () => FindInAppWindows("FolderBrowser_Ok") is not null);
        Check("Cancel closes the folder picker", () => PressInDialog("FolderBrowser_Cancel"));

        // ── Default tabs: the set just captured ───────────────────────────────────────
        SelectSection(sections!, "Default tabs", "DefaultTabs_Remove");
        var tabs = 0;
        Check("The captured tabset is listed", () => WaitForFs(() => (tabs = CountOf("DefaultTabs_Remove")) > 0, 3));
        Check("The last-session offer can be switched off for this workspace", () =>
            SetToggle("DefaultTabs_NoSessionRestore", true));
        Check("... and back on", () => SetToggle("DefaultTabs_NoSessionRestore", false));
        Check("✕ drops a tab from it", () =>
            PressNth("DefaultTabs_Remove", 0) && WaitForFs(() => CountOf("DefaultTabs_Remove") == tabs - 1, 3));

        // ── AI: the ability grid ──────────────────────────────────────────────────────
        SelectSection(sections!, "AI", "AiGrid_Cell");
        Check("The ability grid has cells", () => WaitForFs(() => CountOf("AiGrid_Cell") > 0, 3));
        // The Local column comes first; a cell of it with nothing to use is greyed, and says why.
        var cells = MainWindow.FindAllDescendants(cf => cf.ByAutomationId("AiGrid_Cell"));
        Check("A greyed cell says why it cannot be used", () => cells.Where(c => !c.IsEnabled).All(c => c.HelpText is { Length: > 0 }));
        Check("A cell selects", () => PressNth("AiGrid_Cell", Array.FindIndex(cells, c => c.IsEnabled)));
        // The Local column starts on wherever this machine runs a model; the journey's AI folder is empty, so the grid offers
        // to download them all — never pressed, as that would fetch gigabytes.
        CheckExists("The Local column's models are offered for download", "AiGrid_DownloadLocal");
        Check("+ opens the add-column panel", () =>
            PressNth("AiGrid_AddColumn", 0) && WaitForFs(() => Exists("AiGrid_CancelAdd"), 3));
        CheckExists("Add column", "AiGrid_ConfirmAdd");
        Check("Cancel closes the add-column panel", () =>
            PressNth("AiGrid_CancelAdd", 0) && WaitForFs(() => Exists("AiGrid_AddColumn"), 3));

        // ── Local: a model for each job on this machine, and what is on its disk ──────
        // A journey downloads nothing and deletes nothing — the models are this machine's — so neither list is acted on.
        SelectSection(sections!, "Local", "LocalModels_Choice");
        Check("Each job this machine runs a model for has its list — speech to text among them, on Nexaflow's own Whisper",
              () => WaitForFs(() => CountOf("LocalModels_Choice") >= 2, 3));
        Check("The tools are listed beneath the jobs — the picture tools offered for download where they are not here",
              () => WaitForFs(() => Exists("LocalModels_Tool") && (WaitForId("LocalModels_ToolDownload", 1) is not { } download || download.IsEnabled), 3));

        SelectSection(sections!, "Downloaded models", "DownloadedModels_List");
        Check("What is on disk is listed", () => Exists("DownloadedModels_List"));
        Check("Each listed model's Delete says what it does, or who still uses it",
              () => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("DownloadedModels_Delete")).All(b => b.HelpText is { Length: > 0 }));

        // ── Console: environments ─────────────────────────────────────────────────────
        SelectSection(sections!, "Console", "ConsoleEnv_Add");
        Check("The saved-locations section expands", () => SetToggle("ConsoleEnv_LocationsToggle", true));
        var envs = 0;
        Check("+ Add adds an environment and selects it", () =>
        {
            envs = ListCount("ConsoleEnv_List");
            return PressNth("ConsoleEnv_Add", 0)
                && WaitForFs(() => ListCount("ConsoleEnv_List") == envs + 1 && Exists("ConsoleEnv_BrowseInitialCommand"), 3);
        });
        Check("… opens the in-app file picker for the initial command", () =>
            PressNth("ConsoleEnv_BrowseInitialCommand", 0)
            && WaitFor(() => FindInAppWindows("FileBrowser_Cancel"), 10) is not null);
        Check("Cancel closes the file picker", () => PressInDialog("FileBrowser_Cancel"));
        Check("✕ Remove removes the environment", () =>
            PressNth("ConsoleEnv_Remove", 0) && WaitForFs(() => ListCount("ConsoleEnv_List") == envs, 3));

        // ── Projects ──────────────────────────────────────────────────────────────────
        // The locations are disabled until Projects is on, so it is switched on for the pass and off again after.
        SelectSection(sections!, "Projects", "ProjectsConfig_Enable");
        Check("Enabling Projects unlocks the locations", () => SetToggle("ProjectsConfig_Enable", true));
        Check("… opens the in-app folder picker for the project folder", () =>
            PressNth("ProjectsConfig_BrowseProject", 0)
            && WaitFor(() => FindInAppWindows("FolderBrowser_Cancel"), 10) is not null);
        Check("Cancel closes the folder picker", () => PressInDialog("FolderBrowser_Cancel"));
        CheckExists("Browse for the shelf folder", "ProjectsConfig_BrowseShelf");
        CheckExists("Browse for the archive folder", "ProjectsConfig_BrowseArchive");

        var statuses = 0;
        Check("+ Add adds a backlog status", () =>
        {
            statuses = CountOf("ProjectsConfig_RemoveStatus");
            return PressNth("ProjectsConfig_AddStatus", 0)
                && WaitForFs(() => CountOf("ProjectsConfig_RemoveStatus") == statuses + 1, 3);
        });
        Check("Move up lifts the new status", () => PressNth("ProjectsConfig_MoveUp", -1));
        Check("Move down puts it back", () => PressNth("ProjectsConfig_MoveDown", CountOf("ProjectsConfig_MoveDown") - 2));
        Check("✕ removes it", () =>
            PressNth("ProjectsConfig_RemoveStatus", -1)
            && WaitForFs(() => CountOf("ProjectsConfig_RemoveStatus") == statuses, 3));
        Check("Projects is switched back off", () => SetToggle("ProjectsConfig_Enable", false));

        // ── The panel's own controls ──────────────────────────────────────────────────
        CheckExists("Apply", "WorkspacePanel_Apply");

        // The pass leaves edits unapplied (a swatch, a grid cell), so closing asks about them first.
        Check("✕ asks about the unapplied edits", () =>
            PressNth("WorkspacePanel_CloseX", 0) && WaitFor(() => FindInAppWindows("Chrome_ConfirmAlternate"), 5) is not null);
        Check("It offers to apply them and close", () => FindInAppWindows("Chrome_ConfirmOk") is not null);
        Check("Keep editing leaves the panel open", () =>
            PressInDialog("Chrome_ConfirmCancel") && WaitForId("WorkspacePanel_Close", 3) is not null);
        Check("Discard closes the panel without them", () =>
            PressNth("WorkspacePanel_CloseX", 0)
            && WaitFor(() => FindInAppWindows("Chrome_ConfirmAlternate"), 5) is not null
            && PressInDialog("Chrome_ConfirmAlternate") && WaitForGone("WorkspacePanel_Close"));
        Check("Configure workspace… opens it again", () =>
            FromSelectorMenu("Configure workspace") && WaitForId("WorkspacePanel_Close", 10) is not null);
        CheckDoes("Close closes the panel", "WorkspacePanel_Close", () => WaitForGone("WorkspacePanel_CloseX"));

        AssertJourney();
    }

    /// <summary>Right-clicks the workspace selector and picks from the menu it raises (built in code, so by label).</summary>
    private bool FromSelectorMenu(string label)
    {
        var selector = WaitForId("Workspace_Selector", 5);
        if (selector is null) return false;
        selector.RightClick();
        Wait.UntilInputIsProcessed();
        return PickMenuItem(label);
    }
}
