using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("search-query-parser")]
public class SearchQueryParserTests
{
    // ── Wildcard word ("*fig*") — a content wildcard, seeded as a fragment ──────

    [TestMethod]
    public void FromTerms_WildcardWord_SeedsOnItsCoreAndSearchesContentAndName()
    {
        // Parsed with the glob recogniser present, exactly as the file browser does — "*config*" is a
        // content wildcard, NOT a filename glob, so it must reach the index as a re-filtered fragment.
        var request = SearchSyntax.ParseRequest("*config*", [new Nexaflow.IO.Common.GlobTermRecognizer()]);
        var term    = request.Terms.Single();
        Assert.IsTrue(term.HasWildcards);
        Assert.IsFalse(term.NameOnly, "a bare wildcard word searches contents too");

        var parsed = SearchQueryParser.FromTerms(request.Terms);
        Assert.IsNotNull(parsed);

        // The index can't run the wildcards, so it is seeded on the literal core as a prefix and re-filtered.
        StringAssert.Contains(parsed.WhereClause, "CONTAINS(System.Search.Contents,'\"config*\"')");
        StringAssert.Contains(parsed.WhereClause, "System.FileName LIKE '%config%'");
        Assert.IsFalse(parsed.WhereClause.Contains("'\"*config*\"'"),
            "a leading '*' inside CONTAINS is invalid AQS — the core must be seeded, not the raw pattern");
    }

    [TestMethod]
    public void FromTerms_WildcardWord_TooShortToSeed_IsDroppedNotBroken()
    {
        // "a*b" has no run long enough to narrow on. It must not emit a broken CONTAINS; the term is dropped
        // (the query widens) and the post-filter enforces it. Here it's the only term, so nothing to seed.
        var request = SearchSyntax.ParseRequest("a*b", [new Nexaflow.IO.Common.GlobTermRecognizer()]);
        Assert.IsNull(SearchQueryParser.FromTerms(request.Terms),
            "nothing seedable — better to report that than to search the whole corpus");
    }

    // ── Quoted single term ────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_QuotedTerm_ContainsAndLikeClause()
    {
        var result = SearchQueryParser.Parse("\"hello world\"");

        Assert.IsFalse(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "CONTAINS(System.Search.Contents,'hello world')");
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%hello world%'");
    }

    [TestMethod]
    public void Parse_QuotedTerm_RawInputPreserved()
    {
        var result = SearchQueryParser.Parse("\"foo\"");

        Assert.AreEqual("\"foo\"", result.RawInput);
    }

