using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.ViewModels;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
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
                Substitute.For<IShellServices>(), Substitute.For<IAIService>());
            await vm.Ready;

            Assert.IsTrue(vm.Columns.Count > 0, "columns should load");
            Assert.IsFalse(string.IsNullOrEmpty(vm.GetContext()));
            Assert.IsTrue(vm.GetClientTools().Count > 0, "tools should be exposed");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
