using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Scratchpad;
using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.Services;
using Nexaflow.Features.Scratchpad.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The scratchpad board answering <c>?</c> over three real notes.
/// <para>
/// What is worth pinning beyond the shared contract: the notes that missed are <em>hidden</em>, not marked
/// — a mark on a note two screens away is a mark nobody sees — the board pans to each hit in turn without
/// touching the user's zoom, and each surviving note is handed the query so its own body can paint it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("scratchpad-search")]
public class ScratchpadSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    // Per-instance: MSTest runs test methods in parallel on separate instances.
    private string _root = "";
    private PostItStore _store = null!;
    private ScratchpadConfig _config = null!;

    [TestInitialize]
    public void Setup()
    {
        _root   = Path.Combine(Path.GetTempPath(), "nexa-padsearch-" + Guid.NewGuid().ToString("N"));
        _store  = new PostItStore(_root);
        _config = new ScratchpadConfig();
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
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

    //  Laid out so board order (top row first, then left to right) is unambiguous:
    //    (10,10)  "alpha42 — call the vendor"      ← hit, first in reading order
    //    (400,10) "milk, eggs"                     ← miss
    //    (10,400) "ring back about alpha42"        ← hit, second
    private ScratchpadViewModel Build()
    {
        var vm = new ScratchpadViewModel(_config, _store, RunningShell());
        Add(vm, "alpha42 — call the vendor", 10, 10);
        Add(vm, "milk, eggs",                400, 10);
        Add(vm, "ring back about alpha42",   10, 400);
        return vm;
    }

    private static void Add(ScratchpadViewModel vm, string content, double x, double y)
    {
        var note = new PostItNote { Content = content, X = x, Y = y, Width = 200, Height = 160 };
        vm.Notes.Add(new PostItViewModel(note));
    }

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ScratchpadViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.Scale}|{vm.OffsetX}|{vm.OffsetY}|" +
               string.Join(",", vm.Notes.Select(n => $"{(n.IsSearchHit ? "*" : "")}{(n.IsHiddenBySearch ? "-" : "+")}"));
    }

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Visible(ScratchpadViewModel vm) =>
        vm.Notes.Where(n => !n.IsHiddenBySearch).Select(n => n.Content).ToArray();

    // ── Board behaviour beyond the shared contract ────────────────────────────

    [TestMethod]
    public void DisplayingSearch_HidesTheNotesThatMissed() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;
        Assert.AreEqual(3, Visible(vm).Length);

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(
            new[] { "alpha42 — call the vendor", "ring back about alpha42" }, Visible(vm),
            "a mark on a note two screens away is a mark nobody sees");
        Assert.AreEqual(2, vm.SearchMatchCount);
    });

    [TestMethod]
    public void EachSurvivingNoteIsHandedTheQuery_SoItsOwnBodyCanPaintIt() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.IsTrue(vm.Notes.Where(n => n.IsSearchHit).All(n => n.SearchMatcher is not null));
        Assert.IsTrue(vm.Notes.Where(n => !n.IsSearchHit).All(n => n.SearchMatcher is null),
            "a hidden note has nothing to paint");
    });

    [TestMethod]
    public void TheBoardPansToTheFirstHit_InReadingOrder() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreSame(vm.Notes[0], vm.ScrollToNote, "top row first, then left to right");
    });

    [TestMethod]
    public void PanningToAHit_LeavesTheUsersZoomAlone() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;
        vm.Scale = 2.5;

        await vm.SearchAsync(Query("alpha42"), display: true, default);
        vm.CenterOnWithViewport(vm.Notes[2], 800, 600);

        Assert.AreEqual(2.5, vm.Scale, "the scale is the user's — quietly refitting would lose their working view");
        Assert.AreEqual(800 / 2.0 - (10 + 100) * 2.5, vm.OffsetX, 0.001);
        Assert.AreEqual(600 / 2.0 - (400 + 80) * 2.5, vm.OffsetY, 0.001);
    });

    [TestMethod]
    public void FindNext_WalksEveryHit_AndWraps() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreSame(vm.Notes[2], vm.ScrollToNote);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreSame(vm.Notes[0], vm.ScrollToNote, "then wraps back to the first");
    });

    [TestMethod]
    public void ClearSearch_PutsEveryNoteBack() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(3, Visible(vm).Length);
        Assert.IsFalse(vm.Notes.Any(n => n.IsSearchHit));
        Assert.IsTrue(vm.Notes.All(n => n.SearchMatcher is null));
        Assert.IsNull(vm.ScrollToNote);
    });

    [TestMethod]
    public void ShowResults_NarrowsTheBoardToTheChosenNotes() => WithPage(async page =>
    {
        var vm = (ScratchpadViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == vm.Notes[2].Note.Id.ToString());

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { "ring back about alpha42" }, Visible(vm));
    });

    [TestMethod]
    public void AnEmptyBoard_SaysSo_RatherThanReportingNoMatches() => RunUnpumped(async () =>
    {
        var vm = new ScratchpadViewModel(_config, _store, RunningShell());

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "no notes");
    });
}
