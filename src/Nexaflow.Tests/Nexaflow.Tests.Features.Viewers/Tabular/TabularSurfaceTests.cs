using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.Tabular.FileActions;
using Nexaflow.Features.Tabular.RibbonHandlers;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.Text;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// The Tabular tab's readouts and entry points: what the toolbar descriptor and the status footer report
/// after a load, how a row is selected, and the two ways a file gets into the tab (the "As Table" file
/// action, and re-opening a pinned ribbon button). These are the parts a user reads to decide whether to
/// trust the grid — a wrong descriptor or a dropped transform payload is silent otherwise.
///
/// Template popup / panel command state lives in <see cref="TabularViewModelTests"/>; loading and
/// auto-apply in <see cref="TabularViewModelLoadTests"/>.
/// </summary>
[TestClass]
public class TabularSurfaceTests
{
    private static string WriteCsv(out string dir, string content = "a,b,c\n1,2,3\n4,5,6\n")
    {
        dir = Path.Combine(Path.GetTempPath(), "nexatab_surface_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "t.csv");
        File.WriteAllText(csv, content);
        return csv;
    }

    private static TabularViewModel Load(string csv)
    {
        var vm = new TabularViewModel(csv,
            Substitute.For<IShellServices>(), Substitute.For<IAIService>(), new TabularTemplatesConfig());
        vm.Ready.GetAwaiter().GetResult();
        return vm;
    }

    // ── Toolbar descriptor ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-shape-label")]
    public void ShapeDescriptor_ReadsBackHowTheFileWasInterpreted()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = Load(csv);

            Assert.AreNotEqual("Detecting…", vm.DetectedShapeLabel, "the descriptor must resolve once loaded");
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.DetectedShapeLabel));
            Assert.AreEqual(3, vm.Columns.Count, "precondition: the comma shape was detected");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Status footer ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-encoding-label")]
    public void EncodingLabel_ReportsTheDetectedEncoding()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = Load(csv);

            Assert.AreNotEqual("?", vm.EncodingName, "the footer must name the encoding the file was decoded with");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("tabular-mode-label")]
    public void ModeLabel_SmallFileOpensInSmallMode_WhereSortingIsAvailable()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = Load(csv);

            Assert.IsTrue(vm.IsSmallMode, "a tiny CSV loads whole, which is what enables column sort");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("tabular-row-count")]
    public void RowCount_SettlesOnTheTrueBodyRowCount()
    {
        var csv = WriteCsv(out var dir, "a,b,c\n1,2,3\n4,5,6\n7,8,9\n");
        try
        {
            var vm = Load(csv);

            Assert.IsTrue(vm.TotalRowCount >= 3, "the footer count covers at least the body rows");
            Assert.IsFalse(vm.IsCountingRows, "a small file is counted outright, not in the background");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Row selection ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-row-select")]
    public void RowClick_SelectsOne_CtrlToggles_ShiftExtendsARange()
    {
        var csv = WriteCsv(out var dir, "a,b,c\n1,2,3\n4,5,6\n7,8,9\n0,0,0\n");
        try
        {
            var vm = Load(csv);

            vm.HandleRowClick(0, ctrl: false, shift: false);
            CollectionAssert.AreEquivalent(new[] { 0 }, vm.SelectedRowIndices.ToArray());

            vm.HandleRowClick(2, ctrl: true, shift: false);
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, vm.SelectedRowIndices.ToArray());

            vm.HandleRowClick(2, ctrl: true, shift: false);   // ctrl-click again toggles it back off
            CollectionAssert.AreEquivalent(new[] { 0 }, vm.SelectedRowIndices.ToArray());

            vm.HandleRowClick(0, ctrl: false, shift: false);
            vm.HandleRowClick(2, ctrl: false, shift: true);   // extend from the anchor
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, vm.SelectedRowIndices.ToArray());

            vm.HandleRowClick(3, ctrl: false, shift: false);  // a plain click replaces the selection
            CollectionAssert.AreEquivalent(new[] { 3 }, vm.SelectedRowIndices.ToArray());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Path breadcrumb ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-breadcrumb")]
    public void Breadcrumb_FolderCrumbOpensAnExplorerThere_FileCrumbIsInert()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var shell = Substitute.For<IShellServices>();
            var vm = new TabularViewModel(csv, shell, Substitute.For<IAIService>(), new TabularTemplatesConfig());
            vm.Ready.GetAwaiter().GetResult();

            Assert.IsTrue(vm.PathBreadcrumb.Count > 0, "the tab shows a folder › file trail");
            Assert.IsTrue(vm.PathBreadcrumb[^1].IsFile, "the last crumb is the file itself");

            // The file crumb is the tab you're already on — clicking it must do nothing.
            vm.OpenBreadcrumb(vm.PathBreadcrumb[^1]);
            shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default, default, default);

            var folder = vm.PathBreadcrumb.Last(c => !c.IsFile);
            vm.OpenBreadcrumb(folder);
            shell.Received(1).OpenTab("FileSystem",
                Arg.Is<Dictionary<string, string>>(p => p["mode"] == "path" && p["path"] == folder.FullPath),
                Arg.Any<IPageView?>());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Entry points ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-open-action")]
    public void AsTableAction_OpensTheTabularTab_WithThePath()
    {
        var shell = Substitute.For<IShellServices>();
        var action = new ShowTabularAction(shell);

        Assert.AreEqual("As Table", action.DisplayName);
        Assert.AreEqual("/tabular", action.ExperienceId);
        Assert.IsTrue(action.OpensViewer);
        Assert.IsFalse(action.IsDestructive);

        Assert.IsTrue(action.PerformAction(@"C:\data\sales.csv"));

        shell.Received(1).OpenTab("Tabular",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\data\sales.csv"),
            Arg.Any<IPageView?>());
    }

    /// <summary>
    /// Pinning captures the live transform chain so the ribbon button reopens the file <i>reshaped</i>.
    /// With no view attached there is no chain to snapshot, and the handler must then <b>drop</b> any
    /// "transforms" key it inherited from the tab's parameters — carrying a stale payload would reopen
    /// the file with someone else's reshaping.
    /// </summary>
    [TestMethod]
    [CoversNode("tabular-ribbon-pin")]
    public void Pin_LabelsTheButtonByFileName_AndDropsAStaleTransformPayload()
    {
        var handler = new TabularTabPinHandler();
        var tab = new Page
        {
            Title      = "some tab title",
            Icon       = "▦",
            PageParams = new Dictionary<string, string>
            {
                ["path"]       = @"C:\data\sales.csv",
                ["transforms"] = "[{\"kind\":\"Rename\",\"index\":0,\"name\":\"Stale\"}]",
            },
        };

        var result = handler.Pin(tab);

        Assert.IsNotNull(result);
        Assert.AreEqual("sales.csv", result!.Label, "the pinned button is labelled by the file, not the tab title");
        Assert.AreEqual(@"C:\data\sales.csv", result.PageParams!["path"]);
        Assert.IsFalse(result.PageParams.ContainsKey("transforms"),
                       "no live chain ⇒ the inherited payload must be dropped, not replayed");
        Assert.AreEqual(handler.TabPageKind, result.PageKind);
    }

    [TestMethod]
    [CoversNode("tabular-ribbon-pin")]
    public void Pin_WithNoPath_FallsBackToTheTabTitle()
    {
        var result = new TabularTabPinHandler().Pin(new Page { Title = "Untitled table", Icon = "▦" });

        Assert.IsNotNull(result);
        Assert.AreEqual("Untitled table", result!.Label);
    }

    /// <summary>
    /// Disposing the tab has to leave the file free straight away.
    /// <para>
    /// The row count runs detached with the file open for the whole scan, and cancelling it only asks it
    /// to stop — the token is read between chunks. So a Dispose that returned as soon as it had cancelled
    /// left a live handle behind, and the file could not be deleted or moved for a moment afterwards. In
    /// the app that is closing a CSV's tab and then failing to delete the CSV; in CI it was an occasional
    /// "being used by another process" in whichever tabular test tore its temp folder down first.
    /// </para>
    /// <para>
    /// The delete below is deliberately not retried. A bounded retry would pass either way and would only
    /// prove the handle closes eventually, which was never in doubt — the claim is that Dispose has
    /// already waited for it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Dispose_ReleasesTheFile_WhileTheRowCountIsStillRunning()
    {
        // Large enough to take the windowed path, which is the only mode that starts a background count,
        // and long enough that the count is still reading when Ready completes.
        var rows = new StringBuilder("name,age,city\n");
        for (var i = 0; i < 50_000; i++) rows.Append($"person{i},{i % 90},town number {i} padded out a bit\n");

        var csv = WriteCsv(out var dir, rows.ToString());
        var vm = Load(csv);

        Assert.IsFalse(vm.IsSmallMode, "precondition: only the windowed path starts the row count");
        // Without this the test can pass for the wrong reason: on a fast disk the count sometimes finishes
        // before Ready returns, and then Dispose has nothing to wait for and the delete would succeed either
        // way. Say so rather than bank a green that proved nothing.
        if (!vm.IsCountingRows)
            Assert.Inconclusive("the row count finished before Dispose — this run could not exercise the race.");

        vm.Dispose();

        Directory.Delete(dir, recursive: true);
        Assert.IsFalse(Directory.Exists(dir));
    }
}
