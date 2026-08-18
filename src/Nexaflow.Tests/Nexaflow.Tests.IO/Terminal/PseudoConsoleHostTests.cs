using System.Collections.Generic;
using System.Linq;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>
/// The foreground hard-stop (Ctrl+Break) kills the shell's descendant process tree while sparing the shell.
/// The tree walk that decides <em>what</em> to kill is pure and covered here; the native termination itself is
/// verified manually.
/// </summary>
[TestClass]
[CoversNode("ioterm-job")]
public class PseudoConsoleHostTests
{
    private static IReadOnlyList<uint> Descendants(
        uint root, params (uint Pid, uint ParentPid)[] processes)
        => PseudoConsoleHostService.ComputeDescendants(root, processes);

    [TestMethod]
    public void ComputeDescendants_ReturnsWholeTree_ExcludingRoot()
    {
        // shell(100) → git(200) → less(300); an unrelated tree 400→500 must be untouched.
        var result = Descendants(100, (100, 4), (200, 100), (300, 200), (400, 4), (500, 400));

        CollectionAssert.AreEquivalent(new uint[] { 200, 300 }, result.ToArray());
    }

    [TestMethod]
    public void ComputeDescendants_MultipleChildrenAtSameLevel()
    {
        var result = Descendants(100, (200, 100), (201, 100), (300, 200));

        CollectionAssert.AreEquivalent(new uint[] { 200, 201, 300 }, result.ToArray());
    }

    [TestMethod]
    public void ComputeDescendants_NoChildren_ReturnsEmpty()
        => Assert.AreEqual(0, Descendants(100, (400, 4), (500, 400)).Count);

    [TestMethod]
    public void ComputeDescendants_ExcludesRootItself()
        => CollectionAssert.DoesNotContain(Descendants(100, (200, 100)).ToArray(), 100u);

    [TestMethod]
    public void ComputeDescendants_StaleParentCycle_Terminates()
    {
        // 200↔300 form a parent cycle (PID reuse can produce this); the seen-set must stop the walk.
        var result = Descendants(100, (200, 100), (300, 200), (200, 300));

        Assert.IsTrue(result.Contains(200u));
        Assert.IsTrue(result.Contains(300u));
        Assert.AreEqual(result.Distinct().Count(), result.Count);   // no pid visited twice
    }

    [TestMethod]
    public void ComputeDescendants_SelfParentEntry_Ignored()
    {
        // (0,0) — the idle/system pseudo-process — must not be treated as its own child.
        var result = Descendants(100, (0, 0), (200, 100));

        CollectionAssert.AreEquivalent(new uint[] { 200 }, result.ToArray());
    }
}
