using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    [CoversNode("tabular-toolbar-apply-template")]
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
    [CoversNode("tabular-template-panel-close")]
    public void CloseTemplatePanel_ClosesAnOpenPanel()
    {
        var vm = MakeEmpty();
        vm.OpenTemplatePanelCommand.Execute(null);
        Assert.IsTrue(vm.IsTemplatePanelOpen);

        vm.CloseTemplatePanelCommand.Execute(null);
        Assert.IsFalse(vm.IsTemplatePanelOpen);
    }

    [TestMethod]
    [CoversNode("tabular-toolbar-apply-template")]
    public void OpenTemplatePanel_LeavesChooseModeOff()
    {
        var vm = MakeEmpty();
        vm.OpenTemplatePanelCommand.Execute(null);
        Assert.IsFalse(vm.IsChooseMode, "manual open is not the ambiguous-choice flow");
    }

    [TestMethod]
    [CoversNode("tabular-template-cancel")]
    public void CancelTemplateThis_ClosesThePopup()
    {
        var vm = MakeEmpty();
        // The popup is bound TwoWay; drive it open directly then cancel via the command.
        vm.IsTemplatePopupOpen = true;
        vm.CancelTemplateThisCommand.Execute(null);
        Assert.IsFalse(vm.IsTemplatePopupOpen);
    }

    [TestMethod]
    [CoversNode("tabular-template-name")]
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
    [CoversNode("tabular-template-scope")]
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
    [CoversNode("tabular-show-only-compatible")]
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
    [CoversNode("tabular-column-select")]
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

    // ── Template This: open, save ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-toolbar-template-this")]
    public void OpenTemplateThis_SeedsThePopupFromTheOpenFile()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = Load(csv, new TabularTemplatesConfig());

            vm.OpenTemplateThisCommand.Execute(null);

            Assert.IsTrue(vm.IsTemplatePopupOpen);
            Assert.AreEqual("t", vm.TemplateName, "seeded with the file name, extension stripped");
            Assert.AreEqual(dir, vm.TemplateFolderPath);
            Assert.AreEqual("*.csv", vm.GlobPattern);
            Assert.AreEqual(TemplateScope.Folder, vm.SelectedScope, "folder scope is the default offer");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("tabular-template-save")]
    public void SaveTemplate_CapturesTheLayout_PersistsIt_AndClosesThePopup()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            var shell  = Substitute.For<IShellServices>();
            var vm     = Load(csv, config, shell);

            vm.OpenTemplateThisCommand.Execute(null);
            vm.TemplateName  = "My layout";
            vm.SelectedScope = TemplateScope.Manual;
            vm.SaveTemplateCommand.Execute(null);

            var saved = config.Templates.Single();
            Assert.AreEqual("My layout", saved.Name);
            Assert.AreEqual(TemplateScope.Manual, saved.Scope);
            Assert.AreEqual(3, saved.FieldCount, "the captured shape records the file it was built from");
            Assert.IsFalse(vm.IsTemplatePopupOpen, "saving dismisses the popup");
            shell.Received().SaveFeatureConfig(config);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Apply Template panel: apply, delete ───────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-template-apply")]
    public void ApplyTemplate_ReplacesTheLiveChain_AndClosesThePanel()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            config.Templates.Add(Renamer(dir, "Alpha"));
            var vm = Load(csv, config);

            vm.OpenTemplatePanelCommand.Execute(null);
            var item = vm.PanelTemplates.Single();
            Assert.IsTrue(item.IsCompatible, "precondition: the template matches this file's shape");

            vm.ApplyTemplateCommand.Execute(item);

            Assert.AreEqual("Alpha", vm.Columns[0].Header, "the template's rename is now live");
            Assert.IsFalse(vm.IsTemplatePanelOpen, "applying closes the panel");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("tabular-template-apply")]
    public void ApplyTemplate_Incompatible_IsConfirmedFirst_AndDeclineLeavesTheGridAlone()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            var mismatched = Renamer(dir, "Alpha");
            mismatched.FieldCount      = 5;                          // built from a differently-shaped file
            mismatched.OriginalHeaders = ["a", "b", "c", "d", "e"];
            config.Templates.Add(mismatched);

            var shell = Substitute.For<IShellServices>();
            shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(false));                   // the user declines

            var vm = Load(csv, config, shell);
            vm.ShowOnlyCompatible = false;                           // so the incompatible one is listed
            vm.OpenTemplatePanelCommand.Execute(null);
            var item = vm.PanelTemplates.Single();
            Assert.IsFalse(item.IsCompatible);

            vm.ApplyTemplateCommand.Execute(item);

            Assert.AreEqual("a", vm.Columns[0].Header, "a declined confirmation must not reshape the grid");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("tabular-template-delete")]
    public void DeleteTemplate_RemovesItFromTheSavedSet_AndFromTheList()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            config.Templates.Add(Renamer(dir, "Alpha"));
            var shell = Substitute.For<IShellServices>();
            var vm    = Load(csv, config, shell);

            vm.OpenTemplatePanelCommand.Execute(null);
            var item = vm.PanelTemplates.Single();

            vm.DeleteTemplateCommand.Execute(item);

            Assert.AreEqual(0, config.Templates.Count, "the template is gone from the saved set");
            Assert.AreEqual(0, vm.PanelTemplates.Count, "and the panel list rebuilt without it");
            shell.Received().SaveFeatureConfig(config);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TabularViewModel Load(string csv, TabularTemplatesConfig config, IShellServices? shell = null)
    {
        var vm = new TabularViewModel(csv,
            shell ?? Substitute.For<IShellServices>(), Substitute.For<IAIService>(), config);
        vm.Ready.GetAwaiter().GetResult();
        return vm;
    }

    /// <summary>A manual-scope template over the 3-column fixture that renames the first column.</summary>
    private static TabularTemplate Renamer(string dir, string newName) => new()
    {
        Id              = "t1",
        Name            = "Renamer",
        Scope           = TemplateScope.Manual,
        FolderPath      = dir,
        Separator       = ",",
        HasHeader       = true,
        FieldCount      = 3,
        OriginalHeaders = ["a", "b", "c"],
        TransformsJson  = $"[{{\"kind\":\"Rename\",\"index\":0,\"name\":\"{newName}\"}}]",
    };

    private static string WriteCsv(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexatab_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "t.csv");
        File.WriteAllText(csv, "a,b,c\n1,2,3\n4,5,6\n");
        return csv;
    }
}
