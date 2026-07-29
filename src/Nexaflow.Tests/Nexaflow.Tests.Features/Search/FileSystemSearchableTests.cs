using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The file browser as a searchable page. Its "?" doesn't filter what's on screen — it searches the folder
/// tree the browser is showing — which is precisely why <see cref="ISearchable"/> is not a
/// filter-the-visible-list contract. Sharing it is what gives "?" a single owner.
/// </summary>
[TestClass]
[CoversNode("search-browser-route")]
public sealed class FileSystemSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    protected override string Snapshot(ISearchable page) => ((FileSystemViewModel)page).CurrentPath;

    /// <summary>
    /// Stands in for the Windows Search feature across the assembly seam. Matches its fixed corpus with
    /// <see cref="SearchRequest.Matches"/> — the same call the real engine post-filters with — so the
    /// inherited conformance tests genuinely exercise regex all the way through the browser.
    /// </summary>
    public sealed class FakeCorpus : IFileCorpusSearch
    {
        // No recorded state: the VM builds this through Activator, so a test can't hold the instance, and
        // statics race under method-level parallelism. What reached the engine is asserted from results.
        internal static readonly string[] Files =
            ["alpha42.cs", "beta.cs", "alpha42.txt", "notes.md"];

        public Task<IReadOnlyList<SearchHit>> SearchAsync(
            SearchRequest request, string root, IReadOnlyList<string> drives, int max, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SearchHit>>(
                Files.Where(request.Matches)
                     .Take(max)
                     .Select(f => new SearchHit($@"C:\code\{f}", f, "code"))
                     .ToList());
    }

    private static IShellServices Shell(bool withCorpus = false)
    {
        var shell = Substitute.For<IShellServices>();
        shell.DiscoverImplementations<IFileAction>().Returns([]);
        shell.DiscoverImplementations<IFolderAction>().Returns([]);
        shell.DiscoverImplementations<IFileCreateAction>().Returns([]);
        shell.DiscoverImplementations<IFolderViewlet>().Returns([]);
        shell.DiscoverImplementations<IFileCorpusSearch>()
             .Returns(withCorpus ? [typeof(FakeCorpus)] : []);
        return shell;
    }

    private static FileSystemViewModel Vm(IShellServices? shell = null, string? root = null)
        => new(root ?? Path.GetTempPath(), shell ?? Shell(), Substitute.For<IAIService>(),
               new Dictionary<Type, IFeatureConfig>());

    protected override Task<ISearchable> CreateAsync()
        => Task.FromResult<ISearchable>(Vm(Shell(withCorpus: true)));

    /// <summary>
    /// Constructing the browser starts a folder load that streams entries over a bounded channel and keeps
    /// running after the constructor returns. The pump no longer dies when that reader outlives the block
    /// (see <c>AsyncPumpTests</c>), but its continuations would then be detached to the thread pool while
    /// still mutating <c>Entries</c> — so this page is exercised without a pump rather than relying on an
    /// off-thread collection write being harmless.
    /// </summary>
    protected override void WithPage(Func<ISearchable, Task> body) => WithPageUnpumped(body);

    // ── Browser-specific behaviour beyond the shared contract ─────────────────

    [TestMethod]
    public void ScoreQuery_PrefersGlobsOverProse()
    {
        var vm = Vm();

        Assert.IsTrue(vm.ScoreQuery("*.cs") > vm.ScoreQuery("can you help me find the config file please"),
            "glob detection is why the browser overrides the term-count default");
    }

    [TestMethod]
    public void ScoreQuery_DampsABarePath_SoNavigationWins()
    {
        var vm = Vm();

        // "C:\projects\src" is a '>' navigation, not a search — the two handlers must not fight over it.
        Assert.IsTrue(vm.ScoreQuery(@"C:\projects\src") < vm.ScoreQuery("*.cs"));
    }

    [TestMethod]
    public void DisplayingSearch_HandsOffWithoutNamingWhoShowsIt() => RunUnpumped(async () =>
    {
        var shell = Shell();
        var root  = Path.GetTempPath();
        shell.HandleObject(Arg.Any<object>()).Returns(true);

        await Vm(shell, root).SearchAsync(new SearchRequest("*.cs"), display: true, default);

        // The browser never names the Search feature — it raises the request and something claims it.
        shell.Received(1).HandleObject(Arg.Is<FileSearchRequest>(r => r.Query.Text == "*.cs" && r.Root == root));
        shell.DidNotReceive().OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>());
    });

    [TestMethod]
    public void DisplayingSearch_OpensTheSearchPage_EvenWithNoIndexBehindIt() => RunUnpumped(async () =>
    {
        // The browser hands the query over and stops thinking about it. It does not pre-check whether the
        // search will succeed, so it needs no banner of its own — an empty result, an unreachable indexer
        // and the offer of a folder scan are all the Search page's business.
        var shell = Shell(withCorpus: false);          // nothing here can run a search
        shell.HandleObject(Arg.Any<object>()).Returns(true);

        await Vm(shell, Path.GetTempPath()).SearchAsync(new SearchRequest("*.cs"), display: true, default);

        shell.Received(1).HandleObject(Arg.Any<FileSearchRequest>());
    });

    [TestMethod]
    public void DisplayingSearch_DoesNotConsultTheIndexFirst() => RunUnpumped(async () =>
    {
        // Querying before handing off would make the browser decide whether the Search page is worth
        // opening — and it would decide wrongly, because "the index found nothing" is exactly the case
        // that page exists to offer a way out of.
        var shell = Shell(withCorpus: true);
        shell.HandleObject(Arg.Any<object>()).Returns(true);

        var outcome = await Vm(shell, Path.GetTempPath())
            .SearchAsync(new SearchRequest("zqxwv-no-such-token-8813"), display: true, default);

        Assert.IsFalse(outcome.Failed, "a display search reports the handoff, not the search's outcome");
        shell.Received(1).HandleObject(Arg.Any<FileSearchRequest>());
    });

    [TestMethod]
    public void DisplayingSearch_WithNothingToShowResults_SaysSo() => RunUnpumped(async () =>
    {
        var shell = Shell();
        shell.HandleObject(Arg.Any<object>()).Returns(false);   // no feature claimed it

        var outcome = await Vm(shell, Path.GetTempPath()).SearchAsync(new SearchRequest("*.cs"), display: true, default);

        Assert.IsTrue(outcome.Failed, "a handoff nobody claims must not look like a successful silent search");
    });

    [TestMethod]
    public void AgentSearch_QueriesTheIndexWithoutOpeningATab() => RunUnpumped(async () =>
    {
        var shell = Shell(withCorpus: true);
        var vm    = Vm(shell, Path.GetTempPath());

        var outcome = await vm.SearchAsync(new SearchRequest("alpha42"), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount, "the agent gets real hits, not a tab it can't read");
        shell.DidNotReceive().OpenTab("Search", Arg.Any<Dictionary<string, string>>());
    });

    [TestMethod]
    public void AgentRegexSearch_ReachesTheEngineAsARegex() => RunUnpumped(async () =>
    {
        var vm      = Vm(Shell(withCorpus: true), Path.GetTempPath());
        var pattern = @"alpha\d+\.cs";

        // The browser must not flatten the pattern to text on the way out. The two calls differing is the
        // proof the regex flag survived the trip to the engine.
        var asRegex   = await vm.SearchAsync(new SearchRequest(pattern, IsRegex: true), display: false, default);
        var asLiteral = await vm.SearchAsync(new SearchRequest(pattern), display: false, default);

        Assert.AreEqual(1, asRegex.MatchCount, "only alpha42.cs matches — alpha42.txt must not");
        Assert.AreEqual(0, asLiteral.MatchCount, "no file is literally named that");
    });

    [TestMethod]
    public void RegexThatMatchesNothing_SaysSoWithoutClaimingTheFolderIsEmpty() => RunUnpumped(async () =>
    {
        var vm = Vm(Shell(withCorpus: true), Path.GetTempPath());

        var outcome = await vm.SearchAsync(
            new SearchRequest(@"TODO\s*:\s*refactor", IsRegex: true), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed, "the search ran; it just matched nothing");
        StringAssert.Contains(outcome.Message!, "Nothing matched");
    });

    [TestMethod]
    public void RegexResults_SplitProvenRowsFromUncheckedOnes() => RunUnpumped(async () =>
    {
        var vm = Vm(Shell(withCorpus: true), Path.GetTempPath());

        // The browser can't run the content verifier (it lives with the search feature), so the agent must
        // be told which rows are proven rather than handed a set that mixes proven and possible silently.
        var outcome = await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount);
        StringAssert.Contains(outcome.Message!, "confirmed by file name");
    });

    [TestMethod]
    public void AgentSearch_WithNoIndexAvailable_SaysSo() => RunUnpumped(async () =>
    {
        // No IFileCorpusSearch implementation discovered — an honest refusal beats "no matches".
        var outcome = await Vm(Shell()).SearchAsync(new SearchRequest("*.cs"), display: false, default);

        Assert.IsTrue(outcome.Failed);
        StringAssert.Contains(outcome.Message!, "index");
    });
}
