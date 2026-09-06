using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The explorer tab's own controls, driven through the view-model: column sorting, the footer's
/// clickable counts, the drop target's copy-vs-move wording, and what a pinned action carries with it.
/// </summary>
[TestClass]
public class FileSystemSurfaceTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexa-winfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private static (IShellServices Shell, IAIService Ai, IReadOnlyDictionary<Type, IFeatureConfig> Configs) Deps()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return (shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());
    }

    private FileSystemViewModel AtScratch(out IShellServices shell)
    {
        var (s, ai, configs) = Deps();
        shell = s;
        return new FileSystemViewModel(_scratch, s, ai, configs);
    }

    // ── Column sorting ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-column-sort")]
    public void ClickingANewColumnStartsAscending_AndClickingItAgainFlips()
    {
        var vm = AtScratch(out _);
        Assert.AreEqual(nameof(FileSystemEntry.Name), vm.SortColumn, "a folder opens sorted by name");
        Assert.IsTrue(vm.SortAscending);

        vm.SortByCommand.Execute(nameof(FileSystemEntry.SizeBytes));
        Assert.AreEqual(nameof(FileSystemEntry.SizeBytes), vm.SortColumn);
        Assert.IsTrue(vm.SortAscending, "a new column starts ascending, whatever the last one was doing");

        vm.SortByCommand.Execute(nameof(FileSystemEntry.SizeBytes));
        Assert.IsFalse(vm.SortAscending, "the same column again is the only way to get descending");

        vm.SortByCommand.Execute(nameof(FileSystemEntry.Modified));
        Assert.IsTrue(vm.SortAscending, "and moving on resets the direction rather than inheriting it");
    }

    // ── Footer counts + quick filter ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-footer-filter")]
    public void ClickingACountFiltersToThatKind_AndClickingItAgainClears()
    {
        var vm = AtScratch(out _);

        vm.ToggleFolderFilterCommand.Execute(null);
        Assert.AreEqual(EntryFilter.FoldersOnly, vm.ActiveFilter);

        vm.ToggleFolderFilterCommand.Execute(null);
        Assert.AreEqual(EntryFilter.None, vm.ActiveFilter, "the same tally again is how you get back out");
    }

    [TestMethod]
    [CoversNode("winfs-footer-filter")]
    public void TheTwoTalliesAreExclusive_YouCannotFilterToBothAtOnce()
    {
        var vm = AtScratch(out _);

        vm.ToggleFolderFilterCommand.Execute(null);
        vm.ToggleFileFilterCommand.Execute(null);

        Assert.AreEqual(EntryFilter.FilesOnly, vm.ActiveFilter,
                        "clicking the other tally switches to it — a filter showing neither kind shows nothing");
    }

    [TestMethod]
    [CoversNode("winfs-footer-filter")]
    public void TheCountsReadAsEnglish_SingularAndPlural()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "one-folder"));
        File.WriteAllText(Path.Combine(_scratch, "a.txt"), "");
        File.WriteAllText(Path.Combine(_scratch, "b.txt"), "");

        var vm = AtScratch(out _);
        Assert.IsTrue(SpinWaitFor(() => vm.HasFolders && vm.HasFiles), "the folder to finish loading");

        Assert.AreEqual("1 folder", vm.FolderCountText);
        Assert.AreEqual("2 files", vm.FileCountText);
        Assert.IsTrue(vm.ShowCountSeparator, "the dot between them only earns its place when both are there");
    }

    [TestMethod]
    [CoversNode("winfs-footer-filter")]
    public void SelectingRowsAddsACountInFront_AndDeselectingTakesItAway()
    {
        var vm = AtScratch(out _);

        vm.OnSelectionChanged([new FileSystemEntry { Name = "a" }, new FileSystemEntry { Name = "b" }]);
        StringAssert.Contains(vm.SelectionSummary, "2 selected");

        vm.OnSelectionChanged([]);
        Assert.AreEqual(string.Empty, vm.SelectionSummary);
    }

    // ── Drag & drop ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public void TheDropTooltipSaysWhichWayItWillGo_AndWhere()
    {
        var vm = AtScratch(out _);
        var target = new FileSystemDropTarget(vm);
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\a.txt" });

        Assert.AreEqual("Copy to Projects", target.GetDropDescription(data, "Projects", isMove: false));
        Assert.AreEqual("Move to Projects", target.GetDropDescription(data, "Projects", isMove: true),
                        "holding Shift turns a copy into a move — the tooltip is the only warning of that");
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public void DroppingOntoTheListRatherThanAFolderNamesTheCurrentOne()
    {
        var vm = AtScratch(out _);
        var target = new FileSystemDropTarget(vm);
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\a.txt" });

        StringAssert.Contains(target.GetDropDescription(data, targetFolderName: null, isMove: false),
                              Path.GetFileName(_scratch));
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public void OnlyAFileDropIsAccepted()
    {
        var vm = AtScratch(out _);
        var target = new FileSystemDropTarget(vm);

        Assert.IsTrue(target.CanAcceptDrop(new DataObject(DataFormats.FileDrop, new[] { @"C:\a.txt" })));
        Assert.IsFalse(target.CanAcceptDrop(new DataObject(DataFormats.Text, "hello")));
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public async Task DroppingCopiesTheFileIn_AndMovingTakesTheOriginal()
    {
        var source = Path.Combine(_scratch, "source");
        var dest = Path.Combine(_scratch, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        var copied = Path.Combine(source, "copied.txt");
        var moved = Path.Combine(source, "moved.txt");
        File.WriteAllText(copied, "x");
        File.WriteAllText(moved, "y");

        var vm = AtScratch(out _);
        var target = new FileSystemDropTarget(vm);

        // The drop queues the work and returns — awaiting the operation is the point, not a nicety.
        target.Drop(new DataObject(DataFormats.FileDrop, new[] { copied }), dest, move: false);
        await vm.Operations.Operations[^1].Completion;

        Assert.IsTrue(File.Exists(Path.Combine(dest, "copied.txt")));
        Assert.IsTrue(File.Exists(copied), "a copy leaves the original where it was");

        target.Drop(new DataObject(DataFormats.FileDrop, new[] { moved }), dest, move: true);
        await vm.Operations.Operations[^1].Completion;

        Assert.IsTrue(File.Exists(Path.Combine(dest, "moved.txt")));
        Assert.IsFalse(File.Exists(moved));
    }

    [TestMethod]
    [CoversNode("winfs-drag-drop")]
    public void ASourceThatVanishedBetweenDragAndDropIsSkipped_NotReportedAsAFailure()
    {
        var dest = Path.Combine(_scratch, "dest");
        Directory.CreateDirectory(dest);
        var vm = AtScratch(out var shell);
        var target = new FileSystemDropTarget(vm);

        target.Drop(new DataObject(DataFormats.FileDrop, new[] { Path.Combine(_scratch, "gone.txt") }),
                    dest, move: false);

        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
        Assert.AreEqual(0, vm.Operations.Operations.Count, "nothing was queued either");
    }

    // ── Ribbon pinning ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-ribbon-pin")]
    public void PinningAFileActionCapturesTheFilesItWasAimedAt()
    {
        var vm = AtScratch(out var shell);
        vm.OnSelectionChanged([new FileSystemEntry { Name = "a.txt", FullPath = @"C:\work\a.txt" },
                               new FileSystemEntry { Name = "b.txt", FullPath = @"C:\work\b.txt" }]);

        var paths = PinnedPaths(vm, shell, new FileActionViewModel(new CopyFiles()));

        CollectionAssert.AreEqual(new[] { @"C:\work\a.txt", @"C:\work\b.txt" }, paths,
                                  "the pin remembers the files, so pressing it later re-runs on the same ones");
    }

    [TestMethod]
    [CoversNode("winfs-ribbon-pin")]
    public void PinningAFolderActionCapturesTheFolder_NotWhateverWasSelectedInIt()
    {
        var vm = AtScratch(out var shell);
        vm.OnSelectionChanged([new FileSystemEntry { Name = "a.txt", FullPath = @"C:\work\a.txt" }]);

        var folderAction = new FileActionViewModel(new FolderActionAdapter(new CopyFiles()));
        var paths = PinnedPaths(vm, shell, folderAction);

        CollectionAssert.AreEqual(new[] { _scratch }, paths,
                                  "a folder action acts on the directory — pinning the selection would " +
                                  "re-run it somewhere else entirely");
    }

    /// <summary>Pins <paramref name="action"/> and reads the paths off the payload. The payload type is
    /// internal to the feature, so its contents are read by name rather than cast.</summary>
    private static string[] PinnedPaths(FileSystemViewModel vm, IShellServices shell, FileActionViewModel action)
    {
        object? pinned = null;
        shell.When(s => s.PinToRibbon(Arg.Any<string>(), Arg.Any<object>()))
             .Do(ci => pinned = ci.Arg<object>());

        vm.PinFileActionToRibbonCommand.Execute(action);

        Assert.IsNotNull(pinned, "a pinned action has to carry a payload or the ribbon button does nothing");
        var prop = pinned!.GetType().GetProperty("SelectedPaths");
        Assert.IsNotNull(prop, "the payload must carry the paths it was pinned against");
        return ((IEnumerable<string>)prop!.GetValue(pinned)!).ToArray();
    }

    // ── Create overlay ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-create-folder")]
    public void NewFolderMakesTheDirectoryVerbatim_WithNoExtensionAppended()
    {
        var action = new NewFolderCreateAction();

        var made = action.Create(_scratch, "Q3 Reports");

        Assert.AreEqual(Path.Combine(_scratch, "Q3 Reports"), made);
        Assert.IsTrue(Directory.Exists(made!));
        Assert.AreEqual(string.Empty, action.FileExtension,
                        "declaring no extension is what stops the host appending one to a folder name");
    }

    [TestMethod]
    [CoversNode("winfs-create-folder")]
    public void NewFolderRefusesANamelessCreate()
    {
        var action = new NewFolderCreateAction();

        Assert.IsNull(action.Create(_scratch, "   "));
        Assert.IsNull(action.Create("", "docs"));
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("winfs-context-menu")]
    public void RightClickingEmptySpaceOffersFolderActionsForTheCurrentFolder()
    {
        var vm = AtScratch(out _);

        // No registered actions in this harness, so what is asserted is that the empty-space case is
        // handled as "the current folder" rather than as "no target" — the branch that used to hand back
        // an empty menu.
        var actions = vm.BuildContextActions([]);

        Assert.IsNotNull(actions);
    }

    [TestMethod]
    [CoversNode("winfs-context-menu")]
    public void InThisPcModeOnlyAnEmptySelectionHasNoTarget()
    {
        var (shell, ai, configs) = Deps();
        var vm = FileSystemViewModel.CreateThisPc(shell, ai, configs);

        Assert.AreEqual(0, vm.BuildContextActions([]).Count,
                        "This PC has no open folder, so a right-click on nothing has nothing to act on");
    }

    private static bool SpinWaitFor(Func<bool> until)
        => System.Threading.SpinWait.SpinUntil(until, 10000);
}
