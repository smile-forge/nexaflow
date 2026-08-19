using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

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

        public string? Create(string folderPath, string fileName)
        {
            LastFolder = folderPath;
            LastName   = fileName;
            var p = Path.Combine(folderPath, fileName);
            if (ext.Length == 0) Directory.CreateDirectory(p); else File.WriteAllText(p, string.Empty);
            return p;
        }
    }

    private static FileSystemViewModel AtPath(string path)
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return new FileSystemViewModel(path, shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexavm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void OpenCreateOverlay_PopulatesSelectsAndPrefills()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt"), new FakeCreateAction("")]);

            Assert.IsTrue(vm.CreateOverlayVisible);
            Assert.AreEqual(2, vm.CreateActions.Count);
            Assert.IsNotNull(vm.SelectedCreateAction);
            Assert.IsTrue(vm.CreateActions[0].IsSelected);
            Assert.AreEqual("New File.txt", vm.CreateFileName);
            Assert.IsTrue(vm.CanCreate);
            Assert.IsFalse(vm.CreateNameExists);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void SelectingFolderType_PrefillsNewFolder()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt"), new FakeCreateAction("")]);

            vm.CreateActions[1].SelectCommand.Execute(null);

            Assert.AreSame(vm.CreateActions[1], vm.SelectedCreateAction);
            Assert.IsTrue(vm.CreateActions[1].IsSelected);
            Assert.IsFalse(vm.CreateActions[0].IsSelected);
            Assert.AreEqual("New Folder", vm.CreateFileName);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void ExistingName_FlagsWarning_AndDisablesCreate()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "taken.txt"), "x");
            var vm = AtPath(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt")]);

            vm.CreateFileName = "taken";   // resolves to taken.txt

            Assert.IsTrue(vm.CreateNameExists);
            Assert.IsFalse(vm.CanCreate);
            Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Create_AppendsTypeExtension_InvokesActionWithResolvedName()
    {
        var dir = TempDir();
        try
        {
            var fake = new FakeCreateAction(".txt");
            var vm   = AtPath(dir);
            vm.OpenCreateOverlayWith([fake]);

            vm.CreateFileName = "memo";
            Assert.IsTrue(vm.CreateCommand.CanExecute(null));
            vm.CreateCommand.Execute(null);

            Assert.AreEqual("memo.txt", fake.LastName);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "memo.txt")));
            Assert.IsFalse(vm.CreateOverlayVisible);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Create_KeepsUserProvidedExtension()
    {
        var dir = TempDir();
        try
        {
            var fake = new FakeCreateAction(".txt");
            var vm   = AtPath(dir);
            vm.OpenCreateOverlayWith([fake]);

            vm.CreateFileName = "page.md";
            vm.CreateCommand.Execute(null);

            Assert.AreEqual("page.md", fake.LastName);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "page.md")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void CancelCreate_HidesOverlay()
    {
        var dir = TempDir();
        try
        {
            var vm = AtPath(dir);
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt")]);

            vm.CancelCreateCommand.Execute(null);

            Assert.IsFalse(vm.CreateOverlayVisible);
        }
        finally { Directory.Delete(dir, true); }
    }
}
