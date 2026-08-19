using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>
/// The per-process details page (General / Performance / Threads / Modules), fed synchronously through
/// <c>ApplyDetail</c> rather than the once-a-second timer. Two things matter beyond "the numbers show up":
/// a section Windows refused to read must render as <i>denied</i> rather than as an empty list (otherwise a
/// protected process looks like it has no threads), and a process that exits while the tab is open must
/// flip to "gone" instead of blanking. Handle loading is covered by <see cref="HandlesTests"/>.
/// </summary>
[TestClass]
public class ProcessDetailViewModelTests
{
    private static ProcessDetailViewModel Make(out IShellServices shell, int pid = 4242)
    {
        shell = Substitute.For<IShellServices>();
        return new ProcessDetailViewModel(shell, pid, new FakeProcessSource());
    }

    private static ProcessDetail Detail(
        int pid = 4242, string name = "target.exe",
        IReadOnlyList<ThreadInfo>? threads = null, bool threadsDenied = false,
        IReadOnlyList<ModuleInfo>? modules = null, bool modulesDenied = false,
        string priority = "Normal", double cpu = 3.5) => new()
    {
        Pid = pid, Name = name, Path = @"C:\Program Files\App\target.exe",
        User = "CONTOSO\\ada", Description = "Target app", Company = "Contoso",
        PriorityClass = priority, StartTime = new DateTime(2026, 7, 1, 9, 0, 0),
        CpuPercent = cpu, PrivateBytes = 1024, WorkingSet = 2048, PeakWorkingSet = 4096,
        ThreadCount = threads?.Count ?? 0, HandleCount = 7, GdiObjects = 3, UserObjects = 2,
        Threads = threads ?? [], ThreadsDenied = threadsDenied,
        Modules = modules ?? [], ModulesDenied = modulesDenied,
    };

    // ── General ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("general-info")]
    public void General_ShowsTheIdentityFields_AndTitlesTheTabByNameAndPid()
    {
        var vm = Make(out _);

        vm.ApplyDetail(Detail());

        Assert.AreEqual("target.exe (4242)", vm.Title);
        Assert.AreEqual(@"C:\Program Files\App\target.exe", vm.Detail!.Path);
        Assert.AreEqual("CONTOSO\\ada", vm.Detail.User);
        Assert.AreEqual("Contoso", vm.Detail.Company);
        Assert.IsTrue(vm.HasData);
    }

    [TestMethod]
    [CoversNode("general-info")]
    public void General_WhenTheProcessExits_FlipsToGone_RatherThanBlanking()
    {
        var vm = Make(out _);
        vm.ApplyDetail(Detail());

        vm.ApplyDetail(null);                    // the next read finds nothing

        Assert.IsTrue(vm.IsGone);
        Assert.IsNotNull(vm.Detail, "the last-known detail stays on screen so the tab isn't suddenly empty");
    }

