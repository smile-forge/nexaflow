using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>
/// The Processes tab's controls, driven off an in-memory <see cref="FakeProcessSource"/> — no live system,
/// nothing killed. The theme here is that every action which can destroy a user's unsaved work or fail
/// silently is guarded first: Kill confirms and does nothing when declined, Open Location and Copy Path
/// surface an error rather than a no-op when the image path isn't readable, and the search box's clear
/// button restores the full list.
///
/// Sampling, tree building and snapshot reconciliation are asserted at their own Functionality nodes
/// (<see cref="CpuSamplingTests"/>, <see cref="ProcessTreeBuilderTests"/>, <see cref="ReconciliationTests"/>).
/// </summary>
[TestClass]
public class ProcessesSurfaceTests
{
    /// <summary>
    /// A view-model seeded with one synchronous snapshot. The live path samples on a background thread off
    /// a timer, so tests fold the snapshot in directly (the same seam <see cref="ReconciliationTests"/>
    /// uses) rather than racing the refresh.
    /// </summary>
    private static ProcessesViewModel Build(out IShellServices shell, out FakeProcessSource source,
                                            params (int pid, int parent, string name)[] procs)
    {
        shell  = Substitute.For<IShellServices>();
        source = new FakeProcessSource();
        var vm = new ProcessesViewModel(shell, source);
        vm.ApplySnapshot(FakeProcessSource.Snap(procs) with
        {
            SystemCpuPercent  = 12,
            MemoryLoadPercent = 47,
        });
        return vm;
    }

    private static ProcessRowViewModel Row(ProcessesViewModel vm, string name)
        => vm.Rows.Single(r => r.Name == name);

    // ── Search / filter ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("filter")]
    public void Filter_NarrowsTheList_AndClearRestoresIt()
    {
        var vm = Build(out _, out _, (10, 0, "chrome.exe"), (11, 0, "code.exe"), (12, 0, "chromium.exe"));
        Assert.AreEqual(3, vm.Rows.Count, "precondition: all three rows are listed");

        vm.FilterText = "chrom";
        CollectionAssert.AreEquivalent(new[] { "chrome.exe", "chromium.exe" },
                                       vm.Rows.Select(r => r.Name).ToArray());

        vm.ClearFilterCommand.Execute(null);
        Assert.AreEqual(string.Empty, vm.FilterText);
        Assert.AreEqual(3, vm.Rows.Count);
    }

    // ── View controls ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("live-vs-pause")]
    public void LiveToggle_StartsOn_AndPausingIsJustAFlag_NotADataReset()
    {
        var vm = Build(out _, out _, (10, 0, "a.exe"));
        Assert.IsTrue(vm.AutoRefreshEnabled, "the tab opens live");

        vm.AutoRefreshEnabled = false;

        Assert.IsFalse(vm.AutoRefreshEnabled);
        Assert.AreEqual(1, vm.Rows.Count, "pausing freezes updates — it must not empty the list");
    }

    [TestMethod]
    [CoversNode("expand-collapse-all")]
    public void ExpandAll_RevealsChildren_CollapseAllHidesThemAgain()
    {
        var vm = Build(out _, out _, (10, 0, "parent.exe"), (20, 10, "child.exe"));
        Assert.IsTrue(vm.TreeMode, "the tree is the default view");

        vm.ExpandAllCommand.Execute(null);
        CollectionAssert.Contains(vm.Rows.Select(r => r.Name).ToArray(), "child.exe");

        vm.CollapseAllCommand.Execute(null);
        CollectionAssert.DoesNotContain(vm.Rows.Select(r => r.Name).ToArray(), "child.exe");
        Assert.AreEqual(1, vm.Rows.Count, "only the root stays visible when everything is collapsed");
    }

    [TestMethod]
    [CoversNode("process-toolbar")]
    [CoversNode("tree-vs-list")]
    public void FlatMode_ShowsEveryProcess_RegardlessOfExpansion()
    {
        var vm = Build(out _, out _, (10, 0, "parent.exe"), (20, 10, "child.exe"));
        vm.CollapseAllCommand.Execute(null);

        vm.TreeMode = false;

        Assert.AreEqual(2, vm.Rows.Count, "a flat list has no hierarchy to hide behind");
    }

    // ── Row menu: view details ────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("processes-view-details")]
    public void ViewDetails_OpensTheDetailsTab_ForThatPid()
    {
        var vm = Build(out var shell, out _, (4242, 0, "target.exe"));

        vm.ViewDetailsCommand.Execute(Row(vm, "target.exe"));

        shell.Received(1).OpenTab(Arg.Any<string>(),
            Arg.Is<Dictionary<string, string>>(p => p["pid"] == "4242"),
            Arg.Any<IPageView?>());
    }

