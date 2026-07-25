using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// What the search tab shows around its results: the header that says what was searched and where, the
/// empty state, the column sorting, and the two actions on a selected row.
/// <para>
/// The index query itself needs a live SystemIndex, so what is asserted here is everything either side of
/// it — the readouts a user reads to know whether a search ran at all, and the guards that keep the action
/// strip from firing with nothing selected.
/// </para>
/// </summary>
[TestClass]
public class SearchSurfaceTests
{
    private static SearchViewModel Make(string query = "report", string root = @"C:\work",
                                        IShellServices? shell = null)
        => new(query, root, [], shell ?? Substitute.For<IShellServices>());

    // ── Top bar ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("search-topbar")]
    public void TheHeaderReportsWhatWasSearchedAndWhereBeforeAnyResultsArrive()
    {
        var vm = Make(query: "*.cs", root: @"D:\src");

        Assert.AreEqual("*.cs", vm.SearchQuery);
        Assert.AreEqual(@"D:\src", vm.SearchRoot);
        Assert.IsFalse(vm.IsSearching, "the spinner is off until a search is actually started");
    }

    [TestMethod]
    [CoversNode("search-topbar")]
    public void ASearchWithNoRootReadsAsTheWholeMachine()
    {
        var vm = new SearchViewModel("report", root: string.Empty, [@"C:\", @"D:\"],
                                     Substitute.For<IShellServices>());

        Assert.AreEqual("search:this-pc", vm.GetSecurityContext(),
                        "a cross-drive search is its own scope, not the same as a folder search");
    }

    // ── Results list ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("search-results-list")]
    public void AnEmptyListReadsDifferently_DependingOnWhetherASearchWasEvenAsked()
    {
        // The two look identical on screen, and the difference matters to anyone — user or model — deciding
        // whether "nothing here" means the file is absent or the search never ran.
        var never = new SearchViewModel(string.Empty, @"C:\work", [], Substitute.For<IShellServices>());
        StringAssert.Contains(never.GetContext(), "no search performed yet");

        var ran = Make(query: "report");
        Assert.AreEqual(0, ran.Results.Count);
        StringAssert.Contains(ran.GetContext(), "'report'");
        StringAssert.Contains(ran.GetContext(), "0 result(s)");
    }

    // ── Column sort ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("search-column-sort")]
    public void EveryColumnHeaderMapsToTheFieldItClaimsToSortBy()
    {
        // Rename a column in the XAML without updating this map and clicking it silently stops sorting.
        Assert.AreEqual("FileName", SearchResultSort.PropertyFor("Name"));
        Assert.AreEqual("Directory", SearchResultSort.PropertyFor("Location"));
        Assert.AreEqual("SizeBytes", SearchResultSort.PropertyFor("Size"));
        Assert.AreEqual("Modified", SearchResultSort.PropertyFor("Modified"));
        Assert.IsNull(SearchResultSort.PropertyFor("Something Else"));
    }

    [TestMethod]
    [CoversNode("search-column-sort")]
    public void AHeaderStillResolvesOnceItIsWearingItsSortArrow()
    {
        var sorted = SearchResultSort.WithArrow("Size", ascending: true);

        Assert.AreEqual("SizeBytes", SearchResultSort.PropertyFor(sorted),
                        "the arrow is part of the header text, so it has to be stripped to read the column back");
        Assert.AreEqual("Size", SearchResultSort.Strip(sorted));
        Assert.AreEqual("Size", SearchResultSort.Strip(SearchResultSort.WithArrow(sorted, ascending: false)),
                        "re-sorting must not leave two arrows stacked up");
    }

    [TestMethod]
    [CoversNode("search-column-sort")]
    public void ClickingANewColumnStartsAscending_AndClickingItAgainFlips()
    {
        Assert.IsTrue(SearchResultSort.NextAscending(isSameHeaderAsLast: false, lastWasAscending: true));
        Assert.IsTrue(SearchResultSort.NextAscending(isSameHeaderAsLast: false, lastWasAscending: false));
        Assert.IsFalse(SearchResultSort.NextAscending(isSameHeaderAsLast: true, lastWasAscending: true));
        Assert.IsTrue(SearchResultSort.NextAscending(isSameHeaderAsLast: true, lastWasAscending: false));
    }

    // ── Action strip ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("search-open-location")]
    [CoversNode("search-open-file")]
    public void BothActionsAreDisabledUntilARowIsSelected()
    {
        var vm = Make();

        Assert.IsFalse(vm.HasSelection);
        Assert.IsFalse(vm.OpenLocationCommand.CanExecute(null));
        Assert.IsFalse(vm.OpenFileCommand.CanExecute(null));

        vm.SelectedEntry = new SearchResultEntry
        {
            FileName = "notes.txt",
            FilePath = @"C:\work\notes.txt",
            Directory = @"C:\work",
        };

        Assert.IsTrue(vm.HasSelection);
        Assert.IsTrue(vm.OpenLocationCommand.CanExecute(null));
        Assert.IsTrue(vm.OpenFileCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("search-open-file")]
    public void OpeningAResultThatIsNoLongerThere_FailsQuietly()
    {
        // The index can be stale, so the row may name a file that has since gone. Launching it must not
        // take the tab down with it.
        var shell = Substitute.For<IShellServices>();
        var vm = Make(shell: shell);
        vm.SelectedEntry = new SearchResultEntry
        {
            FileName = "gone.txt",
            FilePath = Path.Combine(Path.GetTempPath(), $"nexasearch_missing_{Guid.NewGuid():N}.txt"),
            Directory = Path.GetTempPath(),
        };

        vm.OpenFileCommand.Execute(null);

        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
    }

    [TestMethod]
    [CoversNode("search-open-location")]
    public void OpeningTheLocationOpensTheContainingFolder_NotTheFile()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("FileSystem", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));

        var vm = Make(shell: shell);
        vm.SelectedEntry = new SearchResultEntry
        {
            FileName = "notes.txt",
            FilePath = @"C:\work\reports\notes.txt",
            Directory = @"C:\work\reports",
        };

        vm.OpenLocationCommand.Execute(null);

        Assert.AreEqual(@"C:\work\reports", opened.Single()["path"]);
        Assert.AreEqual("reports", opened.Single()["label"], "the tab is named for the folder, not the file");
    }
}
