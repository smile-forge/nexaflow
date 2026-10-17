using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// A list holding the reader's place keeps the row they have at the top at the top while rows come and go above it, and
/// lets go once that row is gone.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vcommon-reader-place")]
public class ReaderPlaceTests
{
    private static readonly Size Viewport = new(200, 200);

    private static (ListView List, ScrollViewer Viewer, ObservableCollection<string> Rows) Held(int count)
    {
        var rows = new ObservableCollection<string>(Enumerable.Range(0, count).Select(i => $"row {i}"));
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Row", Width = 150 });
        var list = new ListView { View = view, ItemsSource = rows };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ReaderPlace.SetIsHeld(list, true);
        Lay(list);
        return (list, ViewerOf(list), rows);
    }

    private static void Lay(ListView list)
    {
        list.Measure(Viewport);
        list.Arrange(new Rect(Viewport));
        list.UpdateLayout();
    }

    private static ScrollViewer ViewerOf(ListView list) =>
        Find(list) ?? throw new AssertFailedException("the list has no scroll viewer");

    private static ScrollViewer? Find(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as ScrollViewer ?? Find(child)) is { } viewer) return viewer;
        }
        return null;
    }

    private static string Top((ListView List, ScrollViewer Viewer, ObservableCollection<string> Rows) held) =>
        held.Rows[(int)held.Viewer.VerticalOffset];

    [TestMethod]
    public void RowsRemovedAboveTheReader_LeaveTheSameRowOnTop() => UiThread.Run(() =>
    {
        var held = Held(200);
        held.Viewer.ScrollToVerticalOffset(50);
        Lay(held.List);
        Assert.AreEqual("row 50", Top(held), "scrolled before anything changed");

        held.Rows.RemoveAt(0);
        held.Rows.RemoveAt(3);
        Lay(held.List);

        Assert.AreEqual("row 50", Top(held));
    });

    [TestMethod]
    public void RowsInsertedAboveTheReader_LeaveTheSameRowOnTop() => UiThread.Run(() =>
    {
        var held = Held(200);
        held.Viewer.ScrollToVerticalOffset(50);
        Lay(held.List);

        held.Rows.Insert(0, "new a");
        held.Rows.Insert(10, "new b");
        held.Rows.Move(5, 120);
        Lay(held.List);

        Assert.AreEqual("row 50", Top(held));
    });

    [TestMethod]
    public void OnceTheRowOnTopIsGone_TheListIsLeftToPlaceItself() => UiThread.Run(() =>
    {
        var held = Held(200);
        held.Viewer.ScrollToVerticalOffset(50);
        Lay(held.List);

        held.Rows.Remove("row 50");
        Lay(held.List);
        var placed = held.Viewer.VerticalOffset;
        held.Rows.Add("row at the end");
        Lay(held.List);

        Assert.AreEqual(placed, held.Viewer.VerticalOffset, "a row added below moves nothing once the row held is gone");
    });
}
