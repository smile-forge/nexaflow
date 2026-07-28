using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("winfs-path-query")]
public class FileSystemQueryHandlerTests
{
    private static (IShellServices Shell, IAIService Ai, IReadOnlyDictionary<Type, IFeatureConfig> Configs) Deps()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return (shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    private static FileSystemViewModel AtPath(string path)
    {
        var (shell, ai, configs) = Deps();
        return new FileSystemViewModel(path, shell, ai, configs);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexafsq_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── CanProcess ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void CanProcess_RelativePathWithSpaces_ThatExists_IsAccepted()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "My Stuff"));
            var vm = AtPath(dir);

            var score = new FileSystemQueryHandler().CanProcess("My Stuff", false, vm);

            Assert.IsTrue(score > 0f, "an existing relative folder with a space should be handled");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_RelativePathNoSpaces_ThatExists_IsAccepted()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            var vm = AtPath(dir);

            Assert.IsTrue(new FileSystemQueryHandler().CanProcess("src", false, vm) > 0f);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_RelativePath_ThatDoesNotExist_IsRejected()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);

            // Real folder name shape, but nothing on disk — must not be claimed.
            Assert.AreEqual(0f, new FileSystemQueryHandler().CanProcess("no such folder", false, vm));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_Wildcard_IsRejected()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "logs"));
            var vm = AtPath(dir);

            Assert.AreEqual(0f, new FileSystemQueryHandler().CanProcess("logs\\*.txt", false, vm));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_ThisPcMode_RelativePath_IsRejected()
    {
        var (shell, ai, configs) = Deps();
        var vm = FileSystemViewModel.CreateThisPc(shell, ai, configs);

        // No folder root to resolve against.
        Assert.AreEqual(0f, new FileSystemQueryHandler().CanProcess("anything", false, vm));
    }

    // ── CanProcess: the ">" prefix forces navigation ───────────────────────────

    [TestMethod]
    public void CanProcess_Prefixed_NonExistentRelativePath_IsClaimed()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);

            // Un-prefixed this scores 0 (see above); typed as ">no such folder" the user forced navigation,
            // so the handler must take it and let ProcessAsync report the bad path.
            Assert.IsTrue(new FileSystemQueryHandler().CanProcess("no such folder", true, vm) > 0f);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_Prefixed_Wildcard_IsClaimed()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "logs"));
            var vm = AtPath(dir);

            // The wildcard rule yields to the search handlers on bare input only — ">" overrides it.
            Assert.IsTrue(new FileSystemQueryHandler().CanProcess("logs\\*.txt", true, vm) > 0f);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_Prefixed_EmptyInput_IsRejected()
    {
        var dir = TempDir();
        try
        {
            Assert.AreEqual(0f, new FileSystemQueryHandler().CanProcess("   ", true, AtPath(dir)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public void CanProcess_Prefixed_WrongPage_IsStillRejected()
        => Assert.AreEqual(0f, new FileSystemQueryHandler().CanProcess("C:\\", true, null));

    // ── ProcessAsync ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessAsync_RelativePathWithSpaces_NavigatesIntoIt()
    {
        var dir = TempDir();
        try
        {
            var target = Path.Combine(dir, "My Stuff");
            Directory.CreateDirectory(target);
            var vm = AtPath(dir);

            var error = await new FileSystemQueryHandler().ProcessAsync("My Stuff", false, vm);

            Assert.IsNull(error, "navigation should succeed without an error message");
            Assert.AreEqual(Path.GetFullPath(target), Path.GetFullPath(vm.CurrentPath));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task ProcessAsync_RelativePath_ThatDoesNotExist_ReturnsNotFound()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);

            var error = await new FileSystemQueryHandler().ProcessAsync("no such folder", false, vm);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "not found");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
