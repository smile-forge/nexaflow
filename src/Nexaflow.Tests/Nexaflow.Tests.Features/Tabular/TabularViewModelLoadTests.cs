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

[TestClass]
[CoversNode("tabular-template-autoapply")]
public class TabularViewModelLoadTests
{
    private static string WriteCsv(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexatab_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "t.csv");
        File.WriteAllText(csv, "a,b,c\n1,2,3\n4,5,6\n");
        return csv;
    }

    [TestMethod]
    public async Task Construct_OverCsv_LoadsAndExposesToolsWithoutThrowing()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var vm = new TabularViewModel(csv,
                Substitute.For<IShellServices>(), Substitute.For<IAIService>(),
                new TabularTemplatesConfig());
            await vm.Ready;

            Assert.IsTrue(vm.Columns.Count > 0, "columns should load");
            Assert.IsFalse(string.IsNullOrEmpty(vm.GetContext()));
            Assert.IsTrue(vm.GetClientTools().Count > 0, "tools should be exposed");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task Construct_WithCompatibleFolderTemplate_AutoAppliesTransforms()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            config.Templates.Add(new TabularTemplate
            {
                Id              = "t1",
                Name            = "Renamer",
                Scope           = TemplateScope.Folder,
                FolderPath      = dir,
                Separator       = ",",
                HasHeader       = true,
                FieldCount      = 3,
                OriginalHeaders = ["a", "b", "c"],
                TransformsJson  = "[{\"kind\":\"Rename\",\"index\":0,\"name\":\"Alpha\"}]",
            });

            var vm = new TabularViewModel(csv,
                Substitute.For<IShellServices>(), Substitute.For<IAIService>(), config);
            await vm.Ready;

            Assert.AreEqual(3, vm.Columns.Count);
            Assert.AreEqual("Alpha", vm.Columns[0].Header, "auto-applied rename should be reflected");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task Construct_WithIncompatibleTemplate_DoesNotAutoApply()
    {
        var csv = WriteCsv(out var dir);
        try
        {
            var config = new TabularTemplatesConfig();
            config.Templates.Add(new TabularTemplate
            {
                Id              = "t1",
                Name            = "Mismatch",
                Scope           = TemplateScope.Folder,
                FolderPath      = dir,
                Separator       = ",",
                HasHeader       = true,
                FieldCount      = 5,                       // different column count → incompatible
                OriginalHeaders = ["a", "b", "c", "d", "e"],
                TransformsJson  = "[{\"kind\":\"Rename\",\"index\":0,\"name\":\"Alpha\"}]",
            });

            var vm = new TabularViewModel(csv,
                Substitute.For<IShellServices>(), Substitute.For<IAIService>(), config);
            await vm.Ready;

            Assert.AreEqual("a", vm.Columns[0].Header, "incompatible template must not auto-apply");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
