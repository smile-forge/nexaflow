using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One window moving between two workspaces, and each workspace keeping its tabs while the window is away.
/// <para>
/// A workspace has at most one live runtime. Picking another workspace in the selector moves this window onto that
/// workspace's runtime and leaves its own; the runtime left with no window turns dormant, and the window coming back
/// within the dormant time resumes it at once with the tabs it had — no "restore last session?" offer — where a workspace
/// opened for the first time shows its startup tabs.
/// </para>
/// <para>
/// The second workspace is made the way a person makes one, by cloning the first in Options → Workspaces. Each is given a
/// folder of its own in its file browser, so its tab says which workspace's tabs the window is showing: the folder's name
/// on the tab, and a file only that folder holds in the list.
/// </para>
/// <para>
/// Last, Options → Shell sets a workspace left to close at once. One closed that way and opened again shows its startup
/// tabs and offers, in a toast naming the workspace, the session it was left with; taking the offer brings its tabs back.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("ws-lifetimes")]
[CoversNode("ws-switch-profile")]
public class WorkspaceLifetimesJourneyTests : OptionsOverlayJourney
{
    private const string Selector   = "Workspace_Selector";
    private const string SwitchItem = "Workspace_SwitchItem";

    /// <summary>The selector's tooltip names the window's workspace in this form.</summary>
    private const string SelectorTipSuffix = " Workspace";

    /// <summary>The dormant time that closes a workspace as soon as its last window leaves it.</summary>
    private const string CloseAtOnce = "Close at once";

    /// <summary>The title of the toast offering a workspace opened again the session it was left with.</summary>
    private const string RestoreOfferTitle = "Restore last session?";

    private const string FirstMarker  = "first-workspace.txt";
    private const string SecondMarker = "second-workspace.txt";

    private string _firstFolder  = null!;
    private string _secondFolder = null!;

    /// <summary>The workspace the app launched into, and the clone made of it.</summary>
    private string _first  = "";
    private string _second = "";

    /// <summary>The tab titles the app launched with: the first workspace's startup tabs, which its clone shares.</summary>
    private List<string> _startupTabs = [];

    protected override void OnUISetup()
    {
        var root = Path.Combine(TestTemp.Root, "nexaflow-ws-lifetimes-" + Guid.NewGuid().ToString("N")[..8]);
        _firstFolder  = Path.Combine(root, "WsOne");
        _secondFolder = Path.Combine(root, "WsTwo");
        Directory.CreateDirectory(_firstFolder);
        Directory.CreateDirectory(_secondFolder);
        File.WriteAllText(Path.Combine(_firstFolder, FirstMarker), "the first workspace's folder");
        File.WriteAllText(Path.Combine(_secondFolder, SecondMarker), "the second workspace's folder");
    }

    /// <summary>A second workspace made in Options, the window moved to it and back, and each workspace found as it was left.</summary>
    [TestMethod]
    public void WorkspaceLifetimes_Journey()
    {
        RunStep("Options makes a second workspace, cloned from the first", MakeASecondWorkspace);
        RunStep("the window moves to the second workspace, which opens on its startup tabs", MoveToTheSecond);
        RunStep("back on the first workspace, its folder tab is as it was left, at once", ResumeTheFirst);
        RunStep("and the second, left in its turn, resumes with its own folder", ResumeTheSecond);
        RunStep("set to close at once, a workspace left ends, and opened again offers the session it was left with", ReopenedOffersItsLastSession);
        AssertJourney();
    }

