using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("winfs-create")]
public class FileSystemViewModelCreateTests
{
    /// <summary>Records the create call and materialises the item so Refresh has something to read.</summary>
    private sealed class FakeCreateAction(string ext) : IFileCreateAction
    {
        public string  Icon          => "🧪";
        public string  DisplayName   => ext.Length == 0 ? "Folder" : "File" + ext;
        public string  FileExtension => ext;
        public string? LastFolder;
        public string? LastName;

        public Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
        {
            LastFolder = folderPath;
            LastName   = fileName;
            var p = Path.Combine(folderPath, fileName);
            if (ext.Length == 0) Directory.CreateDirectory(p); else File.WriteAllText(p, string.Empty);
            return Task.FromResult<string?>(p);
        }
    }

    private static async Task<FileSystemViewModel> AtPathAsync(string path)
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        var vm = new FileSystemViewModel(path, shell, ai, new Dictionary<Type, IFeatureConfig>(), VirtualFileSystem.Instance);
        await vm.Browsing;
        return vm;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(TestTemp.Root, "nexavm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public async Task OpenCreateOverlay_PopulatesSelectsAndPrefills()
    {
        var dir = TempDir();
        try
        {
            var vm = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt"), new FakeCreateAction("")]);
            await vm.CreateNameChecked;

            Assert.IsTrue(vm.CreateOverlayVisible);
            Assert.AreEqual(2, vm.CreateActions.Count);
            Assert.IsNotNull(vm.SelectedCreateAction);
            Assert.IsTrue(vm.CreateActions[0].IsSelected);
            Assert.AreEqual(Str.Get("WindowsFileSystem.Create.NewFileName") + ".txt", vm.CreateFileName);
            Assert.IsTrue(vm.CanCreate);
            Assert.IsFalse(vm.CreateNameExists);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task SelectingFolderType_PrefillsNewFolder()
    {
        var dir = TempDir();
        try
        {
            var vm = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt"), new FakeCreateAction("")]);

            vm.CreateActions[1].SelectCommand.Execute(null);

            Assert.AreSame(vm.CreateActions[1], vm.SelectedCreateAction);
            Assert.IsTrue(vm.CreateActions[1].IsSelected);
            Assert.IsFalse(vm.CreateActions[0].IsSelected);
            Assert.AreEqual(Str.Format("WindowsFileSystem.Create.NewItemFormat", "Folder"), vm.CreateFileName);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task ExistingName_FlagsWarning_AndDisablesCreate()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "taken.txt"), "x");
            var vm = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt")]);

            vm.CreateFileName = "taken";   // resolves to taken.txt
            Assert.IsFalse(vm.CanCreate, "nothing is created while the folder is still being asked");
            await vm.CreateNameChecked;

            Assert.IsTrue(vm.CreateNameExists);
            Assert.IsFalse(vm.CanCreate);
            Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task Create_AppendsTypeExtension_InvokesActionWithResolvedName()
    {
        var dir = TempDir();
        try
        {
            var fake = new FakeCreateAction(".txt");
            var vm   = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([fake]);

            vm.CreateFileName = "memo";
            await vm.CreateNameChecked;
            Assert.IsTrue(vm.CreateCommand.CanExecute(null));
            await vm.CreateCommand.ExecuteAsync(null);

            Assert.AreEqual("memo.txt", fake.LastName);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "memo.txt")));
            Assert.IsFalse(vm.CreateOverlayVisible);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task Create_KeepsUserProvidedExtension()
    {
        var dir = TempDir();
        try
        {
            var fake = new FakeCreateAction(".txt");
            var vm   = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([fake]);

            vm.CreateFileName = "page.md";
            await vm.CreateNameChecked;
            await vm.CreateCommand.ExecuteAsync(null);

            Assert.AreEqual("page.md", fake.LastName);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "page.md")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task CancelCreate_HidesOverlay()
    {
        var dir = TempDir();
        try
        {
            var vm = await AtPathAsync(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt")]);

            vm.CancelCreateCommand.Execute(null);

            Assert.IsFalse(vm.CreateOverlayVisible);
        }
        finally { Directory.Delete(dir, true); }
    }
}
