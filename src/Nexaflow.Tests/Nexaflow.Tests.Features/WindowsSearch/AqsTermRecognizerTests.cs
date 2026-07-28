using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// AQS property constraints as one more term kind, mixed freely with globs, patterns and text. The COM
/// parser sits behind <see cref="IAqsTranslator"/>, so everything above it — which token is a constraint,
/// how terms combine, what the SQL looks like — is testable without the Windows Search service.
/// </summary>
[TestClass]
[CoversNode("search-aqs")]
public class AqsTermRecognizerTests
{
    /// <summary>Knows two properties and nothing else — enough to prove the routing.</summary>
    private sealed class FakeAqs : IAqsTranslator
    {
        public bool Recognises(string token) =>
            token.StartsWith("kind:", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("size:", StringComparison.OrdinalIgnoreCase);

        public string? ToWhereClause(string token) =>
            token.StartsWith("kind:", StringComparison.OrdinalIgnoreCase)
                ? $"System.Kind = '{token[5..]}'"
                : token.StartsWith("size:>", StringComparison.OrdinalIgnoreCase)
                    ? "System.Size > 1048576"
                    : null;
    }

    private static ISearchTermRecognizer[] Recognizers() =>
        [new AqsTermRecognizer(new FakeAqs()), new GlobTermRecognizer()];

    [TestMethod]
    public void PropertyConstraint_BecomesAStructuredTerm()
    {
        var terms = SearchSyntax.ParseTerms("kind:document", Recognizers());

        Assert.AreEqual(1, terms.Count);
        Assert.AreEqual(SearchTermKind.Structured, terms[0].Kind);
        Assert.AreEqual("kind:document", terms[0].Value);
    }

    [TestMethod]
    public void MixesWithGlobsPatternsAndText()
    {
        var terms = SearchSyntax.ParseTerms(@"*.txt /ma(ths)/ kind:document ocr", Recognizers());

        Assert.AreEqual(4, terms.Count);
        Assert.IsTrue(terms[0].NameOnly);
        Assert.AreEqual(SearchTermKind.Regex,      terms[1].Kind);
        Assert.AreEqual(SearchTermKind.Structured, terms[2].Kind);
        Assert.AreEqual(SearchTermKind.Text,       terms[3].Kind);
    }

    [TestMethod]
    public void UnknownProperty_StaysPlainText()
    {
        // The translator has the final say, so "http://example.com" isn't read as a property named "http".
        var terms = SearchSyntax.ParseTerms("http://example.com", Recognizers());

        Assert.AreEqual(SearchTermKind.Text, terms[0].Kind);
    }

    [TestMethod]
    public void WithoutTheRecognizer_ItIsJustText()
    {
        // A page not backed by the index never receives a constraint only the index could enforce.
        var terms = SearchSyntax.ParseTerms("kind:document");

        Assert.AreEqual(SearchTermKind.Text, terms[0].Kind);
    }

    // ── The post-filter must not re-test what only the index can answer ───────

    [TestMethod]
    public void StructuredTerms_AreNeverReTestedClientSide()
    {
        // Nothing in a filename or a file's text can tell you whether "size:>1mb" holds. Re-testing it
        // would reject every row the index had already qualified.
        var request = SearchSyntax.ParseRequest("kind:document ocr", Recognizers());

        Assert.IsTrue(request.MatchesName("ocr notes.docx"),
            "the text term matches and the structured one is already guaranteed");
        Assert.IsFalse(request.MatchesName("unrelated.docx"),
            "the text term still has to hold");
    }

    [TestMethod]
    public void AStructuredOnlyQuery_IsNotMatchedAwayByTheFilter()
    {
        var request = SearchSyntax.ParseRequest("kind:document", Recognizers());

        Assert.IsTrue(request.MatchesName("anything.docx"),
            "the index already applied the only constraint there was");
    }

    // ── SQL assembly ──────────────────────────────────────────────────────────

    [TestMethod]
    public void StructuredTerm_ReachesTheWhereClause()
    {
        var terms  = SearchSyntax.ParseTerms("kind:document ocr", Recognizers());
        var parsed = SearchQueryParser.FromTerms(terms, new FakeAqs());

        Assert.IsNotNull(parsed);
        StringAssert.Contains(parsed.WhereClause, "System.Kind = 'document'");
        StringAssert.Contains(parsed.WhereClause, "ocr");
        StringAssert.Contains(parsed.WhereClause, " AND ");
    }

    [TestMethod]
    public void UntranslatableConstraint_IsDroppedNotFaked()
    {
        // "size:<1mb" isn't understood by the fake. Dropping it widens the query, which the post-filter
        // can live with; inventing a clause would silently return the wrong files.
        var terms  = SearchSyntax.ParseTerms("size:<1mb ocr", Recognizers());
        var parsed = SearchQueryParser.FromTerms(terms, new FakeAqs());

        Assert.IsNotNull(parsed);
        Assert.IsFalse(parsed.WhereClause.Contains("System.Size"));
        StringAssert.Contains(parsed.WhereClause, "ocr");
    }

    // ── Refinement must not round-trip through rendered text ──────────────────

    [TestMethod]
    public void RefiningKeepsTheConstraintInTheIndexQuery()
    {
        // Refining "kind:document" with "ocr" must still ask the index for documents. Building the merged
        // query from the combined TERMS is what preserves that.
        var first  = SearchSyntax.ParseTerms("kind:document", Recognizers());
        var second = SearchSyntax.ParseTerms("ocr", Recognizers());

        var merged = SearchQueryParser.FromTerms([.. first, .. second], new FakeAqs());

        Assert.IsNotNull(merged);
        StringAssert.Contains(merged.WhereClause, "System.Kind = 'document'");
        StringAssert.Contains(merged.WhereClause, "ocr");
    }

    [TestMethod]
    public void RenderedQueryTextIsNotASubstituteForTheTerms()
    {
        // Why the merge above can't just re-parse the displayed query: the legacy single-string parser has
        // no term model, so it reads "kind:document" as characters to look for and narrows the index to
        // files literally containing that punctuation — none. Pinned so nobody reintroduces the round-trip.
        var rendered = SearchSyntax.Format(SearchSyntax.ParseRequest("kind:document", Recognizers()));
        var reparsed = SearchQueryParser.Parse(rendered);

        Assert.IsFalse(reparsed.WhereClause.Contains("System.Kind"),
            "the rendered text cannot carry a constraint only the term model knows about");
    }

    [TestMethod]
    public void WithNoTranslator_StructuredTermsAreSkipped()
    {
        // The service may not be present at all; the rest of the query must still run.
        var terms  = SearchSyntax.ParseTerms("kind:document ocr", Recognizers());
        var parsed = SearchQueryParser.FromTerms(terms, aqs: null);

        Assert.IsNotNull(parsed);
        StringAssert.Contains(parsed.WhereClause, "ocr");
    }
}
