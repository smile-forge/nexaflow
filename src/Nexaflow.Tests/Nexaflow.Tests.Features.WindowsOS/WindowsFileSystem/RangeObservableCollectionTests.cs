using System.Collections.Specialized;
using Nexaflow.Visuals.Common.Collections;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The file list relies on RangeObservableCollection.ReplaceAll firing a single Reset
/// (not N events) so a freshly-loaded folder renders in one pass. These guard that.
/// </summary>
[TestClass]
[CoversNode("vcommon-collections")]
public class RangeObservableCollectionTests
{
    private static (RangeObservableCollection<int> Coll, List<NotifyCollectionChangedEventArgs> Events) Track()
    {
        var coll   = new RangeObservableCollection<int>();
        var events = new List<NotifyCollectionChangedEventArgs>();
        coll.CollectionChanged += (_, e) => events.Add(e);
        return (coll, events);
    }

    [TestMethod]
    public void ReplaceAll_RaisesExactlyOneResetEvent()
    {
        var (coll, events) = Track();

        coll.ReplaceAll([1, 2, 3, 4, 5]);

        Assert.AreEqual(1, events.Count, "Expected a single notification.");
        Assert.AreEqual(NotifyCollectionChangedAction.Reset, events[0].Action);
    }

    [TestMethod]
    public void ReplaceAll_ReplacesPriorContents()
    {
        var (coll, _) = Track();
        coll.ReplaceAll([1, 2, 3]);

        coll.ReplaceAll([9, 8]);

        CollectionAssert.AreEqual(new[] { 9, 8 }, coll);
    }

    [TestMethod]
    public void ReplaceAll_Empty_ClearsCollection()
    {
        var (coll, _) = Track();
        coll.ReplaceAll([1, 2, 3]);

        coll.ReplaceAll([]);

        Assert.AreEqual(0, coll.Count);
    }

    [TestMethod]
    public void AddRange_AppendsAndRaisesOneReset()
    {
        var (coll, events) = Track();
        coll.Add(1);            // base Add → its own event
        events.Clear();

        coll.AddRange([2, 3, 4]);

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(NotifyCollectionChangedAction.Reset, events[0].Action);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, coll);
    }

    // ── MatchTo ───────────────────────────────────────────────────────────────

    /// <summary>An item as a reload reads it: a key, and what this reading of it said.</summary>
    private sealed class Row(string key, int reading)
    {
        public string Key { get; } = key;
        public int Reading { get; set; } = reading;
    }

    private static (RangeObservableCollection<Row> Coll, List<NotifyCollectionChangedEventArgs> Events) Holding(params string[] keys)
    {
        var coll = new RangeObservableCollection<Row>();
        foreach (var key in keys) coll.Add(new Row(key, 1));
        var events = new List<NotifyCollectionChangedEventArgs>();
        coll.CollectionChanged += (_, e) => events.Add(e);
        return (coll, events);
    }

    private static void MatchTo(RangeObservableCollection<Row> coll, params string[] keys) =>
        coll.MatchTo([.. keys.Select(key => new Row(key, 2))], row => row.Key, StringComparer.Ordinal,
                     (kept, fresh) => { kept.Reading = fresh.Reading; return true; });

    [TestMethod]
    public void MatchTo_SaysEachDifference_AndKeepsTheRowsItAlreadyHolds()
    {
        var (coll, events) = Holding("a", "b", "c", "d");
        var (a, c, d) = (coll[0], coll[2], coll[3]);

        MatchTo(coll, "a", "c", "e", "d");

        CollectionAssert.AreEqual(new[] { "a", "c", "e", "d" }, coll.Select(row => row.Key).ToArray());
        CollectionAssert.AreEqual(new[] { (NotifyCollectionChangedAction.Remove, 1), (NotifyCollectionChangedAction.Add, 2) },
                                  events.Select(e => (e.Action, e.Action == NotifyCollectionChangedAction.Remove ? e.OldStartingIndex : e.NewStartingIndex)).ToArray(),
                                  "one removal and one insertion, never a Reset");
        Assert.AreSame(a, coll[0]);
        Assert.AreSame(c, coll[1]);
        Assert.AreSame(d, coll[3], "a row that stays is the same row, so a view keeps it where it is");
        Assert.IsTrue(coll.All(row => row.Reading == 2), "and every row that stays takes on the fresh reading");
    }

    [TestMethod]
    public void MatchTo_MovesWhatChangedPlace_RatherThanReplacingIt()
    {
        var (coll, events) = Holding("a", "b", "c");
        var held = coll.ToArray();

        MatchTo(coll, "c", "a", "b");

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, coll.Select(row => row.Key).ToArray());
        Assert.IsTrue(events.All(e => e.Action == NotifyCollectionChangedAction.Move), "a new order is moves");
        CollectionAssert.AreEquivalent(held, coll.ToArray(), "of the same rows");
    }

    [TestMethod]
    public void MatchTo_PutsTheFreshRowInPlace_WhereTheHeldOneCannotStandForIt()
    {
        var (coll, events) = Holding("a", "b");
        var fresh = new Row("b", 2);

        coll.MatchTo([new Row("a", 2), fresh], row => row.Key, StringComparer.Ordinal, (kept, _) => kept.Key != "b");

        Assert.AreSame(fresh, coll[1]);
        Assert.AreEqual(NotifyCollectionChangedAction.Replace, events.Single().Action);
    }

    [TestMethod]
    public void MatchTo_KeysAreComparedAsItIsTold()
    {
        var (coll, events) = Holding("Photo.JPG");
        var held = coll[0];

        coll.MatchTo([new Row("photo.jpg", 2)], row => row.Key, StringComparer.OrdinalIgnoreCase, (kept, _) => true);

        Assert.AreSame(held, coll[0], "the same path read in another case is the same row");
        Assert.AreEqual(0, events.Count);
    }
}
