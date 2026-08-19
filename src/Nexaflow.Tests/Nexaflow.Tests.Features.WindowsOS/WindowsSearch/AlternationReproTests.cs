using Nexaflow.Features.Common.Search;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// Reported: "?/ma(ths|gic)/ returns 2652 confirmed" — every file containing "ma" marked proven, instead
/// of the handful that actually match. The seed is deliberately broad (alternation has no mandatory
/// literal past "ma"), so the post-filter is the ONLY thing standing between the index's answer and the
/// user's. If it doesn't run, or runs vacuously, every row keeps its default Verified state.
/// </summary>
[TestClass]
[CoversNode("page-search-regex")]
public class AlternationReproTests
{
    private static readonly ISearchTermRecognizer[] WithGlobs = [new GlobTermRecognizer()];

    [TestMethod]
    public void AlternationPattern_ParsesAsOneRegexTerm()
    {
        var request = SearchSyntax.ParseRequest("/ma(ths|gic)/", WithGlobs);

        Assert.AreEqual(1, request.Terms.Count);
        Assert.AreEqual(SearchTermKind.Regex, request.Terms[0].Kind);
        Assert.AreEqual("ma(ths|gic)", request.Terms[0].Value);
    }

    [TestMethod]
    public void AlternationPattern_SeedsBroadly_WhichIsWhyTheFilterMatters()
    {
        // "ma" is the only mandatory literal — correct, and exactly why the post-filter carries the query.
        Assert.AreEqual("ma", AqsRegexTranslator.ToIndexQuery("ma(ths|gic)"));
    }

    [TestMethod]
    public void UnrelatedFileName_IsNotConfirmedByName()
    {
        var request = SearchSyntax.ParseRequest("/ma(ths|gic)/", WithGlobs);

        Assert.IsFalse(request.MatchesName("manual.pdf"),
            "'manual.pdf' contains 'ma' but neither 'maths' nor 'magic'");

        var state = SearchVerifier.ClassifyByName(
            new SearchHit(@"C:\d\manual.pdf", "manual.pdf"), request);

        Assert.AreNotEqual(SearchHitState.Verified, state,
            "a row the index returned for the seed must not be reported as proven");
        Assert.AreEqual(SearchHitState.Candidate, state);
    }

    [TestMethod]
    public void MatchingFileName_IsConfirmed()
    {
        var request = SearchSyntax.ParseRequest("/ma(ths|gic)/", WithGlobs);

        Assert.IsTrue(request.MatchesName("maths homework.docx"));
        Assert.IsTrue(request.MatchesName("the magic flute.pdf"));
    }

    [TestMethod]
    public void EmptyTermList_MustNotConfirmEverything()
    {
        // Terms.All(...) is vacuously TRUE on an empty list, which would mark every row proven. Guard it.
        var request = SearchSyntax.ParseRequest("   ", WithGlobs);

        Assert.AreEqual(0, request.Terms.Count);
        Assert.IsFalse(request.MatchesName("anything at all"),
            "a query with no terms matches nothing — it must never match everything");
    }
}
