using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Tests.Visuals.Icons;

/// <summary>The picker's grid: a query and a set narrow it, it is cut into rows as wide as the picker, picking marks
/// the one picked, and every cell has an id a journey can name without typing an emoji.</summary>
[TestClass]
[CoversNode("icons-picker")]
public class IconPickerViewModelTests
{
    [TestMethod]
    public void Search_NarrowsTheGrid()
    {
        var vm = new IconPickerViewModel { SearchText = "home" };
        Assert.IsTrue(vm.Cells.Count < IconCatalog.All().Count);
        Assert.IsTrue(vm.Cells.All(c => c.Entry.Name.Contains("home") || c.Entry.Keywords.Contains("home")));
    }

    [TestMethod]
    public void SetFilter_KeepsOneSet()
    {
        var vm = new IconPickerViewModel();
        vm.SelectedFilter = vm.Filters.Single(f => f.Set == IconSet.FluentFilled);
        Assert.IsTrue(vm.Cells.All(c => c.Entry.Icon.Set == IconSet.FluentFilled));
    }

    [TestMethod]
    public void Rows_AreAsWideAsTheColumns()
    {
        var vm = new IconPickerViewModel { SearchText = "arrow", Columns = 7 };
        Assert.IsTrue(vm.Rows.Take(vm.Rows.Count - 1).All(r => r.Cells.Count == 7));
        Assert.AreEqual(vm.Cells.Count, vm.Rows.Sum(r => r.Cells.Count));
    }

    [TestMethod]
    public void Pick_SelectsAndRaises()
    {
        var vm = new IconPickerViewModel { SearchText = "home" };
        IconRef? raised = null;
        vm.Picked += icon => raised = icon;

        var cell = vm.Cells[0];
        vm.PickCommand.Execute(cell);

        Assert.AreEqual(cell.Entry.Icon, raised);
        Assert.AreEqual(cell.Entry.Icon, vm.Selected);
        Assert.AreEqual(1, vm.Cells.Count(c => c.IsSelected));
    }

    [TestMethod]
    public void Selected_SurvivesANewSearch()
    {
        var vm = new IconPickerViewModel { Selected = IconRef.Fluent("home") };
        vm.SearchText = "home";
        Assert.IsTrue(vm.Cells.Single(c => c.Entry.Icon == IconRef.Fluent("home")).IsSelected);
    }

    [TestMethod]
    public void CellIds_ArePrefixedAndTyped()
    {
        var vm = new IconPickerViewModel("RibbonEditor_Icons") { SearchText = "home" };
        Assert.IsTrue(vm.Cells.Any(c => c.AutomationId == "RibbonEditor_Icons_Icon_FluentRegular_home"));
        Assert.IsTrue(vm.Cells.Any(c => c.AutomationId == "RibbonEditor_Icons_Icon_Emoji_house"));
    }

    [TestMethod]
    public void Prefix_RenamesTheSetChipsAndKeepsTheSet()
    {
        var vm = new IconPickerViewModel();
        vm.SelectedFilter = vm.Filters.Single(f => f.Set == IconSet.Emoji);

        vm.AutomationPrefix = "RibbonEditor_Icons";

        Assert.IsTrue(vm.Filters.All(f => f.AutomationId.StartsWith("RibbonEditor_Icons_Set")));
        Assert.AreEqual(IconSet.Emoji, vm.SelectedFilter.Set);
        Assert.IsTrue(vm.Cells.All(c => c.Entry.Icon.Set == IconSet.Emoji));
    }
}
