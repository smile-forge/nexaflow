using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Compressed.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The archive inspector answering <c>?</c> over a real zip.
/// <para>
/// What is worth pinning beyond the shared contract: the search reaches entries in folders the user never
/// opened (the manifest is searched, not the visible rows), the answer is a <em>filtered tree</em> rather
/// than a flat list — the folders above a hit stay, unmarked, or a deep hit is a name with no context —
/// and rewriting the archive drops the search rather than leaving it pointing at entries that no longer
/// exist.
/// </para>
/// </summary>
[TestClass]
[CoversNode("compressed-search")]
public class CompressedSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    // Per-instance: MSTest runs test methods in parallel on separate instances, so a shared temp folder
    // would have one test's fixture deleted out from under another.
    private string _dir = "";
    private string _zip = "";

    //  a.zip
    //    ├─ alpha42.txt              ← matches on its name
    //    ├─ readme.txt               ← matches nothing
    //    └─ docs/
    //         ├─ alpha42-guide.md    ← matches on its name, one level down
    //         └─ quiet.md            ← matches nothing (but IS reached by "*.md" and by "docs")
    private const string RootHit   = "alpha42.txt";
    private const string DeepHit   = "docs/alpha42-guide.md";
    private const string DeepQuiet = "docs/quiet.md";
    private const string RootQuiet = "readme.txt";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexa-csearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _zip = Path.Combine(_dir, "a.zip");

        using var zip = ZipFile.Open(_zip, ZipArchiveMode.Create);
        foreach (var entry in new[] { RootHit, RootQuiet, DeepHit, DeepQuiet })
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write("body");
        }
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

    private CompressedViewModel Build()
    {
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        return new CompressedViewModel(_zip, RunningShell(), vfs);
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (CompressedViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               string.Join(",", vm.VisibleRows.Select(r => $"{r.ArchivePath}{(r.IsSearchHit ? "*" : "")}"));
    }

    private static SearchRequest Query(string text) =>
        SearchSyntax.ParseRequest(text, [new GlobTermRecognizer()]);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private static string[] Rows(CompressedViewModel vm) =>
        vm.VisibleRows.Select(r => r.ArchivePath).ToArray();

    // ── Archive-specific behaviour beyond the shared contract ─────────────────

    [TestMethod]
    public void TheWholeManifestIsSearched_NotJustTheRowsOnScreen() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;

        // Collapse everything first: without it the tab's own auto-expand would hide the fact that the
        // search walks the manifest rather than the visible rows.
        foreach (var folder in vm.VisibleRows.Where(r => r.IsFolder).ToList())
            vm.ActivateRowCommand.Execute(folder);
        Assert.IsFalse(Rows(vm).Contains(DeepHit), "the nested entry is not on screen");

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { RootHit, DeepHit }, Ids(outcome),
            "an entry inside a collapsed folder is still an entry in this archive");
    });

    [TestMethod]
    public void HitIdsAreArchivePaths_SoTheyRoundTripIntoReadEntry() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        Assert.IsTrue(outcome.Hits.Any(h => h.Id == DeepHit),
            "the id is the full in-archive path — the same string read_entry takes");
        Assert.IsTrue(outcome.Hits.All(h => !h.Id.StartsWith('/')));
    });

    [TestMethod]
    public void DisplayingSearch_FiltersToTheHits_KeepingTheFoldersAboveThem() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(new[] { "docs", DeepHit, RootHit }, Rows(vm),
            "folders first, then files — the tree's own order, narrowed");
        Assert.AreEqual(2, vm.SearchMatchCount, "the folder is context, not a match");
        Assert.IsFalse(vm.VisibleRows.Single(r => r.ArchivePath == "docs").IsSearchHit,
            "…and it says so, so the wash marks only what was asked for");
        Assert.IsTrue(vm.VisibleRows.Single(r => r.ArchivePath == "docs").IsExpanded,
            "the folder is opened, or reaching the hit still takes a click");
    });

    [TestMethod]
    public void AFolderNameMatch_BringsWhatIsInsideItWithIt() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;

        var outcome = await vm.SearchAsync(Query("docs"), display: true, default);

        // Entries match on name OR path, so naming a folder answers with the folder and its contents —
        // which is the useful answer to "what's in docs", and the reason the rule is name-or-path.
        CollectionAssert.AreEqual(new[] { "docs", DeepHit, DeepQuiet }, Ids(outcome));
        CollectionAssert.AreEqual(new[] { "docs", DeepHit, DeepQuiet }, Rows(vm));
    });

    [TestMethod]
    public void AGlobIsJudgedAgainstTheEntryName_NotItsPath() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("*.md"), display: false, default);

        CollectionAssert.AreEqual(new[] { DeepHit, DeepQuiet }, Ids(outcome),
            "both .md files, and not the 'docs' folder their path runs through");
    });

    [TestMethod]
    public void AGlobAndAWordCompose() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("*.md alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { DeepHit }, Ids(outcome),
            "a .md file that also carries the word — each term judged on its own field");
    });

    [TestMethod]
    public void ZeroMatches_StillShowsTheChip_WithAnEmptyTree() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;

        var outcome = await vm.SearchAsync(Query("nothinghere"), display: true, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed, "running and finding nothing is not a failure");
        Assert.IsTrue(vm.IsSearchActive, "\"no matches for X\" is a result the user has to be able to see");
        Assert.AreEqual(0, vm.VisibleRows.Count);
    });

    [TestMethod]
    public void ClearSearch_PutsBackTheTree_AndTheFoldersTheUserHadOpen() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;
        var docs = vm.VisibleRows.Single(r => r.ArchivePath == "docs");
        if (docs.IsExpanded) vm.ActivateRowCommand.Execute(docs);   // the user closed it
        var before = Rows(vm);

        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(docs.IsExpanded, "the search opened it");

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.IsFalse(docs.IsExpanded, "dismissing the search hands back the tree as it was, not splayed open");
        CollectionAssert.AreEqual(before, Rows(vm));
        Assert.IsFalse(vm.VisibleRows.Any(r => r.IsSearchHit));
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenEntries() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;
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
        var vm = (CompressedViewModel)page;
        var before = Rows(vm);

        var narrowed = await vm.ShowResultsAsync([new SearchHit("not/in/here.txt", "here.txt")], default);

        Assert.IsFalse(narrowed, "the agent needs to know it must describe the matches instead");
        CollectionAssert.AreEqual(before, Rows(vm));
    });

    [TestMethod]
    public void RewritingTheArchive_DropsTheSearch() => WithPage(async page =>
    {
        var vm = (CompressedViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        var added = Path.Combine(_dir, "extra.txt");
        File.WriteAllText(added, "x");
        await vm.AddSourcesAsync([added]);

        Assert.IsFalse(vm.IsSearchActive,
            "the tree is rebuilt from the new archive — the filter's rows are entries that no longer exist");
        Assert.IsTrue(Rows(vm).Contains(RootQuiet), "and the whole archive is back on screen");
        Assert.IsTrue(Rows(vm).Contains("extra.txt"));
    });
}
