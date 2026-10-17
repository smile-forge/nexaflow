using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>Finding what a right-click landed on: the nearest row or node above the element hit, never past the list.</summary>
[TestClass]
[CoversNode("vcommon-pointer-menus")]
public class PointerMenusTests
{
    [TestMethod]
    public void TheRowAboveWhatWasHit_IsFound() => UiThread.Run(() =>
    {
        var text = new TextBlock { Text = "a" };
        var row = new ListBoxItem { Content = text };
        var list = new ListBox();
        list.Items.Add(row);

        Assert.AreSame(row, PointerMenus.ItemUnder<ListBoxItem>(text, list));
        Assert.AreSame(row, PointerMenus.ItemUnder<ListBoxItem>(row, list));
    });

    [TestMethod]
    public void NothingPastTheList_IsFound() => UiThread.Run(() =>
    {
        var list = new ListBox();
        var outer = new ListBoxItem { Content = list };
        Assert.IsNull(PointerMenus.ItemUnder<ListBoxItem>(list, list), "the search stops at the list, not the row it sits in");
    });
}
