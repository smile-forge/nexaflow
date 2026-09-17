using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Core;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The workspace's startup tabset and the last-session offer beside it: what a captured tabset reopens
/// as, what a closing window records, and what the "never offer to restore" opt-out changes. Reopening
/// itself needs the feature registry to build the pages, so what is asserted here is the plan the
/// restore follows — pane, order and which tab ends up selected — plus the recording that feeds it.
/// Shares the <see cref="WorkspaceManager"/>/<see cref="ConfigManager"/> singletons, so it runs in the
/// non-parallel phase against a temp config root.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("workspaces")]
public class WorkspaceStartupTabsTests
{
    private string _origBaseDir = string.Empty;
    private string _baseDir     = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _origBaseDir = ConfigManager.Instance.BaseDir;
        _baseDir     = Path.Combine(Path.GetTempPath(), "nexaflow-tabs-" + Guid.NewGuid().ToString("N"));
        ConfigManager.Instance.Initialize(_baseDir);
    }

    [TestCleanup]
    public void Teardown()
    {
        ConfigManager.Instance.Initialize(_origBaseDir);
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static DefaultTabDescriptor Tab(string kind, int pane = 0, bool active = false)
        => new() { PageKind = kind, Pane = pane, IsActive = active, Title = kind };

    // ── The plan a saved tabset reopens by ──

    [TestMethod]
    public void PlanLayout_OpensEachPanesActiveTabLast_SoItEndsSelected()
    {
        // AddTab makes the newest tab active, so the tab that was selected has to be opened last or the
        // reopened window comes back sitting on the wrong tab.
        var (left, right) = ShellServices.PlanLayout(
            [Tab("Terminal", active: true), Tab("FileSystem"), Tab("Markdown")]);

        CollectionAssert.AreEqual(new[] { "FileSystem", "Markdown", "Terminal" },
                                  left.Select(t => t.PageKind).ToArray());
        Assert.AreEqual(0, right.Count);
    }

    [TestMethod]
    public void PlanLayout_KeepsEachPanesTabsInItsOwnPane()
    {
        var (left, right) = ShellServices.PlanLayout(
            [Tab("FileSystem"), Tab("Terminal", pane: 1, active: true), Tab("Markdown", pane: 1)]);

        CollectionAssert.AreEqual(new[] { "FileSystem" },           left.Select(t => t.PageKind).ToArray());
        CollectionAssert.AreEqual(new[] { "Markdown", "Terminal" }, right.Select(t => t.PageKind).ToArray());
    }

    [TestMethod]
    public void PlanLayout_ARightOnlyTabset_CollapsesIntoOnePane()
    {
        // Splitting off an empty left pane would reopen a window the user never had.
        var (left, right) = ShellServices.PlanLayout([Tab("Terminal", pane: 1)]);

        CollectionAssert.AreEqual(new[] { "Terminal" }, left.Select(t => t.PageKind).ToArray());
        Assert.AreEqual(0, right.Count);
    }

    [TestMethod]
    public void PlanLayout_AnEmptyTabset_OpensNothing()
    {
        var (left, right) = ShellServices.PlanLayout([]);

        Assert.AreEqual(0, left.Count, "an empty startup tabset is an explicit start-blank");
        Assert.AreEqual(0, right.Count);
    }

    // ── Setting the default tabset: the round trip through disk ──

    [TestMethod]
    public void ADefaultTabsetIsPersisted_AndComesBackFieldForField()
    {
        var workspace = new Workspace { Name = "Dev" };
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [workspace] });

        // What "Use Tabset as Default" writes: the captured layout, its panes, params and selection.
        workspace.DefaultTabs =
        [
            new DefaultTabDescriptor
            {
                PageKind   = "FileSystem",
                PageParams = new() { ["mode"] = "thispc", ["path"] = @"C:\work" },
                Pane       = 0,
                Title      = "This PC",
                IsActive   = false,
            },
            new DefaultTabDescriptor { PageKind = "Terminal", Pane = 1, Title = "Terminal", IsActive = true },
        ];

        WorkspaceManager.Instance.SaveWorkspaces();

        var reloaded = ReadBack().Contexts.Single(w => w.Name == "Dev");
        Assert.AreEqual(2, reloaded.DefaultTabs.Count);

        var first = reloaded.DefaultTabs[0];
        Assert.AreEqual("FileSystem", first.PageKind);
        Assert.AreEqual("This PC",    first.Title);
        Assert.AreEqual(0,            first.Pane);
        Assert.IsFalse(first.IsActive);
        Assert.IsNotNull(first.PageParams);
        Assert.AreEqual("thispc",   first.PageParams!["mode"]);
        Assert.AreEqual(@"C:\work", first.PageParams["path"]);

        var second = reloaded.DefaultTabs[1];
        Assert.AreEqual("Terminal", second.PageKind);
        Assert.AreEqual(1,          second.Pane);
        Assert.IsTrue(second.IsActive, "the selected tab is what the next window opens on");

        // And what the restore then does with it: the right-pane tab is still in the right pane.
        var (left, right) = ShellServices.PlanLayout(reloaded.DefaultTabs);
        CollectionAssert.AreEqual(new[] { "FileSystem" }, left.Select(t => t.PageKind).ToArray());
        CollectionAssert.AreEqual(new[] { "Terminal" },   right.Select(t => t.PageKind).ToArray());
    }

    [TestMethod]
    public void AnEmptyDefaultTabset_IsHonoured_ButANullOneIsReseeded()
    {
        var workspace = new Workspace { Name = "Blank", DefaultTabs = [] };
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [workspace] });
        WorkspaceManager.Instance.SaveWorkspaces();

        Assert.AreEqual(0, ReadBack().Contexts.Single().DefaultTabs.Count,
                        "starting with no tabs is a choice, not a missing value");

        workspace.DefaultTabs = null!;
        Assert.AreEqual("FileSystem", workspace.DefaultTabs.Single().PageKind,
                        "a stray null falls back to the out-of-the-box This PC tab");
    }

    // ── The last-session offer, and the opt-out ──

    [TestMethod]
    public void AClosingWindow_RecordsItsTabs_OnlyWhenTheyDifferFromTheDefault()
    {
        var (workspace, shell, host) = LiveWorkspace("Dev");
        workspace.DefaultTabs = [Tab("FileSystem")];

        host.Layout = [Tab("FileSystem"), Tab("Terminal")];
        shell.CaptureLastSession(host);
        Assert.AreEqual(2, workspace.LastSessionTabs?.Count, "a different tabset is worth offering back");

        host.Layout = [Tab("FileSystem")];
        shell.CaptureLastSession(host);
        Assert.IsNull(workspace.LastSessionTabs, "a session matching the default has nothing to restore");
    }

    [TestMethod]
    public void TheOptOut_StopsTheRecording_AndDropsWhatWasAlreadyRecorded()
    {
        var (workspace, shell, host) = LiveWorkspace("Dev");
        workspace.DefaultTabs = [Tab("FileSystem")];

        host.Layout = [Tab("FileSystem"), Tab("Terminal")];
        shell.CaptureLastSession(host);
        Assert.IsNotNull(workspace.LastSessionTabs);

        // Turned on with a session already held: the offer must not survive the setting.
        workspace.SuppressSessionRestore = true;
        shell.CaptureLastSession(host);

        Assert.IsNull(workspace.LastSessionTabs);
        Assert.IsNull(ReadBack().Contexts.Single().LastSessionTabs, "and the drop reached disk");
    }

    [TestMethod]
    public void TheOptOut_RoundTripsWithTheWorkspace()
    {
        var workspace = new Workspace { Name = "Fixed", SuppressSessionRestore = true };
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [workspace] });
        WorkspaceManager.Instance.SaveWorkspaces();

        Assert.IsTrue(ReadBack().Contexts.Single().SuppressSessionRestore);
    }

    // ── Helpers ──

    // Reads the saved list back through the real load path (the property-by-property read that
    // Register/LoadFrom use at startup), so the assertions are about what the app itself will see.
    private WorkspacesConfig ReadBack()
    {
        var fresh = new WorkspacesConfig();
        ConfigManager.Instance.LoadFrom(_baseDir, fresh, fresh.ConfigName);
        return fresh;
    }

    // A workspace with one live runtime and one registered, focused window.
    private static (Workspace, ShellServices, FakeWindowHost) LiveWorkspace(string name)
    {
        var workspace = new Workspace { Name = name };
        WorkspaceManager.Instance.Initialize(new WorkspacesConfig { Contexts = [workspace] });

        var runtime = new WorkspaceRuntime(workspace);
        var shell   = new ShellServices(runtime);
        var host    = new FakeWindowHost();
        shell.RegisterWindow(host);
        shell.SetFocused(host);
        return (workspace, shell, host);
    }
}
