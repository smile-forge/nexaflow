using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The tile panel's promises: one column when narrow, more as it widens, each tile to the shortest column, and a wide
/// tile across them all below what came before it.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vcommon-column-tile-panel")]
public class ColumnTilePanelTests
{
    static Border Tile(double height, bool wide = false)
    {
        var tile = new Border { Height = height };
        ColumnTilePanel.SetIsWide(tile, wide);
        return tile;
    }

    static ColumnTilePanel Laid(double width, params Border[] tiles)
    {
        var panel = new ColumnTilePanel { MinColumnWidth = 300, Spacing = 10 };
        foreach (var tile in tiles) panel.Children.Add(tile);
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return panel;
    }

    static Rect Placed(UIElement tile) => new(tile.TranslatePoint(new Point(), (UIElement)VisualTreeParent(tile)), tile.RenderSize);

    static DependencyObject VisualTreeParent(UIElement tile) => System.Windows.Media.VisualTreeHelper.GetParent(tile);

    [TestMethod]
    public void Narrow_TheTilesStack() => UiThread.Run(() =>
    {
        var a = Tile(100);
        var b = Tile(50);
        var panel = Laid(400, a, b);

        Assert.AreEqual(1, panel.ColumnsFor(400));
        Assert.AreEqual(new Rect(0, 0, 400, 100), Placed(a));
        Assert.AreEqual(new Rect(0, 110, 400, 50), Placed(b));
        Assert.AreEqual(160, panel.DesiredSize.Height);
    });

    [TestMethod]
    public void Wide_EachTileGoesToTheShortestColumn() => UiThread.Run(() =>
    {
        var a = Tile(200);
        var b = Tile(50);
        var c = Tile(80);
        Laid(930, a, b, c);   // three 303-pixel columns

        Assert.AreEqual(0, Placed(a).X);
        Assert.AreEqual(313.33, Placed(b).X, 0.5);
        Assert.AreEqual(626.67, Placed(c).X, 0.5);

        var d = Tile(30);
        var panel = Laid(620, Tile(200), Tile(50), Tile(80), d);   // two columns: d goes under the 50, the shorter
        Assert.AreEqual(2, panel.ColumnsFor(620));
        Assert.AreEqual(new Point(315, 150), Placed(d).TopLeft);
    });

    [TestMethod]
    public void AWideTile_TakesARowOfItsOwn_BelowEverythingBefore() => UiThread.Run(() =>
    {
        var a = Tile(200);
        var b = Tile(50);
        var wide = Tile(40, wide: true);
        var after = Tile(10);
        var panel = Laid(620, a, b, wide, after);

        Assert.AreEqual(new Rect(0, 210, 620, 40), Placed(wide), "under the taller column, the full width");
        Assert.AreEqual(new Point(0, 260), Placed(after).TopLeft, "and what follows starts below it");
        Assert.AreEqual(270, panel.DesiredSize.Height);
    });

    [TestMethod]
    public void ACollapsedTile_TakesNoRoom() => UiThread.Run(() =>
    {
        var gone = Tile(500);
        gone.Visibility = Visibility.Collapsed;
        var panel = Laid(400, gone, Tile(20));
        Assert.AreEqual(20, panel.DesiredSize.Height);
    });
}
