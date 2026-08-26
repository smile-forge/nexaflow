using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// Tests the conformance suite itself. A silent-failure detector is worthless if it can't detect the
/// silent failure, so each guard runs the shared assertions against a page that is deliberately broken in
/// exactly the way the suite exists to catch, and asserts the suite rejects it.
/// </summary>
[TestClass]
[NoCoverage("guards the ISearchable conformance suite, not a product node")]
public class SearchableConformanceGuardTests
{
    private const string Content = "line with alpha42 in it";

    /// <summary>Claims regex support but runs <c>Contains</c> regardless — the exact bug being hunted.</summary>
    private sealed class PretendsToSupportRegex : ISearchable
    {
        public string SearchTargetDescription => "a fake page for testing the conformance suite";

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        {
            var hit = Content.Contains(request.Text, StringComparison.OrdinalIgnoreCase);   // ignores IsRegex
            return Task.FromResult(hit
                ? SearchOutcome.Found([new SearchHit("0", "line 1", Content)])
                : SearchOutcome.None());
        }
    }

    /// <summary>Refuses regex outright — the opt-out the contract deliberately doesn't offer.</summary>
    private sealed class RefusesRegex : ISearchable
    {
        public string SearchTargetDescription => "a fake page for testing the conformance suite";

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
            => Task.FromResult(request.IsRegex
                ? SearchOutcome.Unsupported("this backend has no regex operator")
                : SearchOutcome.None());
    }

    /// <summary>Honours neither <see cref="SearchRequest.MatchCase"/> nor the display flag.</summary>
    private sealed class IgnoresMatchCase : ISearchable
    {
        public string SearchTargetDescription => "a fake page for testing the conformance suite";

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        {
            var hit = request.IsRegex
                ? request.Matches(Content)
                : Content.Contains(request.Text, StringComparison.OrdinalIgnoreCase);   // always insensitive
            return Task.FromResult(hit
                ? SearchOutcome.Found([new SearchHit("0", "line 1", Content)])
                : SearchOutcome.None());
        }
    }

    /// <summary>
    /// Says yes to any id it is handed. The failure this hides is quiet in exactly the way the others are:
    /// the agent reports "I've filtered your view to the three that matter" and the view still shows
    /// everything — or, if the page filtered first, shows nothing at all.
    /// </summary>
    private sealed class NarrowsToAnything : ISearchable
    {
        public string SearchTargetDescription => "a fake page for testing the conformance suite";

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
            => Task.FromResult(request.Matches(Content)
                ? SearchOutcome.Found([new SearchHit("0", "line 1", Content)])
                : SearchOutcome.None());

        public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
            => Task.FromResult(true);   // never checks whether it actually holds these ids
    }

    // Deliberately NOT [TestClass]: this derives the conformance suite so the guard can *invoke* its
    // assertions against a deliberately-broken page and check they fail. Marking it would have MSTest
    // discover and run it as a real suite, which is the opposite of the point.
#pragma warning disable MSTEST0030
    private sealed class Probe(ISearchable page) : SearchableContentConformanceTests
#pragma warning restore MSTEST0030
    {
        protected override Task<ISearchable> CreateAsync() => Task.FromResult(page);
        protected override string LiteralTermInContent => "alpha42";
        protected override string RegexOnlyPattern     => @"alpha\d+";
        protected override string Snapshot(ISearchable p) => string.Empty;
    }

    [TestMethod]
    public void Suite_CatchesAPageThatOnlyPretendsToRunRegex()
        => Assert.ThrowsExactly<AssertFailedException>(
            () => new Probe(new PretendsToSupportRegex()).RegexSearch_ActuallyCompilesThePattern());

    [TestMethod]
    public void Suite_CatchesAPageThatRefusesRegexInsteadOfWidening()
        => Assert.ThrowsExactly<AssertFailedException>(
            () => new Probe(new RefusesRegex()).RegexWithLiteralText_IsNeverRefused());

    [TestMethod]
    public void Suite_CatchesAPageThatIgnoresMatchCase()
        => Assert.ThrowsExactly<AssertFailedException>(
            () => new Probe(new IgnoresMatchCase()).MatchCase_IsHonoured());

    [TestMethod]
    public void Suite_CatchesAPageThatClaimsToNarrowToIdsItDoesNotHold()
        => Assert.ThrowsExactly<AssertFailedException>(
            () => new Probe(new NarrowsToAnything()).ShowResults_WithAnIdThisPageNeverGave_Declines());

    [TestMethod]
    public void Suite_PassesACorrectImplementation()
    {
        // The counterpart: the same assertions must not fire on a page that behaves.
        var probe = new Probe(new IgnoresMatchCase());
        probe.RegexSearch_ActuallyCompilesThePattern();   // this fake does honour IsRegex
        probe.LiteralSearch_FindsSeededContent();
        probe.ShowResults_WithAnIdThisPageNeverGave_Declines();   // and it inherits the interface default
    }
}
