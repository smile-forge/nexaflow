using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// A file-system tab's model as a test opens it, with no view-model over it: on a shell that discovers no actions, a scratch
/// folder of its own, and the runtime's services one each.
/// </summary>
internal static class ModelHarness
{
    /// <summary>A shell that discovers nothing, so the model's actions are only those the feature builds in.</summary>
    public static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return shell;
    }

    /// <summary>A model opened on <paramref name="folder"/>, once it has arrived there and read it.</summary>
    public static async Task<FileSystemModel> AtAsync(string folder, IShellServices? shell = null)
    {
        var model = Runtime(shell).OpenModel();
        model.Navigation.StartAt(folder);
        await model.Navigation.Browsing;
        await model.Listing.EntriesLoaded;
        return model;
    }

    /// <summary>A model opened on This PC.</summary>
    public static FileSystemModel ThisPc(IShellServices? shell = null)
    {
        var model = Runtime(shell).OpenModel();
        model.Navigation.StartAtThisPc();
        return model;
    }

    /// <summary>A fresh empty folder under the test temp root.</summary>
    public static string Scratch()
    {
        var dir = Path.Combine(TestTemp.Root, "nexafs_model_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FileBrowserRuntime Runtime(IShellServices? shell)
        => new(shell ?? Shell(), Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), VirtualFileSystem.Instance);
}
