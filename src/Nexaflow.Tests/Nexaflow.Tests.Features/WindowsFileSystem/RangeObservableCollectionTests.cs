using System.Collections.Specialized;
using Nexaflow.Visuals.Common.Collections;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The file list relies on RangeObservableCollection.ReplaceAll firing a single Reset
/// (not N events) so a freshly-loaded folder renders in one pass. These guard that.
/// </summary>
[TestClass]
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
}