    // ── File glob ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_StarGlob_IsGlobTrueAndLikePattern()
    {
        var result = SearchQueryParser.Parse("*.cs");

        Assert.IsTrue(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%.cs'");
    }

    [TestMethod]
    public void Parse_QuestionMarkGlob_IsGlobTrue()
    {
        var result = SearchQueryParser.Parse("report?.pdf");

        Assert.IsTrue(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE");
    }

    [TestMethod]
    public void Parse_GlobWithSpace_FallsToPlainTerms()
    {
        // Spaces mean it's not a single glob pattern — each term is treated individually
        var result = SearchQueryParser.Parse("*.cs *.js");

        Assert.IsFalse(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%.cs'");
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%.js'");
    }

    // ── Prefix syntax ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_PlusMinus_IncludeExcludeClauses()
    {
        var result = SearchQueryParser.Parse("+foo -bar");

        Assert.IsFalse(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%foo%'");
        StringAssert.Contains(result.WhereClause, "System.FileName NOT LIKE '%bar%'");
    }

    [TestMethod]
    public void Parse_PrefixSyntax_JoinsWithAnd()
    {
        var result = SearchQueryParser.Parse("+alpha -beta");

        StringAssert.Contains(result.WhereClause, " AND ");
    }

    // ── Plain terms ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_SingleTerm_ContainsAndLike()
    {
        var result = SearchQueryParser.Parse("config");

        Assert.IsFalse(result.IsGlob);
        StringAssert.Contains(result.WhereClause, "CONTAINS(System.Search.Contents,'config')");
        StringAssert.Contains(result.WhereClause, "System.FileName LIKE '%config%'");
    }

    [TestMethod]
    public void Parse_MultipleTerms_JoinedWithAnd()
    {
        var result = SearchQueryParser.Parse("error log");

        StringAssert.Contains(result.WhereClause, " AND ");
    }

    [TestMethod]
    public void Parse_MultipleTerms_EachTermSearched()
    {
        var result = SearchQueryParser.Parse("error log");

        StringAssert.Contains(result.WhereClause, "'error'");
        StringAssert.Contains(result.WhereClause, "'log'");
    }

    // ── SQL injection / escaping ──────────────────────────────────────────────

    [TestMethod]
    public void Parse_ApostropheInTerm_IsEscaped()
    {
        var result = SearchQueryParser.Parse("o'clock");

        // Single quotes in SQL must be doubled
        StringAssert.Contains(result.WhereClause, "o''clock");
        Assert.IsFalse(result.WhereClause.Contains("o'clock"),
            "Un-escaped apostrophe would break OLE DB SQL");
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_CombinesWithAnd()
    {
        var first  = SearchQueryParser.Parse("foo");
        var second = SearchQueryParser.Parse("bar");

        var merged = SearchQueryParser.Merge(first, second);

        StringAssert.Contains(merged.WhereClause, ") AND (");
    }

    [TestMethod]
    public void Merge_RawInputCombinesBothInputs()
    {
        var first  = SearchQueryParser.Parse("foo");
        var second = SearchQueryParser.Parse("bar");

        var merged = SearchQueryParser.Merge(first, second);

        StringAssert.Contains(merged.RawInput, "foo");
        StringAssert.Contains(merged.RawInput, "bar");
    }

    [TestMethod]
    public void Merge_IsGlobAlwaysFalse()
    {
        var glob   = SearchQueryParser.Parse("*.cs");
        var plain  = SearchQueryParser.Parse("bar");

        var merged = SearchQueryParser.Merge(glob, plain);

        Assert.IsFalse(merged.IsGlob);
    }

    // ── Filesystem-walk predicate (Matches) ───────────────────────────────────
    // Mirrors each WhereClause branch for off-index locations.

    private static FileProbe Probe(string name, long size = 10, int year = 2024)
        => new(name, size, new DateTime(year, 1, 1));

    [TestMethod]
    public void Matches_StarGlob_MatchesByExtension()
    {
        var q = SearchQueryParser.Parse("*.json");

        Assert.IsTrue(q.Matches(Probe("data.json")));
        Assert.IsFalse(q.Matches(Probe("data.txt")));
    }

    [TestMethod]
    public void Matches_QuestionMarkGlob_MatchesSingleChar()
    {
        var q = SearchQueryParser.Parse("report?.pdf");

        Assert.IsTrue(q.Matches(Probe("report1.pdf")));
        Assert.IsFalse(q.Matches(Probe("report12.pdf")));
    }

    [TestMethod]
    public void Matches_QuotedTerm_MatchesFilenameSubstring()
    {
        var q = SearchQueryParser.Parse("\"budget\"");

        Assert.IsTrue(q.Matches(Probe("q4-budget.xlsx")));
        Assert.IsFalse(q.Matches(Probe("revenue.xlsx")));
    }

    [TestMethod]
    public void Matches_PlusMinus_IncludesAndExcludes()
    {
        var q = SearchQueryParser.Parse("+report -draft");

        Assert.IsTrue(q.Matches(Probe("report-final.doc")));
        Assert.IsFalse(q.Matches(Probe("report-draft.doc")));
        Assert.IsFalse(q.Matches(Probe("summary.doc")));
    }

    [TestMethod]
    public void Matches_PlainTerms_RequireEveryTermInName()
    {
        var q = SearchQueryParser.Parse("error log");

        Assert.IsTrue(q.Matches(Probe("error-log.txt")));
        Assert.IsFalse(q.Matches(Probe("error.txt")));
    }

    [TestMethod]
    public void Matches_Merge_AndsBothPredicates()
    {
        var merged = SearchQueryParser.Merge(
            SearchQueryParser.Parse("*.log"),
            SearchQueryParser.Parse("app"));

        Assert.IsTrue(merged.Matches(Probe("app.log")));
        Assert.IsFalse(merged.Matches(Probe("other.log")));
        Assert.IsFalse(merged.Matches(Probe("app.txt")));
    }

    // Size and date constraints are no longer this overload's business — they are parsed by Windows into
    // a SearchCondition and applied by both projections. Covered in AqsTermRecognizerTests, which asserts
    // the folder walk actually evaluates them rather than waving them through.
}
