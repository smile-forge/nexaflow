using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Templates;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// Headless command/toggle-state logic behind the Tabular toolbar &amp; side panels — the pure
/// view-state machinery that doesn't need a live virtualized grid. Loading, shape detection and
/// transform application are covered by <see cref="TabularViewModelLoadTests"/>; the parsing /
/// tokenizer / shape stack has its own dedicated suites. Everything here runs without a window.
/// </summary>
[TestClass]
public class TabularViewModelTests
{
    /// <summary>A VM over an empty path — constructs without kicking off a file load,
    /// so the pure popup/panel toggle state can be exercised in isolation.</summary>
    private static TabularViewModel MakeEmpty(TabularTemplatesConfig? templates = null)
        => new(string.Empty,
               Substitute.For<IShellServices>(), Substitute.For<IAIService>(),
               templates ?? new TabularTemplatesConfig());

    [TestMethod]
    [CoversNode("tabular-apply-template")]
    public void TemplatePanel_TogglesOpenAndClosed()
    {
        var vm = MakeEmpty();
        Assert.IsFalse(vm.IsTemplatePanelOpen);

        vm.OpenTemplatePanelCommand.Execute(null);   // toggle open
        Assert.IsTrue(vm.IsTemplatePanelOpen);

        vm.OpenTemplatePanelCommand.Execute(null);   // same button toggles closed
        Assert.IsFalse(vm.IsTemplatePanelOpen);
    }

    [TestMethod]
    [CoversNode("tabular-apply-template")]
    public void CloseTemplatePanel_ClosesAnOpenPanel()
    {
        var vm = MakeEmpty();
        vm.OpenTemplatePanelCommand.Execute(null);
        Assert.IsTrue(vm.IsTemplatePanelOpen);

        vm.CloseTemplatePanelCommand.Execute(null);
        Assert.IsFalse(vm.IsTemplatePanelOpen);
    }

    [TestMethod]
    [CoversNode("tabular-apply-template")]
    public void OpenTemplatePanel_LeavesChooseModeOff()
    {
        var vm = MakeEmpty();
        vm.OpenTemplatePanelCommand.Execute(null);
        Assert.IsFalse(vm.IsChooseMode, "manual open is not the ambiguous-choice flow");
    }

    [TestMethod]
    [CoversNode("tabular-template-this")]
    public void CancelTemplateThis_ClosesThePopup()
    {
        var vm = MakeEmpty();
        // The popup is bound TwoWay; drive it open directly then cancel via the command.
        vm.IsTemplatePopupOpen = true;
        vm.CancelTemplateThisCommand.Execute(null);
        Assert.IsFalse(vm.IsTemplatePopupOpen);
    }

    [TestMethod]
    [CoversNode("tabular-template-this")]
    public void SaveTemplate_CanExecute_RequiresName()
    {
        var vm = MakeEmpty();
        vm.SelectedScope = TemplateScope.Folder;

        vm.TemplateName = string.Empty;
        Assert.IsFalse(vm.SaveTemplateCommand.CanExecute(null), "blank name blocks save");

        vm.TemplateName = "My layout";
        Assert.IsTrue(vm.SaveTemplateCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("tabular-template-this")]
    public void SaveTemplate_CanExecute_GlobScopeRequiresPattern()
    {
        var vm = MakeEmpty();
        vm.TemplateName  = "Named";
        vm.SelectedScope = TemplateScope.Glob;

        vm.GlobPattern = string.Empty;
        Assert.IsFalse(vm.SaveTemplateCommand.CanExecute(null), "glob scope needs a pattern");

        vm.GlobPattern = "*.csv";
        Assert.IsTrue(vm.SaveTemplateCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("tabular-apply-template")]
    public void ShowOnlyCompatible_TogglesAndRebuildsPanelWithoutThrowing()
    {
        var vm = MakeEmpty();
        Assert.IsTrue(vm.ShowOnlyCompatible, "default is compatible-only");

        // Toggling drives OnShowOnlyCompatibleChanged → RebuildPanelTemplates; must be inert
        // with no data loaded and an empty template set.
        vm.ShowOnlyCompatible = false;
        Assert.IsFalse(vm.ShowOnlyCompatible);
        Assert.AreEqual(0, vm.PanelTemplates.Count);

        vm.ShowOnlyCompatible = true;
        Assert.IsTrue(vm.ShowOnlyCompatible);
    }

    [TestMethod]
    [CoversNode("tabular-filter")]
    public void FilterPanelOpen_TracksColumnSelection()
    {
        // IsFilterPanelOpen is derived from any column being selected — needs a loaded grid.
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = new TabularViewModel(csv,
                Substitute.For<IShellServices>(), Substitute.For<IAIService>(),
                new TabularTemplatesConfig());
            vm.Ready.GetAwaiter().GetResult();

            Assert.IsTrue(vm.Columns.Count > 0, "columns should load");
            Assert.IsFalse(vm.IsFilterPanelOpen, "no column selected → panel closed");

            vm.Columns[0].IsSelected = true;
            Assert.IsTrue(vm.IsFilterPanelOpen, "selecting a column opens the filter panel");

            vm.Columns[0].IsSelected = false;
            Assert.IsFalse(vm.IsFilterPanelOpen, "deselecting closes it again");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string WriteCsv(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexatab_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "t.csv");
        File.WriteAllText(csv, "a,b,c\n1,2,3\n4,5,6\n");
        return csv;
    }
}
