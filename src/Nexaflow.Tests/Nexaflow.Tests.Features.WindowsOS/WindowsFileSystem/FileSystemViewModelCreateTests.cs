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

        public string? Create(string folderPath, string fileName)
        {
            LastFolder = folderPath;
            LastName   = fileName;
            var p = Path.Combine(folderPath, fileName);
            if (ext.Length == 0) Directory.CreateDirectory(p); else File.WriteAllText(p, string.Empty);
            return p;
        }
    }

    /// <summary>A kind that asks for options first: hands out <see cref="FakeOptions"/> opened on the folder it is given.</summary>
    private sealed class FakeOptionsAction(bool offered = true) : IFileCreateWithOptions
    {
        public string Icon          => "🎨";
        public string DisplayName   => "Picture";
        public string FileExtension => ".png";
        public bool   IsOffered     => offered;
        public readonly List<FakeOptions> Opened = [];

        public IFileCreateOptions OpenOptions(string folderPath)
        {
            var options = new FakeOptions(folderPath);
            Opened.Add(options);
            return options;
        }
    }

    private sealed class FakeOptions(string folder) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IFileCreateOptions
    {
        public string Folder { get; } = folder;
        public System.Windows.FrameworkElement View => throw new NotSupportedException("the view model never builds the view");
        public string? Created;
        public bool Disposed;

        private bool _canCreate;
        public bool CanCreate
        {
            get => _canCreate;
            set => SetProperty(ref _canCreate, value);
        }

        public string? Create(string fileName) => Created = Path.Combine(Folder, fileName);

        public void Dispose() => Disposed = true;
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
        var dir = Path.Combine(TestTemp.Root, "nexavm_" + Guid.NewGuid().ToString("N"));
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
            Assert.AreEqual(Str.Get("WindowsFileSystem.Create.NewFileName") + ".txt", vm.CreateFileName);
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
            Assert.AreEqual(Str.Format("WindowsFileSystem.Create.NewItemFormat", "Folder"), vm.CreateFileName);
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
    public void AKindThatAsksForOptions_OpensThemOnTheFolder_AndCreateWaitsOnThem_ThenMakesThroughThem()
    {
        var dir = TempDir();
        try
        {
            var vm    = AtPath(dir);
            var asks  = new FakeOptionsAction();
            vm.OpenCreateOverlayWith([new FakeCreateAction(".txt"), asks]);
            Assert.IsNull(vm.SelectedCreateOptions, "a name is enough for the first kind");

            vm.CreateActions[1].SelectCommand.Execute(null);

            var options = asks.Opened.Single();
            Assert.AreSame(options, vm.SelectedCreateOptions);
            Assert.AreEqual(dir, options.Folder);
            Assert.AreEqual(Str.Get("WindowsFileSystem.Create.NewFileName") + ".png", vm.CreateFileName);
            Assert.IsFalse(vm.CanCreate, "the options do not say enough yet");

            options.CanCreate = true;
            Assert.IsTrue(vm.CreateCommand.CanExecute(null), "Create follows them");

            vm.CreateFileName = "fox";
            vm.CreateCommand.Execute(null);

            Assert.AreEqual(Path.Combine(dir, "fox.png"), options.Created, "made through the options, the name resolved as for any kind");
            Assert.IsFalse(vm.CreateOverlayVisible);
            Assert.IsTrue(options.Disposed, "the picker closed, and its options with it");
            Assert.IsNull(vm.SelectedCreateOptions);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void ChoosingAnotherKind_OrCancelling_ClosesTheOptionsShown()
    {
        var dir = TempDir();
        try
        {
            var vm   = AtPath(dir);
            var asks = new FakeOptionsAction();
            vm.OpenCreateOverlayWith([asks, new FakeCreateAction(".txt")]);
            var first = asks.Opened.Single();

            vm.CreateActions[1].SelectCommand.Execute(null);
            Assert.IsTrue(first.Disposed);
            Assert.IsNull(vm.SelectedCreateOptions);
            Assert.IsTrue(vm.CanCreate, "a name is enough again");

            vm.CreateActions[0].SelectCommand.Execute(null);
            var second = asks.Opened[1];
            Assert.AreNotSame(first, second, "each choice opens them afresh");

            vm.CancelCreateCommand.Execute(null);
            Assert.IsTrue(second.Disposed);
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