    [TestMethod]
    [CoversNode("processes-view-details")]
    public void ViewDetails_WithNoRow_IsANoOp()
    {
        var vm = Build(out var shell, out _, (10, 0, "a.exe"));

        vm.ViewDetailsCommand.Execute(null);

        shell.DidNotReceiveWithAnyArgs().OpenTab(default!, default, default, default);
    }

    // ── Row menu: kill ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("kill-process")]
    public async Task Kill_ConfirmsFirst_AndDeclineTerminatesNothing()
    {
        var vm = Build(out var shell, out _, (4242, 0, "target.exe"));
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));

        await vm.KillCommand.ExecuteAsync(Row(vm, "target.exe"));

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("target.exe") && t.Contains("4242")),
            Arg.Is<string>(b => b.Contains("Unsaved work is lost")),
            Arg.Any<CancellationToken>());
        _ = shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    [CoversNode("kill-process")]
    public async Task KillTree_WarnsThatChildrenGoToo()
    {
        var vm = Build(out var shell, out _, (4242, 0, "target.exe"));
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));

        await vm.KillTreeCommand.ExecuteAsync(Row(vm, "target.exe"));

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("child processes")),
            Arg.Is<string>(b => b.Contains("every process it started")),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [CoversNode("kill-process")]
    public async Task Kill_WithNoRow_NeverEvenAsks()
    {
        var vm = Build(out var shell, out _, (10, 0, "a.exe"));

        await vm.KillCommand.ExecuteAsync(null);

        await shell.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default);
    }

    // ── Row menu: open file location ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("open-file-location")]
    public void OpenLocation_OpensTheImagesFolder_OrSaysWhyItCant()
    {
        var vm = Build(out var shell, out var source, (10, 0, "known.exe"), (11, 0, "unknown.exe"));
        source.DetailFunc = pid => pid == 10
            ? new Nexaflow.Features.Processes.Models.ProcessDetail
              { Pid = 10, Name = "known.exe", Path = @"C:\Windows\System32\known.exe" }
            : null;

        vm.OpenLocationCommand.Execute(Row(vm, "known.exe"));
        shell.Received(1).OpenTab("FileSystem",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\Windows\System32"),
            Arg.Any<IPageView?>());

        vm.OpenLocationCommand.Execute(Row(vm, "unknown.exe"));
        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("unknown.exe")));
    }

    // ── Row menu: copy ────────────────────────────────────────────────────────

    /// <summary>
    /// The copy entries write the system clipboard (a machine-global WPF static, not assertable here), so
    /// what this pins down is the guard in front of it: copying a path the tab never resolved must report
    /// that rather than silently putting an empty string on the clipboard.
    /// </summary>
    [TestMethod]
    [CoversNode("copy-process-info")]
    public void CopyPath_WithNoResolvedImagePath_ReportsItInsteadOfCopyingNothing()
    {
        var vm = Build(out var shell, out _, (10, 0, "noPath.exe"));

        vm.CopyPathCommand.Execute(Row(vm, "noPath.exe"));

        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("noPath.exe")));
    }

    [TestMethod]
    [CoversNode("copy-process-info")]
    public void CopyCommands_WithNoRow_AreNoOps()
    {
        var vm = Build(out var shell, out _, (10, 0, "a.exe"));

        vm.CopyNameCommand.Execute(null);
        vm.CopyPidCommand.Execute(null);
        vm.CopyPathCommand.Execute(null);
        vm.CopyDetailsCommand.Execute(null);

        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    // ── System gauges ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("system-gauges")]
    public void SystemGauges_HaveAHistoryBufferAndAPercentage_AfterTheFirstSample()
    {
        var vm = Build(out _, out _, (10, 0, "a.exe"));

        Assert.IsNotNull(vm.CpuHistory);
        Assert.IsNotNull(vm.MemoryHistory);
        Assert.IsTrue(vm.SystemCpuPercent is >= 0 and <= 100, "a CPU gauge only ever reads 0–100%");
        Assert.IsTrue(vm.MemoryLoadPercent is >= 0 and <= 100);
    }

    // ── Header counts ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("processes-counts")]
    public void HeaderCounts_ShowTheFilteredCountAgainstTheTotal()
    {
        var vm = Build(out _, out _, (10, 0, "chrome.exe"), (11, 0, "code.exe"), (12, 0, "chromium.exe"));
        Assert.AreEqual(3, vm.ProcessCount);

        vm.FilterText = "chrom";

        Assert.AreEqual(2, vm.Rows.Count, "the 'shown' count follows the filter…");
        Assert.AreEqual(3, vm.ProcessCount, "…while the total keeps reporting every running process");
    }
}
