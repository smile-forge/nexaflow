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
    public void ParsesAKnownPropertyIntoATree()
    {
        var condition = _aqs.Parse("kind:document");

        Assert.IsNotNull(condition, "Windows should parse a property every Explorer search box accepts");
        Assert.IsTrue(condition.Properties.Any(p => p.Contains("Kind", StringComparison.OrdinalIgnoreCase)),
            "the tree must name the property, or we are asking the index something else entirely");
    }

    [TestMethod]
    public void ASizeConstraintKeepsItsNumberAsANumber()
    {
        // The PROPVARIANT coercion order matters here: read as a string, 1mb becomes "1048576" and the
        // emitted SQL compares sizes as text, where 9 sorts after 10.
        var condition = _aqs.Parse("size:>1mb");
        Assert.IsNotNull(condition);

        var leaf = Leaves(condition).FirstOrDefault(l => l.Value is long);
        Assert.IsNotNull(leaf, "the size must arrive as a number, not a string");
        Assert.AreEqual(1024L * 1024L, leaf.Value, "1mb is 1048576 bytes");
        Assert.AreEqual(SearchComparison.GreaterThan, leaf.Comparison);
    }

    [TestMethod]
    public void ARelativeDateBecomesAnActualDate()
    {
        // The reason for delegating at all: "last week" is a localised phrase we would otherwise parse
        // ourselves, wrongly, in every locale but ours.
        var condition = _aqs.Parse("modified:lastweek");
        Assert.IsNotNull(condition);

        var dated = Leaves(condition).Where(l => l.Value is DateTime).ToList();
        Assert.IsTrue(dated.Count > 0, "a relative date should resolve to a real DateTime");
        Assert.IsTrue(dated.All(l => ((DateTime)l.Value!) > DateTime.UtcNow.AddYears(-1)),
            "a date read out of the wrong PROPVARIANT field lands in 1601, not last week");
    }

    [TestMethod]
    public void TheParsedTreeEmitsUsableSql()
    {
        var condition = _aqs.Parse("kind:document")!;
        var where     = SearchConditionSql.ToWhereClause(condition);

        Assert.IsNotNull(where);
        StringAssert.Contains(where, "System.Kind");
    }

    private static IEnumerable<SearchCondition> Leaves(SearchCondition condition) =>
        condition.Kind == SearchConditionKind.Leaf
            ? [condition]
            : condition.Children.SelectMany(Leaves);

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
            Assert.IsNotNull(_aqs.Parse(token), $"'{token}' should parse");
    }

    [TestMethod]
    public void NoLeafComesBackWithAnOperatorWeCannotExpress()
    {
        // Unsupported is deliberately not mapped to a nearby operator, so if a common constraint parses
        // into one, the walk silently stops applying it. Better to learn that here.
        foreach (var token in new[] { "kind:document", "size:>1mb", "ext:.txt" })
        {
            var condition = _aqs.Parse(token);
            Assert.IsNotNull(condition);
            Assert.IsFalse(condition.HasUnsupportedComparison, $"'{token}' produced an unmappable operator");
        }
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

    [TestMethod]
    public void APlainTermQueryIsAcceptedByTheIndex()
    {
        // CONTAINS on System.FileName is what gives a literal term whole-word matching on the name. If the
        // provider rejected it, the OleDbException would be swallowed as "index unavailable" and the
        // feature would look broken rather than wrong — so the origin is asserted, not just the count.
        var terms  = SearchSyntax.ParseTerms("needle", []);
        var parsed = SearchQueryParser.FromTerms(terms);
        Assert.IsNotNull(parsed);
        StringAssert.Contains(parsed.WhereClause, "CONTAINS(System.FileName");

        var found = WindowsSearchService
            .SearchWithOriginAsync(parsed, KnownFolder(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.AreEqual(SearchOrigin.Index, found.Origin,
            "the query did not run — a rejected clause is reported as an unreachable index");
    }

    [TestMethod]
    public void AQuotedPhraseQueryIsAcceptedByTheIndex()
    {
        // A phrase goes into CONTAINS with its spaces intact; unbalanced quoting is the usual way to break
        // this, and it breaks at execution rather than at parse.
        var terms  = SearchSyntax.ParseTerms("\"the lost dog\"", []);
        var parsed = SearchQueryParser.FromTerms(terms);
        Assert.IsNotNull(parsed);

        var found = WindowsSearchService
            .SearchWithOriginAsync(parsed, KnownFolder(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.AreEqual(SearchOrigin.Index, found.Origin);
    }

    private static string KnownFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
