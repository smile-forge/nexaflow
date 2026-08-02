using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// Mixed queries: a filename glob, a delimited regex and plain text on one line, AND-ed together, with
/// <c>|</c> as OR inside a term. <c>/…/</c> stays a strict regular expression — a glob and a regex disagree
/// about what <c>?</c> and <c>*</c> mean, so the two syntaxes are kept apart rather than guessed between.
/// <para>
/// Glob recognition is <em>injected</em>, not built in: it belongs to the surfaces that search files, and a
/// page searching its own single body must never be handed a <c>*.txt</c> term it can only ignore.
/// </para>
/// </summary>
[TestClass]
[CoversNode("page-search-regex")]
public class SearchTermParsingTests
{
    private static readonly ISearchTermRecognizer[] WithGlobs = [new GlobTermRecognizer()];

    [TestMethod]
    public void MixedQuery_SplitsIntoGlobRegexAndText()
    {
        var terms = SearchSyntax.ParseTerms(@"*.txt|*.md /ma(ths|gic)/ ocr", WithGlobs);

        Assert.AreEqual(3, terms.Count);

        Assert.IsTrue(terms[0].NameOnly, "a filename glob is recognised as one");
        Assert.AreEqual("*.txt|*.md", terms[0].Label);

        Assert.AreEqual(SearchTermKind.Regex, terms[1].Kind);
        Assert.AreEqual("ma(ths|gic)", terms[1].Value);

        Assert.AreEqual(SearchTermKind.Text, terms[2].Kind);
        Assert.AreEqual("ocr", terms[2].Value);
    }

    // ── Globs are opt-in ──────────────────────────────────────────────────────

    [TestMethod]
    public void WithoutAGlobRecognizer_GlobSyntaxIsJustText()
    {
        // What a text viewer sees. It gets a literal term it can honour, not a name-scoped one it can't.
        var terms = SearchSyntax.ParseTerms("*.txt");

        Assert.AreEqual(1, terms.Count);
        Assert.AreEqual(SearchTermKind.Text, terms[0].Kind);
        Assert.IsFalse(terms[0].NameOnly);
    }

    [TestMethod]
    public void WithAGlobRecognizer_TheSameTokenBecomesAGlobPattern()
    {
        var term = SearchSyntax.ParseTerms("*.txt", WithGlobs)[0];

        Assert.IsTrue(term.NameOnly);
        Assert.IsTrue(term.Matches("notes.txt", isName: true));
        Assert.IsFalse(term.Matches("notes.txt.bak", isName: true), "a glob describes the whole name");
        // Name-scoped on purpose: a glob NARROWS which files are considered, which is what lets a folder
        // scan skip one without opening it. Someone wanting "*.txt" found inside a document quotes it.
        Assert.IsFalse(term.Matches("a body mentioning notes.txt", isName: false),
            "a glob restricts the file set; it is not a content pattern");
    }

    [TestMethod]
    public void MixedGlobAndText_IsLeftAsTextRatherThanHalfInterpreted()
    {
        // "*.txt|notes" — one alternative is a glob and one isn't; guessing which the user meant is worse
        // than treating the whole token literally.
        var term = SearchSyntax.ParseTerms("*.txt|notes", WithGlobs)[0];

        Assert.AreEqual(SearchTermKind.Text, term.Kind);
    }

    // ── Quoted phrases ────────────────────────────────────────────────────────

    [TestMethod]
    public void UnquotedWords_AreSeparateTermsAndedTogether()
    {
        // "the lost dog" unquoted means all three words must appear — not necessarily together.
        var terms = SearchSyntax.ParseTerms("the lost dog", WithGlobs);

        Assert.AreEqual(3, terms.Count);
        CollectionAssert.AreEqual(new[] { "the", "lost", "dog" }, terms.Select(t => t.Value).ToArray());
    }

    [TestMethod]
    public void QuotedWords_AreOnePhrase()
    {
        var terms = SearchSyntax.ParseTerms("\"the lost dog\"", WithGlobs);

        Assert.AreEqual(1, terms.Count);
        Assert.AreEqual("the lost dog", terms[0].Value);
        Assert.IsTrue(terms[0].Matches("a story about the lost dog", isName: false));
        Assert.IsFalse(terms[0].Matches("the dog was lost", isName: false),
            "a phrase is contiguous — that is the point of quoting it");
    }

    [TestMethod]
    public void QuotedPhrase_MixesWithOtherTerms()
    {
        var terms = SearchSyntax.ParseTerms("*.txt \"the lost dog\" urgent", WithGlobs);

        Assert.AreEqual(3, terms.Count);
        Assert.IsTrue(terms[0].NameOnly);
        Assert.AreEqual("the lost dog", terms[1].Value);
        Assert.AreEqual("urgent", terms[2].Value);
    }

    [TestMethod]
    public void QuotingMeansLiteral_SoARecognizerCannotClaimIt()
    {
        // Quoted "*.txt" is someone looking for that text, not filtering by extension.
        var term = SearchSyntax.ParseTerms("\"*.txt\"", WithGlobs)[0];

        Assert.AreEqual(SearchTermKind.Text, term.Kind);
        Assert.IsFalse(term.NameOnly);
        Assert.IsTrue(term.Matches("see *.txt in the readme", isName: false));
    }

    [TestMethod]
    public void PipeInsideAQuotedPhrase_IsJustACharacter()
    {
        var terms = SearchSyntax.ParseTerms("\"a|b\"", WithGlobs);

        Assert.AreEqual(1, terms.Count);
        Assert.AreEqual(1, terms[0].Alternatives.Count, "quoting suppresses OR-splitting too");
        Assert.AreEqual("a|b", terms[0].Value);
    }

