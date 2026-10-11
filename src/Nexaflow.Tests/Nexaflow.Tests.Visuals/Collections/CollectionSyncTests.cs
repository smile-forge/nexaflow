using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Nexaflow.Tests.Visuals.Collections;

/// <summary>
/// <see cref="CollectionSync.SyncTo{T}"/>: a bound list brought to a new one by changing only what differs, so an item
/// that is still there keeps the row, and the automation peer, a view made for it.
/// </summary>
[TestClass]
[CoversNode("vcommon-collections")]
public class CollectionSyncTests
{
    /// <summary>Compares by value, as the records a view-model lists usually do.</summary>
    private sealed record Row(string Id);

    [TestMethod]
    public void AnUnchangedList_RaisesNoChangeAtAll()
    {
        var list = new ObservableCollection<Row> { new("a"), new("b") };
        var changes = 0;
        list.CollectionChanged += (_, _) => changes++;

        list.SyncTo([new Row("a"), new Row("b")]);

        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void AnItemStillListed_KeepsItsInstance_EvenWhenAnEqualOneIsHandedIn()
    {
        var alpha = new Row("a");
        var list = new ObservableCollection<Row> { alpha };

        list.SyncTo([new Row("a")]);

        Assert.AreSame(alpha, list[0], "the equal record handed in must not replace the one the view already shows");
    }

    [TestMethod]
    public void TheResult_IsTheNewList_InItsOrder()
    {
        var list = new ObservableCollection<Row> { new("a"), new("b"), new("c"), new("d") };

        list.SyncTo([new Row("d"), new Row("b"), new Row("e")]);

        CollectionAssert.AreEqual(new[] { "d", "b", "e" }, list.Select(r => r.Id).ToArray());
    }

    [TestMethod]
    public void ItemsAreMovedInsertedAndRemoved_NeverReset()
    {
        var list = new ObservableCollection<Row> { new("a"), new("b"), new("c") };
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);

        list.SyncTo([new Row("c"), new Row("a"), new Row("x")]);

        CollectionAssert.DoesNotContain(actions, NotifyCollectionChangedAction.Reset);
        CollectionAssert.AreEqual(new[] { "c", "a", "x" }, list.Select(r => r.Id).ToArray());
    }

    [TestMethod]
    public void AnEmptyList_EmptiesTheCollection()
    {
        var list = new ObservableCollection<Row> { new("a"), new("b") };

        list.SyncTo([]);

        Assert.AreEqual(0, list.Count);
    }
}
