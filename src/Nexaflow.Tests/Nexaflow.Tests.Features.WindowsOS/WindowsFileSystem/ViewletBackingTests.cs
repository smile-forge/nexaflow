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

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Where a folder viewlet may appear. A viewlet runs real tooling against the folder — git, dotnet, a
/// process with a working directory — so inside an archive there is simply nothing for it to work in:
/// the entries exist only as bytes in a container, and a single materialised file is not a folder.
/// The same reasoning as <c>IFileAction.RequiresFullyBackedPath</c>, applied one level up.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-viewlets")]
public class ViewletBackingTests
{
    private string _dir = string.Empty;
    private string _zip = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _dir = Path.Combine(Path.GetTempPath(), "nexa-viewlet-" + Guid.NewGuid().ToString("N"));
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
        public FrameworkElement CreateView(string folderPath, IViewletController controller) => new FrameworkElement();
    }

    /// <summary>Matches on nothing at all — the contract permits it (both glob sets default to null), so
    /// ContentsMatch short-circuits to true and no directory is ever probed.</summary>
    private sealed class UnconditionalViewlet : IFolderViewlet, ICacheable
    {
        public string DisplayName => "Always";
        public bool   AppliesToDrives => false;
        public FrameworkElement CreateView(string folderPath, IViewletController controller) => new FrameworkElement();
    }

    private static FileSystemFeatureRegistry Registry()
    {
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFolderViewlet>().Returns([typeof(ToolingViewlet), typeof(UnconditionalViewlet)]);
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<Nexaflow.Features.Common.ThisPc.IThisPcItemProvider>().Returns(Array.Empty<Type>());
        return FileSystemFeatureRegistry.For(shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());
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
}