    [TestMethod]
    [CoversNode("general-info")]
    public void OpenLocation_OpensTheImagesFolder_OrSaysWhyItCant()
    {
        var vm = Make(out var shell);

        vm.ApplyDetail(Detail() with { Path = "" });
        vm.OpenLocationCommand.Execute(null);
        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("image path")));

        vm.ApplyDetail(Detail());
        vm.OpenLocationCommand.Execute(null);
        shell.Received(1).OpenTab("FileSystem",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\Program Files\App"),
            Arg.Any<IPageView?>());
    }

    // ── Priority ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("set-priority")]
    public void Priority_SelectorMirrorsTheProcess_WithoutReApplyingWhatItJustRead()
    {
        var vm = Make(out var shell);

        vm.ApplyDetail(Detail(priority: "High"));

        Assert.AreEqual("High", vm.SelectedPriority, "the dropdown reflects the live priority class");
        shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    [TestMethod]
    [CoversNode("set-priority")]
    public void Priority_OffersTheWindowsClasses()
    {
        var vm = Make(out _);

        CollectionAssert.IsSubsetOf(new[] { "Idle", "Normal", "High", "RealTime" },
                                    vm.PriorityClasses.ToArray());
    }

    // ── Performance ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("performance")]
    public void Performance_AccumulatesACpuHistory_AndScalesTheChartToIt()
    {
        var vm = Make(out _);

        vm.ApplyDetail(Detail(cpu: 4));
        vm.ApplyDetail(Detail(cpu: 9));
        vm.ApplyDetail(Detail(cpu: 2));

        CollectionAssert.AreEqual(new[] { 4d, 9d, 2d }, vm.CpuHistory.ToArray());
        Assert.IsTrue(vm.CpuHistoryMax >= 9, "the chart's ceiling must cover the tallest sample");
    }

    [TestMethod]
    [CoversNode("performance")]
    public void Performance_CountersComeStraightFromTheDetail()
    {
        var vm = Make(out _);

        vm.ApplyDetail(Detail());

        Assert.AreEqual(1024, vm.Detail!.PrivateBytes);
        Assert.AreEqual(2048, vm.Detail.WorkingSet);
        Assert.AreEqual(4096, vm.Detail.PeakWorkingSet);
        Assert.AreEqual(7, vm.Detail.HandleCount);
    }

    // ── Threads ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("threads")]
    public void Threads_ReconcileByTid_SoTheListDoesntResetEachTick()
    {
        var vm = Make(out _);
        vm.ApplyDetail(Detail(threads: [new ThreadInfo { Tid = 1 }, new ThreadInfo { Tid = 2 }]));
        var first = vm.Threads.Single(t => t.Tid == 1);

        vm.ApplyDetail(Detail(threads: [new ThreadInfo { Tid = 1 }, new ThreadInfo { Tid = 3 }]));

        Assert.AreSame(first, vm.Threads.Single(t => t.Tid == 1), "an unchanged thread keeps its row");
        CollectionAssert.AreEquivalent(new[] { 1, 3 }, vm.Threads.Select(t => t.Tid).ToArray());
    }

    [TestMethod]
    [CoversNode("threads")]
    public void Threads_DeniedByWindows_IsFlagged_NotShownAsNoThreads()
    {
        var vm = Make(out _);

        vm.ApplyDetail(Detail(threads: [], threadsDenied: true));

        Assert.IsTrue(vm.ThreadsDenied, "a protected process must say 'denied', not 'zero threads'");
        Assert.AreEqual(0, vm.Threads.Count);
    }

    // ── Modules ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("modules")]
    public void Modules_SortByColumn_AndTheSameHeaderReverses()
    {
        var vm = Make(out _);
        vm.ApplyDetail(Detail(modules:
        [
            new ModuleInfo { Name = "zlib.dll",  Path = @"C:\a\zlib.dll" },
            new ModuleInfo { Name = "advapi.dll", Path = @"C:\a\advapi.dll" },
        ]));

        Assert.AreEqual(nameof(ModuleInfo.Name), vm.ModuleSortColumn);
        Assert.IsTrue(vm.ModuleSortAscending);
        CollectionAssert.AreEqual(new[] { "advapi.dll", "zlib.dll" },
                                  vm.ModulesView.Cast<ModuleInfo>().Select(m => m.Name).ToArray());

        vm.SortModulesCommand.Execute(nameof(ModuleInfo.Name));

        Assert.IsFalse(vm.ModuleSortAscending);
        CollectionAssert.AreEqual(new[] { "zlib.dll", "advapi.dll" },
                                  vm.ModulesView.Cast<ModuleInfo>().Select(m => m.Name).ToArray());
    }

    [TestMethod]
    [CoversNode("modules")]
    public void Modules_DeniedByWindows_IsFlagged_NotShownAsNoModules()
    {
        var vm = Make(out _);

        vm.ApplyDetail(Detail(modules: [], modulesDenied: true));

        Assert.IsTrue(vm.ModulesDenied);
        Assert.AreEqual(0, vm.Modules.Count);
    }

    [TestMethod]
    [CoversNode("modules")]
    public void OpenModuleLocation_OpensTheModulesFolder_OrSaysWhyItCant()
    {
        var vm = Make(out var shell);

        vm.OpenModuleLocationCommand.Execute(new ModuleInfo { Name = "x.dll", Path = "" });
        shell.Received().ShowError(Arg.Is<string>(m => m.Contains("module path")));

        vm.OpenModuleLocationCommand.Execute(new ModuleInfo { Name = "x.dll", Path = @"C:\libs\x.dll" });
        shell.Received(1).OpenTab("FileSystem",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\libs"),
            Arg.Any<IPageView?>());
    }

    // ── Kill from the details page ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("details-kill")]
    public async Task Kill_ConfirmsFirst_AndDeclineTerminatesNothing()
    {
        var vm = Make(out var shell);
        vm.ApplyDetail(Detail());
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(false));

        await vm.KillCommand.ExecuteAsync(null);

        await shell.Received().ConfirmAsync(
            Arg.Is<string>(t => t.Contains("target.exe") && t.Contains("4242")),
            Arg.Is<string>(b => b.Contains("Unsaved work is lost")),
            Arg.Any<CancellationToken>());
        shell.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default);
    }

    // ── Re-targeting the tab ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("details-view")]
    public void Reinitialize_ToAnotherPid_ClearsTheOldProcessesLists()
    {
        var vm = Make(out _);
        vm.ApplyDetail(Detail(threads: [new ThreadInfo { Tid = 1 }],
                              modules: [new ModuleInfo { Name = "a.dll", Path = @"C:\a.dll" }]));
        Assert.AreEqual(1, vm.Threads.Count);

        vm.Reinitialize(999);

        Assert.AreEqual(999, vm.Pid);
        Assert.AreEqual(0, vm.Threads.Count, "the previous process's threads must not linger");
        Assert.AreEqual(0, vm.Modules.Count);
        Assert.IsFalse(vm.HandlesLoaded);
    }
}
