using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.VirtualDisk.Handlers;
using Nexaflow.Features.VirtualDisk.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Features.VirtualDisk;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The disk inspector answering <c>?</c>.
/// <para>
/// The image is a real VHD — the tab refuses to search one it can't read, so the metadata half has to be
/// genuine — while the <em>contents</em> come from a stubbed virtual filesystem. That split is deliberate:
/// FAT stores <c>readme.txt</c> as <c>README.TXT</c>, and a fixture whose case is decided by the filesystem
/// driver cannot state what case-sensitive search should do. Reading a real image through the VFS is
/// asserted separately, at the bottom, and by DiskImageArchiveHandlerTests.
/// </para>
/// <para>
/// What is worth pinning beyond the shared contract: the walk goes past the folders the tree has actually
/// loaded (the whole reason this isn't a filter over <c>VisibleRows</c>), a capped walk reports its total
/// as a floor, and dismissing the search re-reads the image root.
/// </para>
/// </summary>
[TestClass]
[CoversNode("vdisk-search")]
public class VirtualDiskSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    // Per-instance: MSTest runs test methods in parallel on separate instances.
    private string _dir = "";
    private string _vhd = "";

    //  <image root>
    //    ├─ docs/
    //    │    ├─ alpha42-guide.md   ← matches on its name, one level down
    //    │    └─ quiet.md           ← matches nothing (but IS reached by "*.md" and by "docs")
    //    ├─ alpha42.txt             ← matches on its name
    //    └─ readme.txt              ← matches nothing
    private const string RootHit   = "alpha42.txt";
    private const string DeepHit   = "docs/alpha42-guide.md";
    private const string DeepQuiet = "docs/quiet.md";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-dsearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _vhd = DiskSampleFactory.CreateFatVhd(_dir);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
             .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
             .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        return shell;
    }

    private static VirtualEntry Dir(string name)  => new(name, true, 0, 0, new DateTime(2026, 1, 1));
    private static VirtualEntry File(string name) => new(name, false, 12, 12, new DateTime(2026, 1, 1));

    /// <summary>The seeded contents, keyed by the in-image folder path the view-model asks for.</summary>
    private IVirtualFileSystem SeededVfs()
    {
        var vfs = Substitute.For<IVirtualFileSystem>();
        vfs.EnumerateEntries(_vhd)
           .Returns([Dir("docs"), File("alpha42.txt"), File("readme.txt")]);
        vfs.EnumerateEntries(Path.Combine(_vhd, "docs"))
           .Returns([File("alpha42-guide.md"), File("quiet.md")]);
        return vfs;
    }

    private async Task<VirtualDiskViewModel> BuildAsync(IVirtualFileSystem? vfs = null)
    {
        var vm = new VirtualDiskViewModel(_vhd, RunningShell(), vfs ?? SeededVfs());
        await vm.WhenLoaded;
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await BuildAsync();

    protected override string Snapshot(ISearchable page)
    {
        var vm = (VirtualDiskViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.IsSearchTruncated}|" +
               string.Join(",", vm.VisibleRows.Select(r => $"{r.InnerPath}{(r.IsSearchHit ? "*" : "")}"));
    }

    private static SearchRequest Query(string text) =>
        SearchSyntax.ParseRequest(text, [new GlobTermRecognizer()]);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private static string[] Rows(VirtualDiskViewModel vm) =>
        vm.VisibleRows.Select(r => r.InnerPath).ToArray();

    // ── Disk-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void TheWalkGoesPastTheFoldersTheTreeHasLoaded() => WithPage(async page =>
    {
        var vm = (VirtualDiskViewModel)page;
        Assert.IsFalse(Rows(vm).Contains(DeepHit),
            "the contents tree is lazy — nothing under 'docs' has been read yet");

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { RootHit, DeepHit }, Ids(outcome),
            "searching the rows on screen would answer a much smaller question than the one asked");
    });

    [TestMethod]
    public void HitIdsAreInImagePaths_SoTheyRoundTripIntoReadFile() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        Assert.IsTrue(outcome.Hits.Any(h => h.Id == DeepHit),
            "the id is the forward-slash in-image path — the same string read_file takes");
    });

    [TestMethod]
    public void DisplayingSearch_ShowsTheHitsUnderTheFoldersTheyLiveIn() => WithPage(async page =>
    {
        var vm = (VirtualDiskViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(new[] { "docs", DeepHit, RootHit }, Rows(vm),
            "folders first, then files — and 'docs' is opened, or the hit inside it is unreachable");
        Assert.AreEqual(2, vm.SearchMatchCount, "the folder is context, not a match");
        Assert.IsFalse(vm.VisibleRows.Single(r => r.InnerPath == "docs").IsSearchHit,
            "…and it says so, so the wash marks only what was asked for");
    });

    [TestMethod]
    public void AFolderNameMatch_BringsWhatIsInsideItWithIt() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("docs"), display: false, default);

        // Entries match on name OR path, so naming a folder answers with the folder and its contents.
        CollectionAssert.AreEqual(new[] { "docs", DeepHit, DeepQuiet }, Ids(outcome));
    });

    [TestMethod]
    public void AGlobIsJudgedAgainstTheEntryName_NotItsPath() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("*.md"), display: false, default);

        CollectionAssert.AreEqual(new[] { DeepHit, DeepQuiet }, Ids(outcome),
            "both .md files, and not the 'docs' folder their path runs through");
    });

    [TestMethod]
    public void ClearSearch_ReReadsTheImageRoot() => WithPage(async page =>
    {
        var vm = (VirtualDiskViewModel)page;
        var before = Rows(vm);
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        CollectionAssert.AreNotEqual(before, Rows(vm), "the search replaced the tree");

        await vm.ClearSearchCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        CollectionAssert.AreEqual(before, Rows(vm),
            "the filtered tree is a different tree, so the real one is read back rather than un-hidden");
        Assert.IsFalse(vm.VisibleRows.Any(r => r.IsSearchHit));
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenEntries() => WithPage(async page =>
    {
        var vm = (VirtualDiskViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == DeepHit);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        Assert.AreEqual(1, vm.SearchMatchCount);
        CollectionAssert.AreEqual(new[] { "docs", DeepHit }, Rows(vm),
            "the one entry the agent kept, still under the folder that says where it is");
    });

    [TestMethod]
    public void ShowResults_WithIdsThisPageNeverGave_DeclinesRatherThanEmptyingTheTree() => WithPage(async page =>
    {
        var vm = (VirtualDiskViewModel)page;
        var before = Rows(vm);

        var narrowed = await vm.ShowResultsAsync([new SearchHit("not/in/here.txt", "here.txt")], default);

        Assert.IsFalse(narrowed, "the agent needs to know it must describe the matches instead");
        CollectionAssert.AreEqual(before, Rows(vm));
    });

    [TestMethod]
    public void AWalkThatStopsAtItsCap_ReportsAFloorAndSaysSo() => RunUnpumped(async () =>
    {
        var many = Substitute.For<IVirtualFileSystem>();
        many.EnumerateEntries(_vhd)
            .Returns(Enumerable.Range(0, 250).Select(i => File($"alpha42-{i:D3}.txt")).ToList());
        var vm = await BuildAsync(many);

        var outcome = await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(200, outcome.MatchCount, "the hit cap bounds what comes back");
        StringAssert.Contains(outcome.Message ?? "", "there may be more");
        Assert.AreEqual("+", vm.SearchCountSuffix,
            "the chip shows a floor, so 200 never reads as the exact total");
    });

    [TestMethod]
    public void ACancelledWalk_Throws_RatherThanReportingAPartialSetAsComplete() => WithPage(async page =>
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => page.SearchAsync(Query("alpha42"), display: false, cts.Token));
    });

    [TestMethod]
    public void AnUnreadableImage_SaysThereIsNothingToSearch_NotThatSearchFailed() => RunUnpumped(async () =>
    {
        var notADisk = Path.Combine(_dir, "notadisk.vhd");
        System.IO.File.WriteAllBytes(notADisk, [0x00, 0x01, 0x02, 0x03]);
        var vm = new VirtualDiskViewModel(notADisk, RunningShell(), SeededVfs());
        await vm.WhenLoaded;

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed,
            "the page understood the query perfectly well — it has no filesystem to run it against, which " +
            "is a different thing from \"this page can't do patterns\"");
        StringAssert.Contains(outcome.Message ?? "", "nothing to search");
    });

    // ── …and the same thing over a real image, read through the real VFS ──────

    [TestMethod]
    public void OverARealImage_FindsAFileInAFolderThatWasNeverOpened() => RunUnpumped(async () =>
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new DiskImageArchiveHandler());
        var vm = await BuildAsync(vfs);
        Assert.IsFalse(Rows(vm).Any(p => p.Contains("guide", StringComparison.OrdinalIgnoreCase)),
            "the nested file has not been read");

        // Case-insensitively, because FAT decides the case of what it stored — which is exactly why the
        // rest of this suite seeds its contents through a stub.
        var outcome = await vm.SearchAsync(Query("guide"), display: false, default);

        Assert.IsTrue(outcome.Hits.Any(h => h.Id.Contains("guide", StringComparison.OrdinalIgnoreCase)),
            "the walk read the image, not the rows");
    });
}
