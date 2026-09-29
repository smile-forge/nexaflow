using Nexaflow.IO.Hdf5;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Hdf5;

/// <summary>
/// The C-order walk that streams a whole dataset through bounded memory: every element exactly once, in
/// order, and no selection over its budget.
/// </summary>
[TestClass]
[CoversNode("hdf5-windowing")]
public sealed class Hdf5SelectionTests
{
    [TestMethod]
    [DataRow(new ulong[] { 2000 }, 1024UL)]
    [DataRow(new ulong[] { 4, 16, 16 }, 100UL)]
    [DataRow(new ulong[] { 4, 16, 16 }, 16UL)]
    [DataRow(new ulong[] { 200, 300 }, 1000UL)]
    [DataRow(new ulong[] { 3, 5, 7 }, 1UL)]
    [DataRow(new ulong[] { 3, 5, 7 }, 10_000UL)]
    public void Walk_VisitsEveryElementOnce_InCOrder_WithinBudget(ulong[] shape, ulong budget)
    {
        var visited = new List<ulong>();
        foreach (var s in Hdf5Selection.Walk(shape, budget))
        {
            Assert.IsTrue(s.ElementCount <= budget, $"{s.ElementCount} > {budget}");
            foreach (var index in Indices(s)) visited.Add(Flatten(index, shape));
        }
        var total = shape.Aggregate(1UL, (n, d) => n * d);
        CollectionAssert.AreEqual(Enumerable.Range(0, (int)total).Select(i => (ulong)i).ToList(), visited);
    }

    [TestMethod]
    public void Walk_OfAScalar_IsTheEmptySelection_AndOfAnEmptyShape_IsNothing()
    {
        Assert.AreEqual(0, Hdf5Selection.Walk([], 10).Single().Rank);
        Assert.AreEqual(0, Hdf5Selection.Walk([4, 0, 2], 10).Count());
    }

    [TestMethod]
    public void Validate_RefusesAWrongRankOrAnOverrun()
    {
        var space = new Hdf5Dataspace(Hdf5SpaceKind.Simple, [10, 10], [10, 10]);
        new Hdf5Selection([0, 0], [10, 10]).Validate(space);
        Assert.ThrowsExactly<ArgumentException>(() => new Hdf5Selection([0], [1]).Validate(space));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Hdf5Selection([5, 0], [6, 1]).Validate(space));
        Assert.ThrowsExactly<ArgumentException>(() => new Hdf5Selection([0], [1]).Validate(Hdf5Dataspace.Scalar));
    }

    private static IEnumerable<ulong[]> Indices(Hdf5Selection s)
    {
        var index = s.Start.ToArray();
        while (true)
        {
            yield return (ulong[])index.Clone();
            int d = s.Rank - 1;
            while (d >= 0 && ++index[d] == s.Start[d] + s.Count[d]) { index[d] = s.Start[d]; d--; }
            if (d < 0) yield break;
        }
    }

    private static ulong Flatten(ulong[] index, ulong[] shape)
    {
        ulong flat = 0;
        for (int d = 0; d < shape.Length; d++) flat = flat * shape[d] + index[d];
        return flat;
    }
}
