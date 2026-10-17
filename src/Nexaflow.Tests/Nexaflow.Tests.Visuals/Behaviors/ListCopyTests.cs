using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Tests.Visuals.Behaviors;

/// <summary>
/// What a copy out of a list says. A reader picks rows in whatever order they click them; the clipboard gets them as
/// the list shows them, each as the line its row chooses.
/// </summary>
[TestClass]
[CoversNode("vcommon-list-copy")]
public class ListCopyTests
{
    sealed record Row(string Name, string Size) : IClipboardText
    {
        public string ClipboardText => $"{Name}\t{Size}";
    }

    [TestMethod]
    public void SelectedRowsCopyInTheOrderTheListShowsThem() => UiThread.Run(() =>
    {
        var list = new ListBox { SelectionMode = SelectionMode.Extended };
        foreach (var name in new[] { "a", "b", "c", "d" }) list.Items.Add(new Row(name, "1 KB"));
        list.SelectedItems.Add(list.Items[2]);
        list.SelectedItems.Add(list.Items[0]);

        Assert.AreEqual($"a\t1 KB{Environment.NewLine}c\t1 KB", ListCopy.SelectionText(list));
    });

    [TestMethod]
    public void ARowWithoutClipboardTextCopiesAsItsString() => UiThread.Run(() =>
    {
        var list = new ListBox();
        list.Items.Add(42);
        list.SelectedIndex = 0;

        Assert.AreEqual("42", ListCopy.SelectionText(list));
    });

    [TestMethod]
    public void NothingSelectedCopiesNothing() => UiThread.Run(() =>
    {
        var list = new ListView();
        list.Items.Add(new Row("a", "1 KB"));

        Assert.IsNull(ListCopy.SelectionText(list));
    });

    [TestMethod]
    public void ATreeCopiesItsSelectedNode() => UiThread.Run(() =>
    {
        var tree = new TreeView();
        var item = new TreeViewItem { Header = "hub" };
        tree.Items.Add(item);
        item.IsSelected = true;

        StringAssert.Contains(ListCopy.SelectionText(tree), "hub");
    });

    [TestMethod]
    public void EnablingItGivesTheListACopyMenu() => UiThread.Run(() =>
    {
        var list = new ListBox();
        ListCopy.SetEnabled(list, true);

        Assert.IsNotNull(list.ContextMenu);
        Assert.IsTrue(list.CommandBindings.Cast<System.Windows.Input.CommandBinding>()
            .Any(b => b.Command == System.Windows.Input.ApplicationCommands.Copy));
    });
}
