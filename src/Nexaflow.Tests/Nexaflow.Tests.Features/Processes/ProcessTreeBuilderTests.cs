using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Features.Processes.ViewModels;
using NSubstitute;
using static Nexaflow.Tests.Features.Processes.FakeProcessSource;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

[TestClass]
[CoversNode("tree-vs-list")]
public class ProcessTreeBuilderTests
{
    // Tree mode isn't the default (flat list is) — these tests are specifically about the hierarchy.
    private static ProcessesViewModel NewVm() =>
        new(Substitute.For<IShellServices>(), new FakeProcessSource()) { TreeMode = true };

    [TestMethod]
    public void NestsChildUnderParent()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((100, 0, "a.exe"), (200, 100, "b.exe")));
        vm.ExpandAllCommand.Execute(null);   // tree starts collapsed; expand to see the child

        var rows = vm.Rows.ToList();
        Assert.AreEqual(2, rows.Count);
        var parent = rows.First(r => r.Pid == 100);
        var child  = rows.First(r => r.Pid == 200);

        Assert.AreEqual(0, parent.Depth);
        Assert.IsTrue(parent.HasChildren);
        Assert.AreEqual(1, child.Depth);
        Assert.IsTrue(rows.IndexOf(parent) < rows.IndexOf(child), "parent must precede child in the flat list");
    }

    [TestMethod]
    public void OrphanWhoseParentIsMissing_BecomesRoot()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((200, 999, "b.exe")));   // parent 999 not present

        var row = vm.Rows.Single();
        Assert.AreEqual(0, row.Depth);
        Assert.IsFalse(row.HasChildren);
    }

    [TestMethod]
    public void ParentedToPidZeroOrFour_AreRoots()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((4, 0, "System"), (1000, 4, "smss.exe")));
        vm.ExpandAllCommand.Execute(null);

        var system = vm.Rows.First(r => r.Pid == 4);
        var smss   = vm.Rows.First(r => r.Pid == 1000);
        Assert.AreEqual(0, system.Depth);
        Assert.AreEqual(1, smss.Depth);
    }

    [TestMethod]
    public void MutualParentCycle_DoesNotRecurseForever()
    {
        using var vm = NewVm();
        // 1's parent is 2 and 2's parent is 1 — must not stack-overflow. Each link is independently
        // cycle-forming, so the safe outcome is both rooted (neither nests), and nothing is lost.
        vm.ApplySnapshot(Snap((1, 2, "a"), (2, 1, "b")));

        Assert.AreEqual(2, vm.Rows.Count);
        Assert.IsTrue(vm.Rows.All(r => r.Depth == 0), "a cycle must be broken by rooting, not by nesting");
    }

    [TestMethod]
    public void SelfParent_BecomesRoot()
    {
        using var vm = NewVm();
        vm.ApplySnapshot(Snap((42, 42, "weird")));

        var row = vm.Rows.Single();
        Assert.AreEqual(0, row.Depth);
        Assert.IsFalse(row.HasChildren);
    }
}
