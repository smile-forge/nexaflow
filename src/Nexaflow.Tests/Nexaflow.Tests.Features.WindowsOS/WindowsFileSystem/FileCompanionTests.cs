using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Companion files — a file's sidecar, named after it plus a suffix — as the file browser treats them: out of the
/// list while their file is beside them, a badge on that file, and along for every operation on it, renamed to
/// follow it. The suffixes are this test's own (<see cref="TestCompanions"/>); what a feature means by one is not
/// the browser's business.
/// </summary>
[TestClass]
[CoversNode("winfs-companions")]
public class FileCompanionTests
{
    /// <summary>A photo-and-recipe pair, as the image editor declares it, plus a badge-less swap file.</summary>
    public sealed class TestCompanions : IFileCompanionProvider
    {
        public IReadOnlyList<FileCompanionKind> Companions { get; } =
            [new(".nfedit", "✎", "edited"), new(".nfedit.tmp")];
    }

    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(TestTemp.Root, "nexa-companions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public async Task RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCompanionProvider>().Returns([typeof(TestCompanions)]);
        return shell;
    }

    private string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(_scratch, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), name + "/" + f);
        return dir;
    }

    private static async Task<FileSystemViewModel> ListedAsync(string path, IShellServices shell)
    {
        var vm = new FileSystemViewModel(path, shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), VirtualFileSystem.Instance);
        for (var i = 0; i < 400 && (vm.IsLoadingEntries || vm.Entries.Count == 0); i++) await Task.Delay(25);
        return vm;
    }

    // ── The list ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ACompanionBesideItsFileIsHidden_TheFileIsBadged_AndAnOrphanIsShown()
    {
        var dir = Folder("photos", "photo.jpg", "photo.jpg.nfedit", "gone.png.nfedit", "notes.txt", "photo.jpg.nfedit.tmp");

        var vm = await ListedAsync(dir, Shell());

        CollectionAssert.AreEquivalent(new[] { "gone.png.nfedit", "notes.txt", "photo.jpg" }, vm.Entries.Select(e => e.Name).ToArray(),
                                       "a companion goes with its file; one whose file is gone is listed, so it can be found");
        var photo = vm.Entries.Single(e => e.Name == "photo.jpg");
        Assert.AreEqual("✎", photo.Companion?.Badge);
        Assert.IsNull(vm.Entries.Single(e => e.Name == "notes.txt").Companion);
        Assert.AreEqual("3 files", vm.FileCountText, "and the count is of what is listed");
    }

    [TestMethod]
    public async Task InAFolderBigEnoughToStream_ACompanionReadBeforeItsFileIsStillPaired()
    {
        var dir = Folder("big");
        // Names sort the companion first, which is the order NTFS enumerates them in.
        File.WriteAllText(Path.Combine(dir, "a.jpg.nfedit"), "recipe");
        for (int i = 0; i < 1100; i++) File.WriteAllText(Path.Combine(dir, $"b{i:0000}.txt"), string.Empty);
        File.WriteAllText(Path.Combine(dir, "a.jpg"), "photo");

        var vm = await ListedAsync(dir, Shell());
        for (var i = 0; i < 400 && vm.Entries.Count < 1101; i++) await Task.Delay(25);

        Assert.AreEqual(1101, vm.Entries.Count);
        Assert.IsFalse(vm.Entries.Any(e => e.Name == "a.jpg.nfedit"));
        Assert.AreEqual("✎", vm.Entries.Single(e => e.Name == "a.jpg").Companion?.Badge);
    }

    // ── Operations ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public async Task ACopyTakesTheCompanion_RenamedToFollowItsFile()
    {
        var src  = Folder("src", "photo.jpg", "photo.jpg.nfedit");
        var dest = Folder("dest", "photo.jpg");   // the name is taken, so the copy becomes photo (2).jpg

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([Path.Combine(src, "photo.jpg")], dest, move: false);
        await op!.Completion;

        Assert.AreEqual("src/photo.jpg", File.ReadAllText(Path.Combine(dest, "photo (2).jpg")));
        Assert.AreEqual("src/photo.jpg.nfedit", File.ReadAllText(Path.Combine(dest, "photo (2).jpg.nfedit")),
                        "the recipe pairs with the copy's new name, not the name it had");
        Assert.IsFalse(File.Exists(Path.Combine(dest, "photo.jpg.nfedit")), "and the file already there gains nothing");
        Assert.IsTrue(File.Exists(Path.Combine(src, "photo.jpg.nfedit")), "a copy leaves the original pair alone");
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public async Task AMoveTakesTheCompanion_EvenWhenItCameAlongAsASourceToo()
    {
        var src  = Folder("src", "photo.jpg", "photo.jpg.nfedit");
        var dest = Folder("dest");

        // A clipboard or another program's drag carries the companion as a source of its own.
        var op = FileOperationQueue.For(Shell()).EnqueueDrop(
            [Path.Combine(src, "photo.jpg"), Path.Combine(src, "photo.jpg.nfedit")], dest, move: true);
        await op!.Completion;

        Assert.IsTrue(File.Exists(Path.Combine(dest, "photo.jpg")));
        Assert.IsTrue(File.Exists(Path.Combine(dest, "photo.jpg.nfedit")));
        Assert.AreEqual(2, Directory.GetFiles(dest).Length, "moved once, not a second time as 'photo.jpg (2).nfedit'");
        Assert.AreEqual(0, Directory.GetFiles(src).Length);
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public async Task AFileCopiedOverAnotherReplacesTheOthersCompanion_RatherThanInheritingIt()
    {
        var src  = Folder("src", "photo.jpg");
        var dest = Folder("dest", "photo.jpg.nfedit");   // a recipe whose photo is gone

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([Path.Combine(src, "photo.jpg")], dest, move: false);
        await op!.Completion;

        Assert.IsTrue(File.Exists(Path.Combine(dest, "photo.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(dest, "photo.jpg.nfedit")),
                       "the recipe described another photo; the new one must not wear it");
    }

    [TestMethod]
    [CoversNode("winfs-act-delete")]
    public async Task DeletingAFileDeletesItsCompanion_PermanentlyOrToTheRecycleBin()
    {
        var dir = Folder("photos", "a.jpg", "a.jpg.nfedit", "b.jpg", "b.jpg.nfedit");
        var queue = FileOperationQueue.For(Shell());

        await queue.EnqueueDelete([Path.Combine(dir, "a.jpg")], permanent: true)!.Completion;
        Assert.IsFalse(File.Exists(Path.Combine(dir, "a.jpg.nfedit")));

        await queue.EnqueueDelete([Path.Combine(dir, "b.jpg")], permanent: false)!.Completion;
        Assert.IsFalse(File.Exists(Path.Combine(dir, "b.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "b.jpg.nfedit")), "recycled together, restored together");
    }

    [TestMethod]
    public async Task RenamingAFileRenamesItsCompanion()
    {
        var dir = Folder("photos", "photo.jpg", "photo.jpg.nfedit");
        var shell = Shell();
        shell.When(s => s.ShowPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<string>>(), Arg.Any<Action>()))
             .Do(call => call.ArgAt<Action<string>>(3)("holiday.jpg"));
        // The rename runs once the prompt is answered, and asks for a refresh when it is done.
        var renamed = new TaskCompletionSource();
        shell.When(s => s.RequestRefresh()).Do(_ => renamed.TrySetResult());

        await new RenameFile(shell, VirtualFileSystem.Instance).PerformActionAsync(Path.Combine(dir, "photo.jpg"));
        await renamed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(File.Exists(Path.Combine(dir, "holiday.jpg")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "holiday.jpg.nfedit")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "photo.jpg.nfedit")));
    }

    [TestMethod]
    public void WhatLeavesForAnotherProgram_CarriesTheCompanions()
    {
        var dir = Folder("photos", "photo.jpg", "photo.jpg.nfedit", "notes.txt");
        var companions = FileCompanions.For(Shell());

        var carried = companions.WithCompanions([Path.Combine(dir, "photo.jpg"), Path.Combine(dir, "notes.txt")]);

        CollectionAssert.AreEqual(
            new[] { Path.Combine(dir, "photo.jpg"), Path.Combine(dir, "photo.jpg.nfedit"), Path.Combine(dir, "notes.txt") },
            carried.ToArray(), "each file followed by its companions, nothing twice");

    }

    // ── Actions about a companion ───────────────────────────────────────────

    /// <summary>An action about the test companion, as the image editor's Revert is about its recipe.</summary>
    public sealed class RevertLike : IFileAction, ICacheable
    {
        public bool IsDestructive => false;
        public bool SupportsMultipleFiles => true;
        public string Icon => "↺";
        public string DisplayName => "Revert";
        public static string? StaticExperienceId => "/";
        public string ExperienceId => "/";
        public string ExperienceDescription => "All files";
        public bool RequiresRefresh => false;
        public bool CanPerformAction => true;
        public string? RequiredCompanionSuffix => ".nfedit";
        public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default) => Task.FromResult(true);
    }

    [TestMethod]
    [CoversNode("winfs-companion-actions")]
    public void AnActionAboutACompanion_IsOfferedOnlyWhereEverySelectedFileHasOne()
    {
        var dir = Folder("photos", "edited.jpg", "edited.jpg.nfedit", "plain.jpg");
        Directory.CreateDirectory(Path.Combine(dir, "album"));
        var action = new RevertLike();

        FileSystemEntry Entry(string name) => new() { Name = name, FullPath = Path.Combine(dir, name), IsDirectory = Directory.Exists(Path.Combine(dir, name)) };
        bool Offered(params string[] names) => FileActionManager.HasRequiredCompanions(action, [.. names.Select(Entry)], VirtualFileSystem.Instance);

        Assert.IsTrue(Offered("edited.jpg"));
        Assert.IsFalse(Offered("plain.jpg"), "nothing to revert");
        Assert.IsFalse(Offered("edited.jpg", "plain.jpg"), "every file in the selection, or it is not offered");
        Assert.IsFalse(Offered("album"), "a folder has no companion");
        Assert.IsTrue(FileActionManager.HasRequiredCompanions(new DeleteFile(Shell()), [Entry("plain.jpg")], VirtualFileSystem.Instance), "an action about no companion is unaffected");
    }
}
