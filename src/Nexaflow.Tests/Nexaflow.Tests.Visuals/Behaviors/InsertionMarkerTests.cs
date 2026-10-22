using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// The insertion line: one adorner on the list, standing in the gap it was shown at — midway across the margin
/// between two items, on the edge past the last — moved rather than re-added, and gone once cleared. Also the gap a
/// laid-out list reads under the pointer, a wrapped one included.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vcommon-insertion-marker")]
[CoversNode("vcommon-drag-reorder")]
public class InsertionMarkerTests
{
    /// <summary>A list of 20-high rows with 10 between them, inside the adorner decorator a window would give it.</summary>
    static ListBox Laid(out ListBoxItem[] rows, int count = 3)
    {
        var list = new ListBox();
        rows = new ListBoxItem[count];
        for (var i = 0; i < count; i++)
            list.Items.Add(rows[i] = new ListBoxItem { Content = $"row {i}", Height = 20, Margin = new Thickness(0, 0, 0, 10) });

        var decorator = new AdornerDecorator { Child = list, Width = 200, Height = 300 };
        decorator.Measure(new Size(200, 300));
        decorator.Arrange(new Rect(0, 0, 200, 300));
        decorator.UpdateLayout();
        return list;
    }

    static double Top(UIElement row, UIElement list)    => row.TranslatePoint(new Point(), list).Y;
    static double Bottom(UIElement row, UIElement list) => Top(row, list) + row.RenderSize.Height;

    static Adorner[] Adorners(UIElement list) => AdornerLayer.GetAdornerLayer(list)?.GetAdorners(list) ?? [];

    /// <summary>Where the line was drawn, along the list's vertical axis.</summary>
    static double LineY(Adorner adorner)
    {
        AdornerLayer.GetAdornerLayer(adorner.AdornedElement)!.UpdateLayout();
        var drawn = VisualTreeHelper.GetDrawing(adorner);
        Assert.IsNotNull(drawn, "the line was rendered");
        return (drawn.Bounds.Top + drawn.Bounds.Bottom) / 2;
    }

    [TestMethod]
    public void Show_AddsOneLine_MidwayAcrossTheGap() => UiThread.Run(() =>
    {
        var list = Laid(out var rows);
        var marker = new InsertionMarker();

        marker.Show(list, 1);

        var adorners = Adorners(list);
        Assert.AreEqual(1, adorners.Length);
        Assert.IsFalse(adorners[0].IsHitTestVisible, "a drop goes through it to the list");
        Assert.AreEqual(1, marker.Gap);
        Assert.AreEqual((Bottom(rows[0], list) + Top(rows[1], list)) / 2, LineY(adorners[0]), 0.5);
    });

    [TestMethod]
    public void ShowingAnotherGap_MovesTheSameLine() => UiThread.Run(() =>
    {
        var list = Laid(out var rows);
        var marker = new InsertionMarker();
        marker.Show(list, 1);
        var first = Adorners(list).Single();
        var firstY = LineY(first);

        marker.Show(list, 3);

        var moved = Adorners(list).Single();
        Assert.AreSame(first, moved);
        Assert.AreEqual(3, marker.Gap);
        Assert.AreEqual(Bottom(rows[2], list), LineY(moved), 0.5, "past the last row, on its edge");
        Assert.AreNotEqual(firstY, LineY(moved));
    });

    [TestMethod]
    public void Clear_TakesItDown() => UiThread.Run(() =>
    {
        var list = Laid(out _);
        var marker = new InsertionMarker();
        marker.Show(list, 0);

        marker.Clear();

        Assert.AreEqual(0, Adorners(list).Length);
        Assert.IsNull(marker.Gap);
        marker.Clear();   // twice is harmless
    });

    [TestMethod]
    public void AGapWithNoItemOnScreen_ShowsNothing() => UiThread.Run(() =>
    {
        var list = Laid(out _, count: 0);
        var marker = new InsertionMarker();

        marker.Show(list, 0);

        Assert.AreEqual(0, Adorners(list).Length);
        Assert.IsNull(marker.Gap);
    });

    [TestMethod]
    public void GapAt_ReadsTheMarginsAndThePastTheEnd() => UiThread.Run(() =>
    {
        var list = Laid(out var rows);
        double At(int row, double offset) => Top(rows[row], list) + offset;

        Assert.AreEqual(0, ItemGaps.GapAt(list, new Point(50, At(0, 3))));
        Assert.AreEqual(1, ItemGaps.GapAt(list, new Point(50, At(0, 25))), "the margin under row 0");
        Assert.AreEqual(2, ItemGaps.GapAt(list, new Point(50, At(1, 18))));
        Assert.AreEqual(3, ItemGaps.GapAt(list, new Point(50, 280)), "the empty space after the last");
    });

    [TestMethod]
    public void GapAt_InAWrapPanel_ReadsTheLineUnderThePointer() => UiThread.Run(() =>
    {
        // Two 50-wide tiles to a line: tiles 0 1 on the first, 2 3 on the second.
        var list = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))),
        };
        for (var i = 0; i < 4; i++) list.Items.Add(new Border { Width = 50, Height = 40 });
        list.Measure(new Size(120, 200));
        list.Arrange(new Rect(0, 0, 120, 200));
        list.UpdateLayout();

        Assert.AreEqual(2, ItemGaps.GapAt(list, new Point(110, 20)), "past the end of the first line");
        Assert.AreEqual(2, ItemGaps.GapAt(list, new Point(10, 60)), "the start of the second");
        Assert.AreEqual(3, ItemGaps.GapAt(list, new Point(30, 60)));
        Assert.AreEqual(4, ItemGaps.GapAt(list, new Point(110, 150)), "below everything, the end of the last line");
    });
}
