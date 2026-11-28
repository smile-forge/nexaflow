using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// What a tab lists, with no view over it: folders before files, sorted as the reader asks and filtered to one kind, counted
/// as it fills, caught up with when the folder changes, and held back while the shell is changing it.
/// </summary>
[TestClass]
[CoversNode("winfs-filelist")]
public class FolderListingTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = ModelHarness.Scratch();
        Directory.CreateDirectory(Path.Combine(_scratch, "zeta"));
        File.WriteAllText(Path.Combine(_scratch, "big.txt"), new string('x', 300));
        File.WriteAllText(Path.Combine(_scratch, "alpha.txt"), "a");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    [TestMethod]
    public async Task AFolderIsListed_FoldersFirst_ThenByName_AndCounted()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        CollectionAssert.AreEqual(new[] { "zeta", "alpha.txt", "big.txt" }, model.Listing.Entries.Select(e => e.Name).ToArray());
        Assert.AreEqual(1, model.Listing.FolderCount);
        Assert.AreEqual(2, model.Listing.FileCount);
        Assert.IsFalse(model.Listing.IsLoadingEntries);
    }

    [TestMethod]
    public async Task SortingByAColumnTwice_TurnsItRound_AndFoldersStayFirst()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        model.Listing.SortBy(nameof(FileSystemEntry.SizeBytes));
        CollectionAssert.AreEqual(new[] { "zeta", "alpha.txt", "big.txt" }, model.Listing.Entries.Select(e => e.Name).ToArray());

        model.Listing.SortBy(nameof(FileSystemEntry.SizeBytes));
        Assert.IsFalse(model.Listing.SortAscending);
        CollectionAssert.AreEqual(new[] { "zeta", "big.txt", "alpha.txt" }, model.Listing.Entries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void AFilter_LetsThroughOnlyItsKind_AndNoFilterLetsThroughEverything()
    {
        var folder = new FileSystemEntry { Name = "f", IsDirectory = true };
        var file   = new FileSystemEntry { Name = "g" };

        var folders = FolderListing.FilterFor(EntryFilter.FoldersOnly)!;
        var files   = FolderListing.FilterFor(EntryFilter.FilesOnly)!;

        Assert.IsTrue(folders(folder));
        Assert.IsFalse(folders(file));
        Assert.IsTrue(files(file));
        Assert.IsFalse(files(folder));
        Assert.IsNull(FolderListing.FilterFor(EntryFilter.None));
    }

    [TestMethod]
    public async Task TogglingAFilterTwice_ShowsEverythingAgain_AndBrowsingStartsTheNextFolderClean()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        model.Listing.ToggleFilter(EntryFilter.FilesOnly);
        Assert.AreEqual(EntryFilter.FilesOnly, model.Listing.ActiveFilter);
        model.Listing.ToggleFilter(EntryFilter.FilesOnly);
        Assert.AreEqual(EntryFilter.None, model.Listing.ActiveFilter);

        model.Listing.ToggleFilter(EntryFilter.FoldersOnly);
        model.Listing.SortBy(nameof(FileSystemEntry.Modified));
        await model.Navigation.BrowseToAsync(Path.Combine(_scratch, "zeta"));

        Assert.AreEqual(EntryFilter.None, model.Listing.ActiveFilter);
        Assert.AreEqual(nameof(FileSystemEntry.Name), model.Listing.SortColumn);
        Assert.IsTrue(model.Listing.SortAscending);
    }

    [TestMethod]
    public async Task AChangeTheShellReports_IsReadInPlace_WhileTheViewIsShown()
    {
        var shell = ModelHarness.Shell();
        Action<FolderChange>? changed = null;
        shell.WatchFolder(Arg.Any<string>(), Arg.Do<Action<FolderChange>>(callback => changed = callback));

        var model = await ModelHarness.AtAsync(_scratch, shell);
        model.Listing.AttachFolderWatching();
        Assert.IsNotNull(changed, "the open folder is watched while the view is shown");

        File.WriteAllText(Path.Combine(_scratch, "new.txt"), "n");
        changed!(new FolderChange(new HashSet<string>(), Whole: true));
        await model.Listing.EntriesLoaded;

        CollectionAssert.Contains(model.Listing.Entries.Select(e => e.Name).ToArray(), "new.txt");
        Assert.AreEqual(3, model.Listing.FileCount);
    }

    [TestMethod]
    public async Task AFolderTheShellIsChanging_IsNotRead_AndItsClearingIsAnnounced()
    {
        var shell = ModelHarness.Shell();
        shell.GetFolderBusyMessage(_scratch).Returns("Deleting…");
        var model = await ModelHarness.AtAsync(_scratch, shell);

        model.Listing.AttachBusyTracking();
        Assert.IsTrue(model.Listing.IsFolderBusy);
        Assert.AreEqual("Deleting…", model.Listing.FolderBusyMessage);

        model.Listing.ReadFolder(inPlace: true);
        await model.Listing.EntriesLoaded;
        Assert.AreEqual(0, model.Listing.Entries.Count, "a folder the shell is changing is not enumerated");

        var cleared = false;
        model.Listing.FolderBusyCleared += () => cleared = true;
        shell.GetFolderBusyMessage(_scratch).Returns((string?)null);
        shell.FolderBusyChanged += Raise.Event<Action>();

        Assert.IsTrue(cleared);
        Assert.IsFalse(model.Listing.IsFolderBusy);
    }
}
