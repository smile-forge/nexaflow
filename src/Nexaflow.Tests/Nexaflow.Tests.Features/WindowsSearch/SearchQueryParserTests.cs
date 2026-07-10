using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("search-query-parser")]
public class SearchQueryParserTests
{
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

    // ── Size filters ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_SizeFilterMb_ConvertsToBytes()
    {
        var result = SearchQueryParser.Parse("size:>1mb");

        StringAssert.Contains(result.WhereClause, $"System.Size > {1L * 1024 * 1024}");
    }

    [TestMethod]
    public void Parse_LargerFilter_UsesGreaterThan()
    {
        var result = SearchQueryParser.Parse("larger:100kb");

        StringAssert.Contains(result.WhereClause, $"System.Size > {100L * 1024}");
    }

    [TestMethod]
    public void Parse_SmallerFilter_UsesLessThan()
    {
        var result = SearchQueryParser.Parse("smaller:500kb");

        StringAssert.Contains(result.WhereClause, $"System.Size < {500L * 1024}");
    }

    // ── Date filters ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_DateYear_ExpandsToIsoDate()
    {
        var result = SearchQueryParser.Parse("date:2023");

        StringAssert.Contains(result.WhereClause, "System.DateModified");
        StringAssert.Contains(result.WhereClause, "2023-01-01");
    }

    [TestMethod]
    public void Parse_DateYearMonth_ExpandsToFirstOfMonth()
    {
        var result = SearchQueryParser.Parse("modified:2023-06");

        StringAssert.Contains(result.WhereClause, "2023-06-01");
    }

    [TestMethod]
    public void Parse_BeforeKeyword_UsesLessThan()
    {
        var result = SearchQueryParser.Parse("before:2024-01-01");

        StringAssert.Contains(result.WhereClause, "System.DateModified < '2024-01-01'");
    }

    [TestMethod]
    public void Parse_AfterKeyword_UsesGreaterThan()
    {
        var result = SearchQueryParser.Parse("after:2024-01-01");

        StringAssert.Contains(result.WhereClause, "System.DateModified > '2024-01-01'");
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
    public void Matches_SizeFilter_ComparesBytes()
    {
        var q = SearchQueryParser.Parse("larger:100kb");

        Assert.IsTrue(q.Matches(Probe("big.bin", size: 200 * 1024)));
        Assert.IsFalse(q.Matches(Probe("small.bin", size: 50 * 1024)));
    }

    [TestMethod]
    public void Matches_DateFilter_ComparesModified()
    {
        var q = SearchQueryParser.Parse("after:2024-01-01");

        Assert.IsTrue(q.Matches(Probe("recent.txt", year: 2025)));
        Assert.IsFalse(q.Matches(Probe("old.txt", year: 2023)));
    }

    [TestMethod]
    public void Matches_Merge_AndsBothPredicates()
    {
        var merged = SearchQueryParser.Merge(
            SearchQueryParser.Parse("*.log"),
            SearchQueryParser.Parse("larger:1kb"));

        Assert.IsTrue(merged.Matches(Probe("app.log", size: 4096)));
        Assert.IsFalse(merged.Matches(Probe("app.log", size: 100)));
        Assert.IsFalse(merged.Matches(Probe("app.txt", size: 4096)));
    }
}
