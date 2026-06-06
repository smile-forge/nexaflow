using System.Linq;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
public class EnvironmentVariablesViewModelTests
{
    private static EnvironmentVariablesViewModel Build() =>
        new(Substitute.For<IShellServices>());

    [TestMethod]
    public void SettingEditValue_RebuildsEntries()
    {
        var vm = Build();
        vm.EditValue = @"C:\a;C:\b";

        CollectionAssert.AreEqual(new[] { @"C:\a", @"C:\b" }, vm.EditEntries.ToArray());
        Assert.IsTrue(vm.IsEditingList);
    }

    [TestMethod]
    public void AddEntry_AppendsAndRecomposesValue()
    {
        var vm = Build();
        vm.EditValue = @"C:\a;C:\b";
        vm.NewEntry  = @"C:\c";

        vm.AddEntryCommand.Execute(null);

        Assert.AreEqual(@"C:\a;C:\b;C:\c", vm.EditValue);
        Assert.AreEqual("", vm.NewEntry);
    }

    [TestMethod]
    public void RemoveEntry_RemovesAndRecomposesValue()
    {
        var vm = Build();
        vm.EditValue = @"C:\a;C:\b;C:\c";

        vm.RemoveEntryCommand.Execute(@"C:\b");

        Assert.AreEqual(@"C:\a;C:\c", vm.EditValue);
    }

    [TestMethod]
    public void MoveEntryUp_ReordersAndRecomposesValue()
    {
        var vm = Build();
        vm.EditValue = @"C:\a;C:\b;C:\c";

        vm.MoveEntryUpCommand.Execute(@"C:\b");

        Assert.AreEqual(@"C:\b;C:\a;C:\c", vm.EditValue);
    }
}
