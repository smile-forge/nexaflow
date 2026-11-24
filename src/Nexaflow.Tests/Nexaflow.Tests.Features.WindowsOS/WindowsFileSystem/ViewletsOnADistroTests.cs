using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The folder viewlets over a real distro: a repository and a project there are found through the Windows folder showing
/// the distro, and a viewlet whose tools run on the distro's own host is offered whether the folder is named by the
/// location's path or by its <c>\\wsl.localhost</c> folder — both handing its programs the same distro path — while one
/// that changes files with Windows' own tools is offered by neither. The files are the test's own, under a folder it makes
/// in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("winfs-viewlet-host")]
public sealed class ViewletsOnADistroTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    /// <summary>Matches a repository and runs its tools on the host — stands in for Git.</summary>
    private sealed class RepositoryViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Repository";
        public bool   AppliesToDrives => false;
        public bool   RunsOnLocationHost => true;
        public string[]? ContainsFolderGlobs => [".git"];
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    /// <summary>Matches a project and runs its tools on the host — stands in for .NET.</summary>
    private sealed class ProjectViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Project";
        public bool   AppliesToDrives => false;
        public bool   RunsOnLocationHost => true;
        public string[]? ContainsFileGlobs => ["*.csproj"];
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    /// <summary>Changes the folder with Windows' own tools — stands in for the AI summary.</summary>
    private sealed class WindowsToolViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Windows tool";
        public bool   AppliesToDrives => false;
        public bool   RequiresLocalFolder => true;
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AViewletWhoseToolsRunOnTheDistro_IsOfferedByEitherName_AndRunsOnTheSameFolder()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var name   = $"nexaflow-viewlets-{Guid.NewGuid():N}";
        var folder = $@"{set.Mounts[0].Root}\tmp\{name}";
        await files.CreateDirectoryAsync($@"{folder}\.git");
        await files.WriteAllTextAsync($@"{folder}\App.csproj", "<Project/>");
        try
        {
            var shell = Substitute.For<IShellServices>();
            shell.DiscoverImplementations<IFolderViewlet>().Returns([typeof(RepositoryViewlet), typeof(ProjectViewlet), typeof(WindowsToolViewlet)]);
            var viewlets = FileSystemFeatureRegistry.For(shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), files);
            var native   = files.TryResolveReal(folder) ?? throw new AssertFailedException("a distro's folder is shown by Windows");

            string[] Offered(string path) => FolderViewletRegistry.GetMatchingViewlets(path, viewlets).Select(v => v.DisplayName).ToArray();

            StringAssert.StartsWith(native, @"\\wsl", StringComparison.OrdinalIgnoreCase);
            CollectionAssert.AreEquivalent(new[] { "Repository", "Project" }, Offered(folder));
            CollectionAssert.AreEquivalent(new[] { "Repository", "Project" }, Offered(native), "the same folder, named as Windows shows it");
            Assert.AreEqual($"/tmp/{name}", files.GetProgramHost(folder)?.HostPathOf(folder));
            Assert.AreEqual($"/tmp/{name}", files.GetProgramHost(native)?.HostPathOf(native), "a program run there runs in the distro");
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
