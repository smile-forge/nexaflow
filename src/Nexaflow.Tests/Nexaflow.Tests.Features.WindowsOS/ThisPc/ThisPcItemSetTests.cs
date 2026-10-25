using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ThisPc;

/// <summary>
/// The merge rules every "This PC" surface shares. Every row is a provider's — the volumes as much as a sync folder —
/// so the dedupe and the order happen in one place on the consumer's side, where every provider's rows meet.
/// </summary>
[TestClass]
[CoversNode("winfs-thispc-providers")]
public class ThisPcItemSetTests
{
    private sealed class FakeProvider(string id, int sortOrder, params ThisPcItem[] items) : IThisPcItemProvider
    {
        public string ProviderId => id;
        public int    SortOrder  => sortOrder;
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => items;
        public Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default)
            => Task.FromResult(new ThisPcItemDetail { Available = true, Label = "described by " + id });
    }

    private sealed class ThrowingProvider : IThisPcItemProvider
    {
        public string ProviderId => "boom";
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<ThisPcItem> GetItems() => throw new InvalidOperationException("provider is broken");
        public Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ThisPcItem Item(string id, string path, int sort = 0,
                                   ThisPcItemBacking backing = ThisPcItemBacking.LocalPath) =>
        new() { Id = id, Label = id, TargetPath = path, TypeLabel = "Test", SortOrder = sort, Backing = backing };

    /// <summary>A stand-in for the volumes' provider: sorted first, its rows browsed at their own roots.</summary>
    private static FakeProvider Volumes(params string[] roots) =>
        new("drive", 0, [.. roots.Select((root, i) => Item("drive." + i, root, i, ThisPcItemBacking.OwnPath))]);

    private static string[] Ids(IReadOnlyList<ThisPcRow> rows) => [.. rows.Select(r => r.Item.Id)];

    [TestMethod]
    public void ALocationThatIsAlreadyAVolumeIsNotListedTwice()
    {
        // The case that matters in the wild: a sync client that mounts its own drive letter is a volume AND a
        // provider's row.
        var provider = new FakeProvider("p", 10, Item("p.g", @"G:\"));

        var result = ThisPcItemSet.Collect([provider, Volumes(@"C:\", @"G:\")]);

        CollectionAssert.AreEqual(new[] { "drive.0", "drive.1" }, Ids(result), "the volume keeps the place");
    }

    [TestMethod]
    public void ATrailingSeparatorOrDifferentCasingIsStillTheSameLocation()
    {
        var provider = new FakeProvider("p", 10, Item("p.g", @"g:\"));

        CollectionAssert.AreEqual(new[] { "drive.0" }, Ids(ThisPcItemSet.Collect([provider, Volumes(@"G:")])));
    }

    [TestMethod]
    public void TheVolumesComeFirstWhateverOrderTheProvidersArriveIn()
    {
        var late = new FakeProvider("p", 10, Item("p.docs", @"C:\Docs"));

        var result = ThisPcItemSet.Collect([late, Volumes(@"C:\")]);

        CollectionAssert.AreEqual(new[] { "drive.0", "p.docs" }, Ids(result));
    }

    [TestMethod]
    public void TwoProvidersOfferingOneLocationYieldASingleRow()
    {
        var first  = new FakeProvider("a", 10, Item("a.docs", @"C:\Shared"));
        var second = new FakeProvider("b", 20, Item("b.docs", @"C:\Shared\"));

        var result = ThisPcItemSet.Collect([first, second]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("a.docs", result[0].Item.Id, "the lower-sorted provider claims the location");
    }

    [TestMethod]
    public void ARepeatedIdIsKeptOnlyOnce()
    {
        var first  = new FakeProvider("a", 10, Item("dup", @"C:\One"));
        var second = new FakeProvider("b", 20, Item("dup", @"C:\Two"));

        var result = ThisPcItemSet.Collect([first, second]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(@"C:\One", result[0].Item.TargetPath);
    }

    [TestMethod]
    public void AVirtualRowIsDroppedForAConsumerThatCanOnlyNavigateRealPaths()
    {
        var provider = new FakeProvider("p", 10,
            Item("p.local", @"C:\Local"),
            Item("p.cloud", "cloud://acct", backing: ThisPcItemBacking.Virtual));

        Assert.AreEqual(2, ThisPcItemSet.Collect([provider], allowVirtual: true).Count);

        var forPicker = ThisPcItemSet.Collect([provider], allowVirtual: false);
        Assert.AreEqual(1, forPicker.Count);
        Assert.AreEqual("p.local", forPicker[0].Item.Id);
    }

    [TestMethod]
    public void AVirtualRowIsNeverDedupedBecauseItsPathIsNotALocation()
    {
        // "cloud://x" is not a location on disk, so it must not consume a location's slot.
        var provider = new FakeProvider("p", 10,
            Item("p.a", "cloud://x", backing: ThisPcItemBacking.Virtual),
            Item("p.b", "cloud://x", backing: ThisPcItemBacking.Virtual));

        Assert.AreEqual(2, ThisPcItemSet.Collect([provider]).Count);
    }

    [TestMethod]
    public void AProviderThatThrowsIsSkippedRatherThanEmptyingThisPc()
    {
        var healthy = new FakeProvider("ok", 20, Item("ok.docs", @"C:\Docs"));

        var result = ThisPcItemSet.Collect([new ThrowingProvider(), healthy]);

        CollectionAssert.AreEqual(new[] { "ok.docs" }, Ids(result));
    }

    [TestMethod]
    public void RowsComeBackInProviderThenItemOrder()
    {
        var late  = new FakeProvider("late",  50, Item("late.b", @"C:\4", sort: 2), Item("late.a", @"C:\3", sort: 1));
        var early = new FakeProvider("early", 10, Item("early.b", @"C:\2", sort: 2), Item("early.a", @"C:\1", sort: 1));

        var result = ThisPcItemSet.Collect([late, early]);

        CollectionAssert.AreEqual(new[] { "early.a", "early.b", "late.a", "late.b" }, Ids(result));
    }

    [TestMethod]
    public void AnItemMissingAnIdOrALocationIsIgnored()
    {
        var provider = new FakeProvider("p", 10,
            Item("", @"C:\NoId"),
            Item("p.nopath", ""),
            Item("p.good", @"C:\Good"));

        CollectionAssert.AreEqual(new[] { "p.good" }, Ids(ThisPcItemSet.Collect([provider])));
    }

    [TestMethod]
    public void NoProvidersMeansNoRows()
    {
        Assert.AreEqual(0, ThisPcItemSet.Collect([]).Count, "nothing is in This PC that no provider offered");
    }

    [TestMethod]
    public async Task EveryRowIsDescribedByTheProviderThatOfferedIt()
    {
        var a = new FakeProvider("a", 10, Item("a.one", @"C:\One"));
        var b = new FakeProvider("b", 20, Item("b.two", @"C:\Two"));

        var rows = ThisPcItemSet.Collect([a, b]);

        Assert.AreEqual("described by a", (await rows[0].DetailAsync()).Label);
        Assert.AreEqual("described by b", (await rows[1].DetailAsync()).Label);
    }
}
