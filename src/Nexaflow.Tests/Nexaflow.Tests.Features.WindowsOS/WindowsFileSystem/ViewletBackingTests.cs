using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Where a folder viewlet may appear. A viewlet runs real tooling against the folder — git, dotnet, a
/// process with a working directory — so inside an archive there is simply nothing for it to work in:
/// the entries exist only as bytes in a container, and a single materialised file is not a folder.
/// The same reasoning as <c>IFileAction.RequiresFullyBackedPath</c>, applied one level up.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-viewlet-host")]
public class ViewletBackingTests
{
    private string _dir = string.Empty;
    private string _zip = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _dir = Path.Combine(TestTemp.Root, "nexa-viewlet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "repo", ".git"));
        File.WriteAllText(Path.Combine(_dir, "repo", "App.csproj"), "<Project/>");

        // A zip whose contents would satisfy both viewlets if anything looked inside it.
        _zip = Path.Combine(_dir, "bundle.zip");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("repo/App.csproj").Open();
            var bytes = new UTF8Encoding(false).GetBytes("<Project/>");
            s.Write(bytes, 0, bytes.Length);
        }
        File.WriteAllBytes(_zip, ms.ToArray());
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    /// <summary>Structural criteria a real repo folder satisfies — stands in for Git / .NET.</summary>
    private sealed class ToolingViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Tooling";
        public bool   AppliesToDrives => false;
        public string[]? ContainsFileGlobs => ["*.csproj"];
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    /// <summary>Matches on nothing at all — the contract permits it (both glob sets default to null), so
    /// ContentsMatch short-circuits to true and no directory is ever probed.</summary>
    private sealed class UnconditionalViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Always";
        public bool   AppliesToDrives => false;
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    private static FileSystemFeatureRegistry Registry()
    {
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFolderViewlet>().Returns([typeof(ToolingViewlet), typeof(UnconditionalViewlet)]);
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<Nexaflow.Features.Common.ThisPc.IThisPcItemProvider>().Returns(Array.Empty<Type>());
        return FileSystemFeatureRegistry.For(shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), VirtualFileSystem.Instance);
    }

    private static IReadOnlyList<string> NamesFor(string folderPath)
        => [.. FolderViewletRegistry.GetMatchingViewlets(folderPath, Registry()).Select(v => v.DisplayName)];

    [TestMethod]
    public void OnARealRepoFolderTheToolingViewletAppears()
    {
        CollectionAssert.Contains(NamesFor(Path.Combine(_dir, "repo")).ToArray(), "Tooling");
    }

    [TestMethod]
    public void InsideAnArchiveNoViewletAppearsAtAll()
    {
        // Including the one that matches unconditionally: there is no folder here for a tool to run in,
        // whether or not the viewlet bothered to state structural criteria.
        var names = NamesFor(Path.Combine(_zip, "repo"));

        CollectionAssert.DoesNotContain(names.ToArray(), "Tooling");
        CollectionAssert.DoesNotContain(names.ToArray(), "Always");
    }

    [TestMethod]
    public void OnTheArchiveFileItselfNoViewletAppearsEither()
    {
        var names = NamesFor(_zip);

        CollectionAssert.DoesNotContain(names.ToArray(), "Tooling");
        CollectionAssert.DoesNotContain(names.ToArray(), "Always");
    }

    [TestMethod]
    public void UnderAMountViewletsAppearBecauseTheFolderGenuinelyExists()
    {
        const string id = "viewletmount";
        VirtualFileSystem.Instance.RegisterMount(new VirtualMount(id, "Viewlet Mount", _dir));
        try
        {
            var names = NamesFor($@"{VirtualMount.RootFor(id)}\repo");

            CollectionAssert.Contains(names.ToArray(), "Tooling",
                "a mount has the whole tree on disk, so git and dotnet can run in it");
            CollectionAssert.Contains(names.ToArray(), "Always");
        }
        finally { VirtualFileSystem.Instance.UnregisterMount(id); }
    }

    /// <summary>Changes the folder with Windows' own tools and no other way — stands in for the AI summary.</summary>
    private sealed class ChangingViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Changing";
        public bool   AppliesToDrives => false;
        public bool   RequiresLocalFolder => true;
        public string[]? ContainsFileGlobs => ["*.csproj"];
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    /// <summary>Changes the folder with tools that run on a location's own host there — stands in for Git and .NET.</summary>
    private sealed class HostViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Host";
        public bool   AppliesToDrives => false;
        public bool   RunsOnLocationHost => true;
        public string[]? ContainsFolderGlobs => [".git"];
        public object CreateViewModel(string folderPath, IViewletController controller) => new object();
    }

    private sealed class SameNames : Nexaflow.IO.Common.Locations.ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(Nexaflow.IO.Common.Locations.ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    /// <summary>The viewlets offered on a repository in a location Windows also shows as a folder, as a WSL distro is, named
    /// by the location's path, by that Windows folder, and — for comparison — on this PC.</summary>
    private (string[] OnLocation, string[] ByNative, string[] OnThisPc) OfferedOnADistro(
        Nexaflow.IO.Common.Locations.LocationCapabilities capabilities)
    {
        var distro = Path.Combine(_dir, "distro");
        Directory.CreateDirectory(Path.Combine(distro, "repo", ".git"));
        File.WriteAllText(Path.Combine(distro, "repo", "App.csproj"), "<Project/>");

        var files = new Nexaflow.IO.Common.Locations.LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(new Nexaflow.IO.Common.Locations.LocationMount(
            new Nexaflow.IO.Common.Locations.ConnectionProfile("viewlet-probe", "wsl", "Probe", "Probe"), capabilities, null!)
        { NativeRoots = [distro], NativePaths = new SameNames() });
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFolderViewlet>().Returns([typeof(ToolingViewlet), typeof(ChangingViewlet), typeof(HostViewlet)]);
        var registry = FileSystemFeatureRegistry.For(shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), files);

        string[] Offered(string folder) => FolderViewletRegistry.GetMatchingViewlets(folder, registry).Select(v => v.DisplayName).ToArray();
        return (Offered(@"::viewlet-probe\repo"), Offered(Path.Combine(distro, "repo")), Offered(Path.Combine(_dir, "repo")));
    }

    [TestMethod]
    public void OnALocation_AViewletThatChangesFilesWithWindowsToolsIsNotOffered_OneThatReadsIs()
    {
        var (onLocation, byNative, onThisPc) = OfferedOnADistro(Nexaflow.IO.Common.Locations.LocationCapabilities.SameMachine);

        CollectionAssert.AreEquivalent(new[] { "Tooling" }, onLocation,
            "one that reads is offered there; one that changes files with Windows' tools is not, nor one whose host runs nothing");
        CollectionAssert.AreEquivalent(new[] { "Tooling" }, byNative, "the distro's folder named as Windows shows it is the same folder");
        CollectionAssert.AreEquivalent(new[] { "Tooling", "Changing", "Host" }, onThisPc);
    }

    [TestMethod]
    public void OnALocationWhoseHostRunsPrograms_AViewletThatRunsItsToolsThereIsOffered_ByEitherName()
    {
        var (onLocation, byNative, onThisPc) = OfferedOnADistro(
            Nexaflow.IO.Common.Locations.LocationCapabilities.SameMachine | Nexaflow.IO.Common.Locations.LocationCapabilities.Shell);

        CollectionAssert.AreEquivalent(new[] { "Tooling", "Host" }, onLocation, "git and dotnet run on the host; Windows' tools still do not");
        CollectionAssert.AreEquivalent(new[] { "Tooling", "Host" }, byNative, "named by its Windows folder, it is still the location's");
        CollectionAssert.AreEquivalent(new[] { "Tooling", "Changing", "Host" }, onThisPc);
    }

    /// <summary>A folder action that changes the folder with Windows' own calls.</summary>
    public sealed class LocalOnlyFolderAction : IFolderAction, ICacheable
    {
        public bool    IsDestructive         => false;
        public bool    SupportsMultipleFiles => true;
        public Nexaflow.Icons.IconRef Icon   => Nexaflow.Icons.IconRef.Emoji("🛠");
        public string  DisplayName           => "Local only";
        public bool    RequiresRefresh       => false;
        public bool    CanPerformAction      => true;
        public bool    AppliesToRoot         => true;
        public bool    AppliesToDrives       => false;
        public bool    RequiresLocalFolder   => true;
        public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>A folder action that changes the folder through the virtual file system.</summary>
    public sealed class ThroughTheFilesFolderAction : IFolderAction, ICacheable
    {
        public bool    IsDestructive         => false;
        public bool    SupportsMultipleFiles => true;
        public Nexaflow.Icons.IconRef Icon   => Nexaflow.Icons.IconRef.Emoji("📁");
        public string  DisplayName           => "Anywhere";
        public bool    RequiresRefresh       => false;
        public bool    CanPerformAction      => true;
        public bool    AppliesToRoot         => true;
        public bool    AppliesToDrives       => false;
        public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default) => Task.FromResult(true);
    }

    [TestMethod]
    public void OnALocation_AFolderActionThatChangesFilesWithWindowsCallsIsNotOffered_SelectedOrOpen()
    {
        var distro = Path.Combine(_dir, "distro");
        Directory.CreateDirectory(Path.Combine(distro, "repo"));
        var files = new Nexaflow.IO.Common.Locations.LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(new Nexaflow.IO.Common.Locations.LocationMount(
            new Nexaflow.IO.Common.Locations.ConnectionProfile("action-probe", "wsl", "Probe", "Probe"),
            Nexaflow.IO.Common.Locations.LocationCapabilities.SameMachine, null!)
        { NativeRoots = [distro], NativePaths = new SameNames() });
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns([typeof(LocalOnlyFolderAction), typeof(ThroughTheFilesFolderAction)]);
        var actions = new FileActionManager(FileSystemFeatureRegistry.For(shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), files));
        var canPerform = actions.SnapshotCanPerform().Folder;

        string[] Selected(string path) => actions.FilterFolderActions(
            [new FileSystemEntry { Name = "repo", FullPath = path, IsDirectory = true }], canPerform).Select(a => a.DisplayName).ToArray();
        string[] Open(string path) => actions.FilterFolderActions([], canPerform, path).Select(a => a.DisplayName).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Anywhere" }, Selected(@"::action-probe\repo"));
        CollectionAssert.AreEquivalent(new[] { "Anywhere" }, Selected(Path.Combine(distro, "repo")), "named as Windows shows it, the same folder");
        CollectionAssert.AreEquivalent(new[] { "Anywhere" }, Open(@"::action-probe\repo"));
        CollectionAssert.AreEquivalent(new[] { "Local only", "Anywhere" }, Selected(Path.Combine(_dir, "repo")));
        CollectionAssert.AreEquivalent(new[] { "Local only", "Anywhere" }, Open(Path.Combine(_dir, "repo")));
    }
}