    // ── Round-tripping between surfaces ───────────────────────────────────────
    //
    // A query travels from the browser to a Search tab as a string page-parameter. Anything Format drops
    // is silently gone by the time the other side parses it back.

    [TestMethod]
    public void FormatThenParse_KeepsEveryTerm()
    {
        const string typed = "ocr /maths/ *.txt";
        var request = SearchSyntax.ParseRequest(typed, WithGlobs);

        var reparsed = SearchSyntax.ParseRequest(SearchSyntax.Format(request), WithGlobs);

        Assert.AreEqual(3, reparsed.Terms.Count, "a handoff must not flatten a query to its first term");
        CollectionAssert.AreEqual(
            request.Terms.Select(t => t.Label).ToArray(),
            reparsed.Terms.Select(t => t.Label).ToArray());
    }

    [TestMethod]
    public void FormatThenParse_KeepsAPhraseAPhrase()
    {
        // Losing the quotes turns one phrase back into three AND-ed words.
        var request = SearchSyntax.ParseRequest("\"Requirement descriptions typically\"", WithGlobs);

        var reparsed = SearchSyntax.ParseRequest(SearchSyntax.Format(request), WithGlobs);

        Assert.AreEqual(1, reparsed.Terms.Count);
        Assert.AreEqual("Requirement descriptions typically", reparsed.Terms[0].Value);
    }

    [TestMethod]
    public void FormatThenParse_KeepsARegexARegex()
    {
        var request  = SearchSyntax.ParseRequest("/ma(ths)/c", WithGlobs);
        var reparsed = SearchSyntax.ParseRequest(SearchSyntax.Format(request), WithGlobs);

        Assert.AreEqual(SearchTermKind.Regex, reparsed.Terms[0].Kind);
        Assert.AreEqual("ma(ths)", reparsed.Terms[0].Value);
        Assert.IsTrue(reparsed.Terms[0].MatchCase);
    }

    [TestMethod]
    public void FormatThenParse_KeepsAGlob()
    {
        var request  = SearchSyntax.ParseRequest("*.txt|*.md", WithGlobs);
        var reparsed = SearchSyntax.ParseRequest(SearchSyntax.Format(request), WithGlobs);

        Assert.IsTrue(reparsed.Terms[0].NameOnly);
        Assert.AreEqual(2, reparsed.Terms[0].Alternatives.Count);
    }

    // ── Regex terms belong to the base syntax ─────────────────────────────────

    [TestMethod]
    public void PipeInsideARegex_BelongsToThePattern_NotTheTermSplitter()
    {
        var terms = SearchSyntax.ParseTerms("/ma(ths|gic)/", WithGlobs);

        Assert.AreEqual(1, terms.Count);
        Assert.AreEqual("ma(ths|gic)", terms[0].Value);
    }

    [TestMethod]
    public void RegexMayContainSpaces()
    {
        // Its delimiters say where it ends, so whitespace inside it isn't a term boundary.
        var terms = SearchSyntax.ParseTerms(@"/TODO:\s*fix the parser/ urgent", WithGlobs);

        Assert.AreEqual(2, terms.Count);
        Assert.AreEqual(@"TODO:\s*fix the parser", terms[0].Value);
        Assert.AreEqual("urgent", terms[1].Value);
    }

    [TestMethod]
    public void ARecognizerNeverGetsToReinterpretADelimitedPattern()
    {
        // "/*.txt/" is a regex the user delimited on purpose — the glob recognizer must not claim it.
        var term = SearchSyntax.ParseTerms("/*.txt/", WithGlobs)[0];

        Assert.AreEqual(SearchTermKind.Regex, term.Kind);
        Assert.IsFalse(term.NameOnly);
        Assert.IsFalse(term.TryValidate(out _), "a leading '*' has nothing to repeat");
    }

    // ── The wildcard collision, kept explicit ─────────────────────────────────

    [TestMethod]
    public void QuestionMark_MeansOneCharacterInAGlob_AndOptionalInARegex()
    {
        var glob = SearchSyntax.ParseTerms("mat?s", WithGlobs)[0];
        Assert.IsTrue(glob.Matches("matXs", isName: true), "glob '?' consumes exactly one character");
        Assert.IsFalse(glob.Matches("mats", isName: true), "…so it is not optional");

        var regex = SearchSyntax.ParseTerms("/mat?s/", WithGlobs)[0];
        Assert.IsTrue(regex.Matches("mas", isName: true), "regex '?' makes the 't' optional");
        Assert.IsFalse(regex.Matches("matter", isName: true));
    }

    [TestMethod]
    public void InvalidRegex_IsReportedNotSilentlyUnmatchable()
    {
        var term = SearchSyntax.ParseTerms("/[unclosed/", WithGlobs)[0];

        Assert.IsFalse(term.TryValidate(out var error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    // ── Single-term inputs keep behaving exactly as before ────────────────────

    [TestMethod]
    public void PlainSingleTerm_StillParsesAsASimpleRequest()
    {
        var request = SearchSyntax.Parse("magic");

        Assert.IsFalse(request.IsRegex);
        Assert.AreEqual("magic", request.Text);
    }

    [TestMethod]
    public void SingleDelimitedTerm_StillParsesAsARegexRequest()
    {
        var request = SearchSyntax.Parse("/magic/c");

        Assert.IsTrue(request.IsRegex);
        Assert.AreEqual("magic", request.Text);
        Assert.IsTrue(request.MatchCase);
    }
}
