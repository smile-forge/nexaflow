using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The project detail tab answering <c>?</c> over a real project's backlog.
/// <para>
/// What is worth pinning beyond the shared contract: "?" searches the <em>backlog</em> — title and detail —
/// and a search run from the Project Details tab switches to it. Answering a question about a list the
/// user cannot see is worse than not answering it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("projects-detail-search")]
public class ProjectDetailSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private const string Folder     = "widgets";
    private const string ByTitle    = "Wire the alpha42 widget";
    private const string ByDetail   = "Ship it";           // carries the term in its detail only
    private const string Quiet      = "Write the readme";

    // Per-instance: MSTest runs test methods in parallel on separate instances.
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-detailsearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ActiveDir);
        Directory.CreateDirectory(Path.Combine(ActiveDir, Folder));

        var ops = Ops();
        // No description on purpose: the tab then opens on Project Details, which is what lets the
        // "searching switches to the Backlog tab" rule be asserted at all.
        ops.ModifyProjectHeader(Folder, "widgets", string.Empty);
        ops.AddToDo(Folder, ByTitle,  "plain detail.");
        ops.AddToDo(Folder, ByDetail, "blocked until alpha42 lands.");
        ops.AddToDo(Folder, Quiet,    "nothing to see.");
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string ActiveDir => Path.Combine(_root, "active");

    private ProjectsConfig Config() => new()
    {
        EnableProjects   = true,
        ProjectDirectory = ActiveDir,
        ShelfDirectory   = Path.Combine(_root, "shelf"),
        ArchiveDirectory = Path.Combine(_root, "archive"),
    };

    private ProjectOperations Ops() => new(Config(), ActiveDir);

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

    private ProjectDetailViewModel Build() =>
        new(Ops(), Config(), Folder, RunningShell());

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProjectDetailViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.SelectedTabIndex}|{vm.SelectedItem?.Title}|" + string.Join(",", Visible(vm));
    }

    private static string[] Visible(ProjectDetailViewModel vm) =>
        System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Backlog)
              .Cast<BacklogItemViewModel>().Select(b => b.Title).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Titles(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Label).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Backlog-search behaviour beyond the shared contract ───────────────────

    [TestMethod]
    public void AnItemMatchesOnItsTitle_OrOnItsDetail() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { ByDetail, ByTitle }, Titles(outcome),
            "the detail is where the reason for an item lives — matching titles alone would miss it");
    });

    [TestMethod]
    public void HitIdsAreItemIds_SoTheyRoundTripIntoReadBacklogItem() => WithPage(async page =>
    {
        var vm = (ProjectDetailViewModel)page;
        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.IsTrue(outcome.Hits.All(h => Guid.TryParse(h.Id, out _)));
        Assert.IsTrue(outcome.Hits.All(h => vm.Backlog.Any(b => b.Id.ToString() == h.Id)));
    });

    [TestMethod]
    public void SearchingFromTheDetailsTab_SwitchesToTheBacklog() => WithPage(async page =>
    {
        var vm = (ProjectDetailViewModel)page;
        Assert.AreEqual(0, vm.SelectedTabIndex, "this project has no description, so it opens on Details");

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreEqual(1, vm.SelectedTabIndex,
            "answering about a list the user cannot see would be worse than not answering");
        CollectionAssert.AreEquivalent(new[] { ByTitle, ByDetail }, Visible(vm));
    });

    [TestMethod]
    public void DisplayingSearch_KeepsTheEditorOnSomethingStillOnTheBoard() => WithPage(async page =>
    {
        var vm = (ProjectDetailViewModel)page;
        vm.SelectedItem = vm.Backlog.Single(b => b.Title == Quiet);

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        Assert.AreNotEqual(Quiet, vm.SelectedItem?.Title,
            "an item filtered away leaves the right pane editing something the list no longer shows");
        Assert.IsTrue(Visible(vm).Contains(vm.SelectedItem?.Title));
    });

    [TestMethod]
    public void ClearSearch_PutsEveryItemBack() => WithPage(async page =>
    {
        var vm = (ProjectDetailViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(3, Visible(vm).Length);
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenItems() => WithPage(async page =>
    {
        var vm = (ProjectDetailViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Label == ByDetail);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { ByDetail }, Visible(vm));
    });

    [TestMethod]
    public void AnEmptyBacklog_SaysSo_RatherThanReportingNoMatches() => RunUnpumped(async () =>
    {
        var empty = "blank";
        Directory.CreateDirectory(Path.Combine(ActiveDir, empty));
        Ops().ModifyProjectHeader(empty, "blank", string.Empty);
        var vm = new ProjectDetailViewModel(Ops(), Config(), empty, RunningShell());

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "no backlog items");
    });
}
