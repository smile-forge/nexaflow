using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Hdf5.ViewModels;
using Nexaflow.IO.Hdf5;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Hdf5;

/// <summary>Opens the viewer's view-model on a sample and waits for what it shows, so tests read settled state.</summary>
internal static class Hdf5Harness
{
    public static string Sample(string name = "experiment.h5") => TestSampleData.Path("hdf5", name);

    /// <summary>A shell whose UI hop simply runs the work, as it would on the UI thread.</summary>
    public static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Func<Task<ToolResult>>>()).Returns(ci => ci.Arg<Func<Task<ToolResult>>>()());
        return shell;
    }

    public static async Task<Hdf5ViewModel> OpenAsync(string? node = null, string? path = null, IShellServices? shell = null)
    {
        var vm = new Hdf5ViewModel(path ?? Sample(), node, shell ?? Shell(), new Hdf5SourceCache(TimeSpan.Zero));
        await vm.Loaded;
        await Settled(vm);
        return vm;
    }

    public static async Task SelectAsync(Hdf5ViewModel vm, string path)
    {
        await vm.NavigateToAsync(path);
        await Settled(vm);
    }

    public static async Task Settled(Hdf5ViewModel vm)
    {
        if (vm.ShowTask is { } show) await show;
    }

    /// <summary>Expands a tree row and waits for its first page of members.</summary>
    public static async Task<Hdf5NodeViewModel> ExpandAsync(Hdf5NodeViewModel node)
    {
        node.IsExpanded = true;
        for (int i = 0; i < 500 && !node.ChildrenLoaded; i++) await Task.Delay(10);
        Assert.IsTrue(node.ChildrenLoaded, $"{node.Display} did not list its members");
        return node;
    }

    public static Hdf5NodeViewModel Child(Hdf5NodeViewModel node, string name) =>
        node.ObjectChildren.Single(c => c.Display == name);

    /// <summary>A file of one group with more members than a page of the tree holds.</summary>
    public static string WriteLargeGroup(int members)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexa-hdf5-large-{Guid.NewGuid():N}.h5");
        var group = new PureHDF.H5Group();
        for (int i = 0; i < members; i++) group[$"d{i:D4}"] = new PureHDF.H5Dataset(new[] { i });
        var file = new PureHDF.H5File { ["many"] = group };
        file.Write(path);
        return path;
    }
}
