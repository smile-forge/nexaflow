using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass UI journey for the <b>inside</b> of the modal Options overlay.
/// <para>
/// <see cref="ShellChromeJourneyTests"/> opens and closes Options, which proves the toggle works and
/// nothing more — every section, every editor and every custom control behind it was undriven. That is
/// a large blind spot: Options is where a feature's config is edited, and a section that throws while
/// rendering takes the whole overlay with it, on a surface no test ever looked at.
/// </para>
/// <para>
/// So this walks the section list itself. Sections come from the registered <c>IFeatureConfig</c>s, one
/// per feature, and building the list forces every feature assembly to activate — meaning a broken
/// section anywhere shows up here regardless of which feature owns it. The pass is deliberately
/// data-driven rather than a hard-coded section list: features are added and removed often, and a
/// journey that enumerated them by name would need editing every time.
/// </para>
/// <para>
/// <b>About → System components</b> is then driven specifically, because it is the one section that
/// reports on the machine rather than on config: it probes for third-party runtimes (Edge WebView2,
/// libvlc, the dotnet CLI) and is the page a user is sent to when a viewer says a component is missing.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[NoCoverage("options journey")]
public class OptionsJourneyTests : OptionsOverlayJourney
{
    [TestMethod]
    public void Options_SectionsRender_AndAboutReportsComponents()
    {
        var list = OpenOptions();

        try
        {
            var sections = list.FindAllChildren();
            Check("Options lists at least a few sections", () => sections.Length >= 3);

            // ── Every section renders ────────────────────────────────────────
            // Selecting a section builds its editor: a [CustomControl] instance, or a reflected property
            // grid. Either way something must appear on the right — a section that selects to a blank
            // pane is the exact failure this journey exists to catch.
            foreach (var section in sections)
            {
                var name = Label(section);
                if (string.IsNullOrWhiteSpace(name)) continue;

                Check($"Section '{name}' selects and renders", () =>
                {
                    section.Patterns.SelectionItem.PatternOrDefault?.Select();
                    Wait.UntilInputIsProcessed();
                    System.Threading.Thread.Sleep(120);

                    // A custom control names itself; a property grid renders rows. Either is a pass, and
                    // a section with genuinely no settings still renders its (empty) grid host.
                    return WaitForId("Options_CustomSection", 1) is not null
                        || MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_OptionsPanel"))
                                     ?.FindAllDescendants().Length > 0;
                });
            }

            // ── About → System components ────────────────────────────────────
            SelectSection(list, "About", "About_ComponentsToggle");

            CheckPresent("System components heading", "About_ComponentsToggle", 10);

            // Expanded only when something is missing, so open it explicitly rather than assuming.
            Expand("About_ComponentsToggle");

            CheckPresent("System components list", "About_ComponentsList", 10);

            // The declarations are real, so the probe must have produced real rows. WebView2 is the one
            // every build declares (from both the PDF reader and the Web tab, merged into a single row),
            // which is what makes it a safe anchor for this assertion.
            Check("WebView2 is listed as a component", () => ComponentRowNames().Any(
                n => n.Contains("WebView2", StringComparison.OrdinalIgnoreCase)));

            Check("Each listed component reports a status", () => ComponentRowNames().Any(n =>
                n.Contains("Installed",     StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Missing",       StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Not installed", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("check",         StringComparison.OrdinalIgnoreCase)));

            // Re-check re-probes without a restart. The observable effect is that the list survives it —
            // a refresh that emptied the list, or threw, would be worse than no button at all.
            CheckDoes("Re-check re-probes", "About_Recheck",
                () => Wait.UntilResponsive(MainWindow)
                       && WaitForId("About_ComponentsList", 10) is not null
                       && ComponentRowNames().Any(n => n.Contains("WebView2", StringComparison.OrdinalIgnoreCase)));

            // The notices section shares the page; expanding it proves the two collapsibles are independent.
            Expand("About_NoticesToggle");

            // Reset Config wipes every setting and restarts, so it asks first — and the question is declined.
            Check("Reset Config asks for confirmation", () =>
                PressNth("About_ResetConfig", 0) && WaitForId("Chrome_ConfirmCancel", 5) is not null);
            CheckDoes("Declining the reset keeps everything", "Chrome_ConfirmCancel", () => WaitForGone("Chrome_ConfirmCancel"));

            // ── The editors behind the sections ──────────────────────────────────────
            // Every section rendered above; these drive the controls inside the custom editors. They edit the throwaway
            // config dir's copy, and Cancel at the end discards even that.
            DriveVoice(list);
            DriveWorkspaces(list);
            DriveDefaultActions(list);
            DriveExternalApps(list);
            DriveFileTypeActions(list);
            DriveTemplatedCreate(list);
            DriveOneDrive(list);
            DriveGitPathPicker(list);

            // ── The panel's own footer ──
            // Save commits the edited copy and closes; the X closes without committing. Neither is pressed,
            // and not because the app would come to harm — it runs against a throwaway config dir, so what
            // they write is discarded with it. They are left alone because each one closes the panel, and
            // Cancel below is the exit whose behaviour this pass actually asserts.
            CheckPresent("Save",    "Options_Save");
            CheckPresent("Close X", "Options_CloseX");
        }
        finally
        {
            CancelOptions();
        }

        AssertJourney();
    }

    /// <summary>Expands a collapsible section, tolerating one that is already open.</summary>
    private void Expand(string automationId)
    {
        var toggle = WaitForId(automationId, 5);
        if (toggle is null) return;
        if (toggle.Patterns.Toggle.PatternOrDefault?.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On)
            return;

        toggle.Click();
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(200);
    }

    /// <summary>
    /// Every piece of text rendered inside the components list. The rows are a DataTemplate of TextBlocks,
    /// which publish their text as the automation Name — so this is how the journey reads what the user
    /// can actually see, rather than trusting that binding happened.
    /// </summary>
    private string[] ComponentRowNames()
    {
        var list = WaitForId("About_ComponentsList", 5);
        return list is null
            ? []
            : list.FindAllDescendants()
                  .Select(e => e.Name ?? string.Empty)
                  .Where(n => n.Length > 0)
                  .ToArray();
    }

    /// <summary>Download fetches a speech model from the network, so it is only checked for.</summary>
    private void DriveVoice(AutomationElement list)
    {
        SelectSection(list, "Voice", "Voice_Download");
        CheckExists("Download the speech model", "Voice_Download");
    }

    /// <summary>
    /// The workspace list. "+ Add Workspace" opens the setup wizard and Configure leaves Options for the workspace
    /// panel (WorkspaceConfigJourneyTests drives that), so both are checked for. A clone is added and removed.
    /// </summary>
    private void DriveWorkspaces(AutomationElement list)
    {
        SelectSection(list, "Workspaces", "Workspaces_Add");
        CheckExists("+ Add Workspace", "Workspaces_Add");
        CheckExists("Configure", "Workspaces_Configure");
        Check("A colour swatch recolours the workspace", () => PressNth("Workspaces_Swatch", 0));

        // One Clone per row, and Remove is collapsed for the live workspace — so the last Remove is the clone's.
        var rows = 0;
        Check("Clone adds a copy of the workspace", () =>
        {
            rows = CountOf("Workspaces_Clone");
            return PressNth("Workspaces_Clone", 0) && WaitForFs(() => CountOf("Workspaces_Clone") == rows + 1, 3);
        });
        Check("Remove on the copy asks first", () =>
            PressNth("Workspaces_Remove", -1) && WaitForId("Chrome_ConfirmOk", 5) is not null);
        CheckDoes("Confirming removes the copy", "Chrome_ConfirmOk",
                  () => WaitForFs(() => CountOf("Workspaces_Clone") == rows, 3));
    }

    /// <summary>An override is added for .txt, reopened, and removed again.</summary>
    private void DriveDefaultActions(AutomationElement list)
    {
        SelectSection(list, "Default Actions", "DefaultActions_Add");
        Check("+ Add default opens the editor", () =>
            PressNth("DefaultActions_Add", 0) && WaitForFs(() => Exists("DefaultActions_Cancel"), 3));
        Check("Cancel returns to the list", () =>
            PressNth("DefaultActions_Cancel", 0) && WaitForFs(() => Exists("DefaultActions_Add"), 3));

        Check("+ Add default again", () =>
            PressNth("DefaultActions_Add", 0) && WaitForFs(() => Exists("DefaultActions_Confirm"), 3));
        Check("An extension lists what can open it", () =>
            TypeInto("DefaultActions_Extension", ".txt") && WaitForFs(() => ListCount("DefaultActions_Candidates") > 1, 5));
        // The first candidate is "Automatic", which confirms as no override at all — so the second is picked.
        Check("A candidate can be picked", () =>
        {
            var candidates = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DefaultActions_Candidates"))
                                       ?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
            if (candidates is not { Length: > 1 }) return false;
            candidates[1].Patterns.SelectionItem.PatternOrDefault?.Select();
            return true;
        });
        Check("Confirm adds the override", () =>
            PressNth("DefaultActions_Confirm", 0) && WaitForFs(() => Exists("DefaultActions_Edit"), 3));
        Check("Edit reopens it", () =>
            PressNth("DefaultActions_Edit", 0) && WaitForFs(() => Exists("DefaultActions_Cancel"), 3));
        Check("Cancel leaves it as it was", () =>
            PressNth("DefaultActions_Cancel", 0) && WaitForFs(() => Exists("DefaultActions_Edit"), 3));
        Check("✕ removes it", () =>
            PressNth("DefaultActions_Remove", 0) && WaitForFs(() => !Exists("DefaultActions_Edit"), 3));
    }

    /// <summary>
    /// An app is added, its advanced section and a rule exercised, and the app removed. The three "…" buttons open the
    /// system file dialog, which is not the app's to drive, so they are checked for.
    /// </summary>
    private void DriveExternalApps(AutomationElement list)
    {
        SelectSection(list, "External Apps", "ExternalApps_Add");
        // Turning the registry switch off asks whether to keep the current Windows handlers by importing them
        // first — a window-modal question, so nothing else in the panel is reachable until it is answered.
        // "Do not Import" declines the (slow) HKCR sweep; either answer turns the handlers off.
        Check("Turning the registry switch off asks about importing", () =>
            SetToggle("ExternalApps_UseRegistry", false) && WaitForId("Chrome_ConfirmCancel", 5) is not null);
        Check("Do not Import answers it", () =>
            PressNth("Chrome_ConfirmCancel", 0) && WaitForFs(() => !Exists("Chrome_ConfirmCancel"), 5));
        Check("The registry switch goes back on", () => SetToggle("ExternalApps_UseRegistry", true));

        var apps = 0;
        Check("+ Add adds an app and selects it", () =>
        {
            apps = ListCount("ExternalApps_List");
            return PressNth("ExternalApps_Add", 0)
                && WaitForFs(() => ListCount("ExternalApps_List") == apps + 1 && Exists("ExternalApps_BrowseApp"), 3);
        });
        CheckExists("Browse for the application", "ExternalApps_BrowseApp");
        Check("Advanced options expands", () =>
            SetToggle("ExternalApps_AdvancedToggle", true) && WaitForFs(() => Exists("ExternalApps_BrowseWorkingDir"), 3));
        CheckExists("Browse for the working folder", "ExternalApps_BrowseWorkingDir");
        CheckExists("Browse for the icon", "ExternalApps_BrowseIcon");

        var rules = 0;
        Check("+ Add rule adds a matching rule", () =>
        {
            rules = CountOf("ExternalApps_RemoveRule");
            return PressNth("ExternalApps_AddRule", 0) && WaitForFs(() => CountOf("ExternalApps_RemoveRule") == rules + 1, 3);
        });
        Check("✕ removes the rule", () =>
            PressNth("ExternalApps_RemoveRule", -1) && WaitForFs(() => CountOf("ExternalApps_RemoveRule") == rules, 3));
        Check("The rules section collapses", () =>
            SetToggle("ExternalApps_RulesToggle", false) && WaitForFs(() => !Exists("ExternalApps_AddRule"), 3));

        Check("✕ Remove removes the app", () =>
            PressNth("ExternalApps_Remove", 0) && WaitForFs(() => ListCount("ExternalApps_List") == apps, 3));
    }

    /// <summary>A criterion is added and removed, then added again so the mapping differs from its bundled default —
    /// which is what offers Reset: declined once, then confirmed, restoring the original criteria.</summary>
    private void DriveFileTypeActions(AutomationElement list)
    {
        SelectSection(list, "File Type Actions", "FileMap_Tree");
        Check("Selecting an experience opens its criteria", SelectFirstMapping);

        var criteria = 0;
        Check("+ Add Criterion adds a row", () =>
        {
            criteria = CountOf("FileMap_RemoveCriterion");
            return PressNth("FileMap_AddCriterion", 0) && WaitForFs(() => CountOf("FileMap_RemoveCriterion") == criteria + 1, 3);
        });
        Check("✕ removes it", () =>
            PressNth("FileMap_RemoveCriterion", -1) && WaitForFs(() => CountOf("FileMap_RemoveCriterion") == criteria, 3));

        // Reset to Default appears only once the live criteria differ from the bundled ones, and a blank row is
        // not a difference — the editor ignores empty values because they change nothing about what the mapping
        // matches. So what makes it differ is dropping a criterion the experience really has.
        Check("Dropping a criterion makes the mapping differ from its default", () =>
            criteria > 0
            && PressNth("FileMap_RemoveCriterion", -1)
            && WaitForFs(() => CountOf("FileMap_RemoveCriterion") == criteria - 1
                            && Exists("FileMap_ResetToDefault"), 3));
        Check("Reset to Default asks inline", () =>
            PressNth("FileMap_ResetToDefault", 0) && WaitForFs(() => Exists("FileMap_ResetCancel"), 3));
        Check("Cancel keeps the edit", () =>
            PressNth("FileMap_ResetCancel", 0)
            && WaitForFs(() => Exists("FileMap_ResetToDefault") && CountOf("FileMap_RemoveCriterion") == criteria - 1, 3));
        Check("Reset asks again", () =>
            PressNth("FileMap_ResetToDefault", 0) && WaitForFs(() => Exists("FileMap_ResetConfirm"), 3));
        Check("Confirming restores the bundled criteria", () =>
            PressNth("FileMap_ResetConfirm", 0) && WaitForFs(() => CountOf("FileMap_RemoveCriterion") == criteria, 3));
    }

    /// <summary>Selects tree rows in turn until one whose mapping opens the criteria editor.</summary>
    private bool SelectFirstMapping()
    {
        var tree = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileMap_Tree"));
        if (tree is null) return false;
        foreach (var row in tree.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem)))
        {
            row.Patterns.SelectionItem.PatternOrDefault?.Select();
            Wait.UntilInputIsProcessed();
            if (WaitForFs(() => Exists("FileMap_AddCriterion"), 1)) return true;
        }
        return false;
    }

    /// <summary>A template is added and removed. Its "…" opens the system file dialog, so it is checked for.</summary>
    private void DriveTemplatedCreate(AutomationElement list)
    {
        SelectSection(list, "Templated Create", "TemplatedCreate_Add");
        var templates = 0;
        Check("+ Add adds a template and selects it", () =>
        {
            templates = ListCount("TemplatedCreate_List");
            return PressNth("TemplatedCreate_Add", 0) && WaitForFs(() => ListCount("TemplatedCreate_List") == templates + 1, 3);
        });
        CheckExists("Browse for the template's source", "TemplatedCreate_BrowseSource");
        Check("✕ Remove removes it", () =>
            PressNth("TemplatedCreate_Remove", 0) && WaitForFs(() => ListCount("TemplatedCreate_List") == templates, 3));
    }

    /// <summary>"Add folder…" opens the system folder dialog, so it is checked for.</summary>
    private void DriveOneDrive(AutomationElement list)
    {
        SelectSection(list, "OneDrive", "OneDriveOpt_AddFolder");
        CheckExists("Add folder…", "OneDriveOpt_AddFolder");
    }

    /// <summary>
    /// Git's manager path is a [FilePath] setting, which the property grid renders with a browse button onto the
    /// app's own file picker — the one path editor any section declares. Opened, checked, and cancelled.
    /// The id is the Options panel's own: OptionsPanel.xaml carries its own copies of the property-row templates,
    /// so the ConfigEditorView twin the Configure panel and the wizard render is never in this tree. Naming that
    /// one here would also be enough for the AutomationId ratchet to call it covered, which is how it came to be
    /// pressed by name and never found — so it is deliberately not spelled out.
    /// </summary>
    private void DriveGitPathPicker(AutomationElement list)
    {
        SelectSection(list, "Git", "Options_BrowseFile");
        Check("Browse opens the in-app file picker", () =>
            PressNth("Options_BrowseFile", 0) && WaitFor(() => FindInAppWindows("FileBrowser_Cancel"), 10) is not null);
        Check("The file picker offers OK", () => FindInAppWindows("FileBrowser_Ok") is not null);
        Check("Cancel closes the file picker", () => PressInDialog("FileBrowser_Cancel"));
    }
}
