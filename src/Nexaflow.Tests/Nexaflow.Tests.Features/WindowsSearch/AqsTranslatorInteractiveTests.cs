using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// Calls the REAL Windows Search COM API. Everything else about AQS is tested against a fake; this is the
/// one place that answers "are our interop declarations actually right, and does Windows agree with what we
/// assume it returns?" — which no amount of mocking can.
/// <para>
/// <b>Why its own category.</b> These need the Windows Search service running, so they never run in CI
/// (see the filter in .github/workflows/ci.yml). They are safe on any developer machine: read-only, no
/// index modification, no fixture files, and nothing asserted about which files exist.
/// </para>
/// <para>
/// <b>Why this matters more than a normal test.</b> A COM interface is a vtable built from declaration
/// order. A mistake there doesn't throw — it calls the wrong function pointer and takes the process down.
/// So the assertions below are chosen to fail loudly on a wrong slot rather than to check happy paths:
/// crossing three interfaces and reading a string back proves the layout end to end.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Interactive")]
[CoversNode("search-aqs")]
public class AqsTranslatorInteractiveTests
{
    private AqsTranslator _aqs = null!;

    [TestInitialize]
    public void Setup()
    {
        _aqs = new AqsTranslator();

        if (!_aqs.IsAvailable)
            Assert.Inconclusive("Windows Search is not running on this machine — nothing to verify against.");
    }

    [TestCleanup]
    public void Cleanup() => _aqs.Dispose();

    // ── The vtable itself ─────────────────────────────────────────────────────

    [TestMethod]
    public void TheInteropReachesTheParserAtAll()
    {
        // Getting here means CoCreate → ISearchManager.GetCatalog → ISearchCatalogManager.GetQueryHelper
        // → put_QuerySyntax → put_QueryWhereRestrictions all landed on the right slots. Those are the four
        // methods we declare positions for; if any were off, Setup would have crashed or returned false.
        Assert.IsTrue(_aqs.IsAvailable);
    }

    [TestMethod]
    public void GeneratesAWhereClauseForAKnownProperty()
    {
        var where = _aqs.ToWhereClause("kind:document");

        Assert.IsNotNull(where, "Windows should parse a property every Explorer search box accepts");
        StringAssert.Contains(where, "System.Kind",
            "the clause must name the property, or we are sending the index something else entirely");
    }

    // ── The assumptions the rest of the code is built on ──────────────────────

    [TestMethod]
    public void AnUnknownPropertyIsNotMistakenForAConstraint()
    {
        // The whole hybrid rests on this. A structured term is trusted by the post-filter as already
        // enforced, so a token wrongly called a property becomes a constraint nobody ever applies —
        // silently widening the search instead of narrowing it.
        Assert.IsFalse(_aqs.Recognises("notarealproperty:zzz"));
        Assert.IsFalse(_aqs.Recognises("http://example.com"));
    }

    [TestMethod]
    public void PlainTextIsNotMistakenForAConstraint()
    {
        // Free text must fall through to our own term handling, not be swallowed as AQS.
        Assert.IsFalse(_aqs.Recognises("ocr"));
        Assert.IsFalse(_aqs.Recognises("the lost dog"));
    }

    [TestMethod]
    public void TheCommonPropertyFamiliesAllTranslate()
    {
        // These are the constraints worth having — the ones we would otherwise have hand-rolled regexes
        // for. If Windows stops recognising one, the feature quietly loses it, so name them explicitly.
        foreach (var token in new[] { "kind:document", "size:>1mb", "ext:.txt", "modified:lastweek" })
            Assert.IsNotNull(_aqs.ToWhereClause(token), $"'{token}' should translate");
    }

    [TestMethod]
    public void TheClauseIsARestrictionNotAWholeStatement()
    {
        // SearchQueryParser splices this into a bigger WHERE with AND. A leaked SELECT would produce SQL
        // that either fails or, worse, silently means something else.
        var where = _aqs.ToWhereClause("kind:document")!;

        Assert.IsFalse(where.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(where.Contains("FROM SystemIndex", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(where.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
    }

    // ── End to end, through the real parser ───────────────────────────────────

    [TestMethod]
    public void AMixedQueryPutsTheConstraintAndTheTextInOneWhere()
    {
        var terms  = SearchSyntax.ParseTerms("kind:document ocr", [new AqsTermRecognizer(_aqs)]);
        var parsed = SearchQueryParser.FromTerms(terms, _aqs);

        Assert.IsNotNull(parsed);
        StringAssert.Contains(parsed.WhereClause, "System.Kind");
        StringAssert.Contains(parsed.WhereClause, "ocr");
        StringAssert.Contains(parsed.WhereClause, " AND ");
    }

    [TestMethod]
    public void TheGeneratedSqlIsAcceptedByTheIndex()
    {
        // The real proof: a clause Windows wrote for us, run back against Windows. A syntactically valid
        // string that the provider rejects would still be a bug, and only executing it can tell.
        var terms  = SearchSyntax.ParseTerms("kind:document", [new AqsTermRecognizer(_aqs)]);
        var parsed = SearchQueryParser.FromTerms(terms, _aqs);
        Assert.IsNotNull(parsed);

        var results = WindowsSearchService.SearchAsync(parsed, KnownFolder(), CancellationToken.None)
                                          .GetAwaiter().GetResult();

        // Count is whatever this machine has indexed — asserting on it would make the test machine-specific.
        // That it returned instead of throwing is the assertion.
        Assert.IsNotNull(results);
    }

    private static string KnownFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
