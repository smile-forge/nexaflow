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
/// The project list answering <c>?</c> over three real project folders.
/// <para>
/// What is worth pinning beyond the shared contract: the <em>description</em> is searched, not just the
/// name. A project's folder is something someone typed in a hurry; what it is about is written in the
/// description, and "the one about invoices" is how anyone actually looks for it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("projects-search")]
public class ProjectsSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    private const string ByName = "alpha42-importer";   // matches on its name / folder
    private const string ByDesc = "invoices";           // matches on its description only
    private const string Quiet  = "ledger";

    // Per-instance: MSTest runs test methods in parallel on separate instances.
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-projsearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ActiveDir);
        Directory.CreateDirectory(ShelfDir);

        var cfg = Config();
        var ops = new ProjectOperations(cfg, ActiveDir);
        Seed(ops, ByName, "alpha42 importer", "moves things between systems.");
        Seed(ops, ByDesc, "invoices",         "the alpha42 pipeline lives here.");
        Seed(ops, Quiet,  "ledger",           "nothing to see.");
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string ActiveDir => Path.Combine(_root, "active");
    private string ShelfDir  => Path.Combine(_root, "shelf");

    private ProjectsConfig Config() => new()
    {
        EnableProjects   = true,
        ProjectDirectory = ActiveDir,
        ShelfDirectory   = ShelfDir,
        ArchiveDirectory = Path.Combine(_root, "archive"),
    };

    private void Seed(ProjectOperations ops, string folder, string name, string description)
    {
        Directory.CreateDirectory(Path.Combine(ActiveDir, folder));
        ops.ModifyProjectHeader(folder, name, description);
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

    private ProjectsViewModel Build() => new(Config(), RunningShell());

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Build());

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ProjectsViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.SelectedProject?.FolderName}|" + string.Join(",", Visible(vm));
    }

    private static string[] Visible(ProjectsViewModel vm) =>
        System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Projects)
              .Cast<ProjectSummaryItem>().Select(p => p.FolderName).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Project-list behaviour beyond the shared contract ─────────────────────

    [TestMethod]
    public void AProjectMatchesOnItsName_OrOnItsDescription() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { ByName, ByDesc }, Ids(outcome),
            "'the one about the alpha42 pipeline' is how people look for a project, and its name says nothing");
    });

    [TestMethod]
    public void HitIdsAreFolderNames_SoTheyRoundTripIntoReadProject() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("invoices"), display: false, default);

        Assert.AreEqual(ByDesc, outcome.Hits.Single().Id);
    });

    [TestMethod]
    public void DisplayingSearch_NarrowsTheList_AndKeepsTheSummaryPaneOnSomethingVisible() => WithPage(async page =>
    {
        var vm = (ProjectsViewModel)page;
        Assert.AreEqual(3, Visible(vm).Length);

        await vm.SearchAsync(Query("invoices"), display: true, default);

        CollectionAssert.AreEqual(new[] { ByDesc }, Visible(vm));
        Assert.AreEqual(ByDesc, vm.SelectedProject?.FolderName,
            "a selection filtered away leaves the summary pane describing a project the list no longer shows");
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void SwitchingBucket_DropsTheSearch_RatherThanDescribingProjectsThatAreGone() => WithPage(async page =>
    {
        var vm = (ProjectsViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.SelectedBucket = ProjectBucket.Shelf;

        Assert.IsFalse(vm.IsSearchActive, "the page shows one bucket at a time");
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
    });

    [TestMethod]
    public void ClearSearch_PutsEveryProjectBack() => WithPage(async page =>
    {
        var vm = (ProjectsViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(3, Visible(vm).Length);
    });

    [TestMethod]
    public void ShowResults_NarrowsToTheChosenProjects() => WithPage(async page =>
    {
        var vm = (ProjectsViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == ByDesc);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { ByDesc }, Visible(vm));
    });

    [TestMethod]
    public void ShowResults_WithFoldersThisBucketDoesNotHold_Declines() => WithPage(async page =>
    {
        var vm = (ProjectsViewModel)page;

        var narrowed = await vm.ShowResultsAsync([new SearchHit("not-here", "not-here")], default);

        Assert.IsFalse(narrowed, "the agent needs to know it must describe the matches instead");
        Assert.AreEqual(3, Visible(vm).Length);
    });

    [TestMethod]
    public void ADisabledFeature_SaysSo_RatherThanReportingNoProjects() => RunUnpumped(async () =>
    {
        var cfg = Config();
        cfg.EnableProjects = false;
        var vm = new ProjectsViewModel(cfg, RunningShell());

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "disabled");
    });
}
