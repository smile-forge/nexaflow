using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.ViewModels;
using NSubstitute;
using static Nexaflow.Tests.Features.Processes.FakeProcessSource;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

[TestClass]
public class ReconciliationTests
{
    private static ProcessesViewModel NewVm() =>
        new(Substitute.For<IShellServices>(), new FakeProcessSource());

    [TestMethod]
    [CoversNode("processes-reconcile")]
    public void RowIdentity_SurvivesRefresh()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 0, "b")));
        var a = vm.Rows.First(r => r.Pid == 1);

        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 0, "b")));
        Assert.AreSame(a, vm.Rows.First(r => r.Pid == 1), "the same PID must reuse the same row instance");
    }

    [TestMethod]
    [CoversNode("processes-reconcile")]
    public void DeadProcess_IsRemoved()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 0, "b")));
        Assert.AreEqual(2, vm.Rows.Count);

        vm.ApplySnapshot(Snap((1, 0, "a")));
        Assert.AreEqual(1, vm.Rows.Count);
        Assert.AreEqual(1, vm.Rows.Single().Pid);
    }

    [TestMethod]
    [CoversNode("processes-reconcile")]
    public void Metrics_UpdateInPlace()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(new ProcessSnapshot { Processes = [Sample(1, "a", cpu: 5)], ProcessorCount = 4 });
        var a = vm.Rows.Single();

        vm.ApplySnapshot(new ProcessSnapshot { Processes = [Sample(1, "a", cpu: 50)], ProcessorCount = 4 });
        Assert.AreSame(a, vm.Rows.Single());
        Assert.AreEqual(50, a.CpuPercent);
    }

    [TestMethod]
    [CoversNode("processes-reconcile")]
    public void ExpansionState_SurvivesRefresh()
    {
        using var vm = NewVm();
        vm.TreeMode = true;
        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 1, "b")));
        var parent = vm.Rows.First(r => r.Pid == 1);

        // Tree starts collapsed → the child is hidden.
        Assert.IsFalse(parent.IsExpanded);
        Assert.AreEqual(1, vm.Rows.Count);

        vm.ToggleExpandCommand.Execute(parent);     // expand
        Assert.IsTrue(parent.IsExpanded);
        Assert.AreEqual(2, vm.Rows.Count);          // child now visible

        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 1, "b")));
        Assert.AreSame(parent, vm.Rows.First(r => r.Pid == 1));
        Assert.IsTrue(parent.IsExpanded);           // expansion preserved across refresh
        Assert.AreEqual(2, vm.Rows.Count);
    }

    [TestMethod]
    [CoversNode("process-columns")]
    public void Sort_ByCpuDescByDefault_ThenByNameOnHeaderClick()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(new ProcessSnapshot
        {
            Processes      = [Sample(1, "a", cpu: 1), Sample(2, "b", cpu: 9)],
            ProcessorCount = 4,
        });
        Assert.AreEqual(2, vm.Rows[0].Pid, "CPU descending by default → the busier process first");

        vm.SortByCommand.Execute(nameof(ProcessRowViewModel.Name));
        Assert.AreEqual(1, vm.Rows[0].Pid, "name ascending → a before b");
    }

    [TestMethod]
    [CoversNode("tree-vs-list")]
    public void FlatMode_DropsHierarchy()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((1, 0, "a"), (2, 1, "b")));
        vm.TreeMode = false;

        Assert.IsTrue(vm.Rows.All(r => r.Depth == 0), "flat mode must zero every depth");
    }
}
