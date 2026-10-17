using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// What counts as a list's blank space, where a click clears the selection: the list itself, not a row or anything in
/// one.
/// </summary>
[TestClass]
[CoversNode("vcommon-list-copy")]
public class ListSelectionTests
{
    static ListBox Laid(params string[] items)
    {
        var list = new ListBox { Width = 200, Height = 300 };
        foreach (var item in items) list.Items.Add(item);
        list.Measure(new Size(200, 300));
        list.Arrange(new Rect(0, 0, 200, 300));
        list.UpdateLayout();
        return list;
    }

    [TestMethod]
    public void TheListItself_IsBlank() => UiThread.Run(() =>
    {
        var list = Laid("a", "b");
        Assert.IsTrue(ListSelection.IsBlank(list, list));
    });

    [TestMethod]
    public void ARow_AndWhatIsInIt_AreNot() => UiThread.Run(() =>
    {
        var inside = new TextBlock { Text = "a" };
        var row = new ListBoxItem { Content = inside };
        var list = Laid();
        list.Items.Add(row);
        Assert.IsFalse(ListSelection.IsBlank(list, row));
        Assert.IsFalse(ListSelection.IsBlank(list, inside));
    });

    [TestMethod]
    public void TurningItOn_IsOnlyForLists() => UiThread.Run(() =>
    {
        var list = new ListBox();
        ListSelection.SetClearOnBlankClick(list, true);
        Assert.IsTrue(ListSelection.GetClearOnBlankClick(list));
        ListSelection.SetClearOnBlankClick(new Border(), true);   // anything else is left alone, not thrown at
    });
}
