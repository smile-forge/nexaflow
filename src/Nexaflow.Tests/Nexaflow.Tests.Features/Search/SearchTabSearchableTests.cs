using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The Search tab as a searchable page — refinement now goes through <see cref="ISearchable"/> instead of a
/// dedicated handler. Only the content-free conformance applies: its backend is the live Windows Search
/// index, which a unit test can't seed. That is exactly why the refusal path matters here — AQS has no
/// regex operator, so this is the implementor that must decline rather than pretend.
/// </summary>
[TestClass]
[CoversNode("search-refine")]
public sealed class SearchTabSearchableTests : SearchableConformanceTests
{
    private static SearchViewModel Vm(string query = "report", string root = @"C:\data") =>
        new(query, root, [], Substitute.For<IShellServices>());

    protected override Task<ISearchable> CreateAsync() => Task.FromResult<ISearchable>(Vm());

    [TestMethod]
    public void UnscopedTab_RefusesRatherThanReturningNothing() => AsyncPump.Run(async () =>
    {
        // No root and no drives — "nothing to search" is a failure, not an empty result set.
        var vm      = new SearchViewModel("report", "", [], Substitute.For<IShellServices>());
        var outcome = await vm.SearchAsync(new SearchRequest("*.pdf"), display: false, default);

        Assert.IsTrue(outcome.Failed);
    });

    [TestMethod]
    public void ScoreQuery_PrefersGlobsOverProse()
    {
        var vm = Vm();

        Assert.IsTrue(vm.ScoreQuery("*.pdf") > vm.ScoreQuery("why is this folder so large, could you check"),
            "the glob scorer is the reason this page overrides the term-count default");
    }

    [TestMethod]
    public void ScoreQuery_StillClaimsARefinementWhileASearchIsRunning()
    {
        var vm = Vm();
        vm.IsSearching = true;

        // Previously zero, on the reasoning that bare input shouldn't jump an in-flight query. That reads
        // backwards during a folder scan, which runs for minutes while the user watches rows arrive and
        // decides to narrow them — the moment they are MOST likely to be refining. Superseding a running
        // query is the ViewModel's job; scoring only decides who the input belongs to.
        Assert.IsTrue(vm.ScoreQuery("*.pdf") > 0.5f);
    }
}