    private void MakeASecondWorkspace()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "The file browser the app launches into did not load.");
        _startupTabs = TabTitles();
        _first = WorkspaceShown() ?? "";
        Check($"The app launches on its startup tabs ({string.Join(", ", _startupTabs)})", () => _startupTabs.Count > 0);
        Check($"The selector names the workspace the app launched into ('{_first}')", () => _first.Length > 0);

        var list = OpenOptions();
        try
        {
            SelectSection(list, "Workspaces", "Workspaces_Clone");
            var rows = 0;
            Check("Clone adds a copy of the workspace", () =>
            {
                rows = CountOf("Workspaces_Clone");
                return PressNth("Workspaces_Clone", 0) && WaitForFs(() => CountOf("Workspaces_Clone") == rows + 1, 3);
            });
            CheckDoes("Save keeps it and closes Options", "Options_Save", () => WaitForGone("Chrome_OptionsPanel", 5));
        }
        finally
        {
            if (WaitForId("Chrome_OptionsPanel", 1) is not null) CancelOptions();
        }

        // The clone is named after its source; the switcher lists the saved workspaces, the copy beneath its source.
        _second = _first + " copy";
        Check($"The switcher lists '{_first}' and its copy '{_second}'", () =>
        {
            var names = OpenSwitcher() ? SwitchItemNames() : new List<string>();
            CloseSwitcher();
            return names.SequenceEqual([_first, _second]);
        });
    }

    private void MoveToTheSecond()
    {
        NavigateFileBrowserTo(_firstFolder);
        Check("The first workspace's file browser shows its folder", () =>
            TabsAre(5, FolderName(_firstFolder)) && FindInFileList(FirstMarker, 10) is not null);

        Check($"Picking '{_second}' in the switcher moves the window to it", () => SwitchTo(_second));
        Check("The second workspace opens on its startup tabs", () =>
            TabsAre(10, [.. _startupTabs]));
        Check("Nothing of the first workspace's came with the window", () => FindInFileList(FirstMarker, 1) is null);

        NavigateFileBrowserTo(_secondFolder);
        Check("The second workspace's file browser shows a folder of its own", () =>
            TabsAre(5, FolderName(_secondFolder)) && FindInFileList(SecondMarker, 10) is not null);
    }

    private void ResumeTheFirst()
    {
        Check($"Picking '{_first}' moves the window back", () => SwitchTo(_first));
        Check("Its folder tab is back at once, the only tab, as it was left", () =>
            TabsAre(5, FolderName(_firstFolder)));
        Check("It shows that folder's files", () => FindInFileList(FirstMarker, 10) is not null);
        Check("Nothing of the second workspace's came with the window", () => FindInFileList(SecondMarker, 1) is null);
        Check("Nothing offers to restore a last session: the workspace was resumed, not reopened", () => !RestoreOffered());
    }

    private void ResumeTheSecond()
    {
        Check($"Picking '{_second}' again moves the window to it", () => SwitchTo(_second));
        Check("Its folder tab is back, as it was left", () =>
            TabsAre(5, FolderName(_secondFolder)));
        Check("It shows that folder's files", () => FindInFileList(SecondMarker, 10) is not null);
        Check("Nothing offers to restore a last session", () => !RestoreOffered());
    }

    /// <summary>
    /// Options → Shell sets a workspace left to close at once rather than stay dormant. The window, on the second workspace, goes
    /// back to the first — dormant since before the change, so still resumed — and the second, closed as it was left, opens
    /// afresh on its startup tabs, offering the session it was left with; taking the offer brings its folder tab back.
    /// </summary>
    private void ReopenedOffersItsLastSession()
    {
        var list = OpenOptions();
        try
        {
            SelectSection(list, "Shell", "ShellDormantWorkspaceCombo");
            Check($"A workspace left can be set to '{CloseAtOnce}'", () =>
            {
                var combo = WaitForId("ShellDormantWorkspaceCombo", 5)?.AsComboBox();
                combo?.Select(CloseAtOnce);
                return combo?.SelectedItem?.Text == CloseAtOnce;
            });
            CheckDoes("Save keeps it and closes Options", "Options_Save", () => WaitForGone("Chrome_OptionsPanel", 5));
        }
        finally
        {
            if (WaitForId("Chrome_OptionsPanel", 1) is not null) CancelOptions();
        }

        Check($"Picking '{_first}' moves the window back", () => SwitchTo(_first));
        Check("The first, dormant since before the change, still resumes with its folder tab", () =>
            TabsAre(5, FolderName(_firstFolder)));

        Check($"Picking '{_second}' opens it again", () => SwitchTo(_second));

        // The offer is a toast, gone five seconds after it shows: it is found, and the strip beside it read, before anything
        // else is waited on.
        var offer = WaitFor(RestoreOffer, Affordable(5));
        var shownWithTheOffer = TabTitles();
        var offerNamesTheWorkspace = ToastNames(_second);
        Check("Closed when it was left, it opens afresh and offers to restore the session it was left with", () => offer is not null);
        Check($"The offer names the workspace it is about, '{_second}'", () => offerNamesTheWorkspace);
        Check($"Beside it the window shows the workspace's startup tabs (the strip shows {string.Join(" | ", shownWithTheOffer)})",
              () => shownWithTheOffer.SequenceEqual(_startupTabs));
        Check("Restore brings its folder tab back", () =>
        {
            if (offer is null) return false;
            offer.AsButton().Invoke();
            Wait.UntilInputIsProcessed();
            return TabsAre(5, FolderName(_secondFolder));
        });
        Check("It shows that folder's files", () => FindInFileList(SecondMarker, 10) is not null);
    }

    // ── The selector ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The workspace the window shows, as the selector's tooltip names it; null when the selector is not there.</summary>
    private string? WorkspaceShown()
    {
        var tip = WaitForId(Selector, 5)?.HelpText;
        return tip is { Length: > 0 } && tip.EndsWith(SelectorTipSuffix, StringComparison.Ordinal)
            ? tip[..^SelectorTipSuffix.Length]
            : tip;
    }

    /// <summary>Opens the switcher: true once its rows show.</summary>
    private bool OpenSwitcher() =>
        PressNth(Selector, 0) && WaitForFs(() => CountOf(SwitchItem) > 0, 5);

    /// <summary>Closes the switcher without picking: it closes on a click away, which Escape stands for.</summary>
    private void CloseSwitcher()
    {
        if (CountOf(SwitchItem) == 0) return;
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Each row's workspace name, in the switcher's order. A row shows the workspace's icon, then its name.</summary>
    private List<string> SwitchItemNames() =>
        MainWindow.FindAllDescendants(cf => cf.ByAutomationId(SwitchItem))
                  .Select(row => row.FindAllChildren(cf => cf.ByControlType(ControlType.Text)).LastOrDefault()?.Name ?? "")
                  .ToList();

    /// <summary>Picks <paramref name="workspace"/> in the switcher: true once the selector names it as the window's.</summary>
    private bool SwitchTo(string workspace)
    {
        if (!OpenSwitcher()) return false;
        var rows = MainWindow.FindAllDescendants(cf => cf.ByAutomationId(SwitchItem));
        var row = rows.FirstOrDefault(r => r.FindAllChildren(cf => cf.ByControlType(ControlType.Text)).LastOrDefault()?.Name == workspace);
        if (row is null)
        {
            CloseSwitcher();
            return false;
        }
        row.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => WorkspaceShown() == workspace, 10);
    }

    // ── The tabs ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every tab's title, in strip order. A tab shows its icon, its title and its close button; the close button is the one
    /// carrying an id, and the title is the text before it.
    /// </summary>
    private List<string> TabTitles() =>
        MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip"))
                  .SelectMany(strip => strip.FindAllDescendants())
                  .Where(e => e.Properties.AutomationId.ValueOrDefault?.StartsWith("TabItem_", StringComparison.Ordinal) == true)
                  .Select(tab => tab.FindAllChildren(cf => cf.ByControlType(ControlType.Text))
                                    .Where(t => string.IsNullOrEmpty(t.Properties.AutomationId.ValueOrDefault))
                                    .Select(t => t.Name ?? "")
                                    .LastOrDefault() ?? "")
                  .ToList();

    /// <summary>
    /// True once the strip shows exactly <paramref name="expected"/>, waiting up to <paramref name="seconds"/>; otherwise what it
    /// shows instead is written to the output, since the check that fails can only say it returned false.
    /// </summary>
    private bool TabsAre(int seconds, params string[] expected)
    {
        var seen = new List<string>();
        if (WaitForFs(() => (seen = TabTitles()).SequenceEqual(expected), Affordable(seconds))) return true;
        System.Console.WriteLine($"[tabs] expected {string.Join(" | ", expected)}; the strip shows {string.Join(" | ", seen)}");
        return false;
    }

    /// <summary>The title a file browser's tab gives <paramref name="folder"/>: its own name.</summary>
    private static string FolderName(string folder) => Path.GetFileName(folder);

    /// <summary>True while a toast offers to restore a last session — what a workspace opened afresh offers, and a resumed one must not.</summary>
    private bool RestoreOffered() => RestoreOffer() is not null;

    /// <summary>The toast's Restore, while a toast offers to restore a last session; null when none does.</summary>
    private AutomationElement? RestoreOffer() =>
        MainWindow.FindAllDescendants(cf => cf.ByAutomationId("Toast_Action"))
                  .FirstOrDefault(action => !action.IsOffscreen && action.Name?.Contains("Restore", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// True when the restore offer's toast names <paramref name="workspace"/> as the one it is about. The toast heads its title
    /// with the workspace's name, its icon before both.
    /// </summary>
    private bool ToastNames(string workspace)
    {
        var title = MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(RestoreOfferTitle))).FirstOrDefault();
        if (title is null) return false;
        var walker = Automation.TreeWalkerFactory.GetRawViewWalker();
        var heading = new List<string>();
        for (var e = walker.GetPreviousSibling(title); e is not null && heading.Count < 2; e = walker.GetPreviousSibling(e))
            heading.Add(e.Name ?? "");
        return heading.Contains(workspace);
    }
}
