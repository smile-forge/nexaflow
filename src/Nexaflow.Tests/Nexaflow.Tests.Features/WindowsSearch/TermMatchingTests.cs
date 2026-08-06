using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

/// <summary>
/// A glob is the wildcard syntax people reach for instead of writing a regex, so it has to work against
/// file CONTENTS as well as names. The two are different questions and a single pattern can't ask both:
/// anchored to a whole name, or bounded to one word inside a document.
/// </summary>
[TestClass]
// Class-level because SearchTerm is the shared data object every child of this node parses, scores or
// refines — the whole class exercises the container, not one of its leaves.
[CoversNode("search-user-input")]
public class TermMatchingTests
{
    private static SearchTerm Glob(string token) =>
        new GlobTermRecognizer().Recognize(token)
        ?? throw new AssertFailedException($"'{token}' was not recognised as a glob");

    // ── A glob restricts the file set ─────────────────────────────────────────

    [TestMethod]
    public void AGlobIsAFilenameGlob()
    {
        // It narrows WHICH FILES are considered — which is the point of typing one, and what lets a folder
        // scan skip a file without opening and reading it.
        var term = Glob("*.txt");

        Assert.IsTrue(term.NameOnly);
        Assert.IsTrue(term.Matches("notes.txt", isName: true));
        Assert.IsFalse(term.Matches("a line mentioning notes.txt", isName: false),
            "a glob is not a content pattern");
    }

    [TestMethod]
    public void QuotingItAsksForItInTheContentsInstead()
    {
        // The escape hatch, and the reason the glob can stay name-scoped: "*.txt" quoted is a literal term,
        // so it is matched against the body like any other text.
        var term = SearchSyntax.ParseTerms("\"*.txt\"", [new GlobTermRecognizer()])[0];

        Assert.AreEqual(SearchTermKind.Text, term.Kind);
        Assert.IsFalse(term.NameOnly);
        Assert.IsTrue(term.Matches("see *.txt in the readme", isName: false));
    }

    [TestMethod]
    public void AGlobRulesAFileOutByNameAlone()
    {
        // What makes it cheap: NameRulesItOut lets the walk reject a file before it is ever read.
        var request = SearchSyntax.ParseRequest("*.txt", [new GlobTermRecognizer()]);

        Assert.IsTrue(request.NameRulesItOut("readme.md"));
        Assert.IsFalse(request.NameRulesItOut("readme.txt"));
    }

    // ── Names still behave as filenames ───────────────────────────────────────

    [TestMethod]
    public void AgainstAName_TheGlobIsStillWholeName()
    {
        var term = Glob("*.txt");

        Assert.IsTrue(term.Matches("notes.txt", isName: true));
        Assert.IsFalse(term.Matches("notes.txt.bak", isName: true),
            "a filename glob is anchored — otherwise every archive of a .txt matches");
    }

    // ── It is no longer name-scoped ───────────────────────────────────────────

    // ── A literal term means the word it spells ───────────────────────────────

    [TestMethod]
    public void APlainTermDoesNotMatchALongerWord()
    {
        // "needle" finding "needless" is the reported bug. It is also what the index does NOT do — CONTAINS
        // is word-based — so substring matching here made the post-filter disagree with the query feeding it.
        var term = new SearchTerm(SearchTermKind.Text, ["needle"]);

        Assert.IsTrue(term.Matches("a needle in the haystack", isName: false));
        Assert.IsFalse(term.Matches("this is needless", isName: false));
        Assert.IsFalse(term.Matches("threadneedle street", isName: false));
    }

    [TestMethod]
    public void PunctuationStillEndsAWord()
    {
        // Otherwise the rule would be useless on filenames and prose alike, where words butt against dots,
        // hyphens and commas far more often than spaces.
        var term = new SearchTerm(SearchTermKind.Text, ["needle"]);

        Assert.IsTrue(term.Matches("needle.txt", isName: true));
        Assert.IsTrue(term.Matches("find-needle-here.md", isName: true));
        Assert.IsTrue(term.Matches("the needle, obviously", isName: false));
        Assert.IsFalse(term.Matches("needles.txt", isName: true));
    }

    [TestMethod]
    public void ABareWildcardWordIsNotAFilenameGlob()
    {
        // "needle*" has no extension or path, so it is NOT a file-only glob — the recogniser declines it and
        // it becomes an ordinary wildcard text term, matched against contents as well as the name.
        Assert.IsNull(new GlobTermRecognizer().Recognize("needle*"),
            "a bare wildcard word is a content wildcard, not a filename glob");

        var term = SearchSyntax.ParseTerms("needle*")[0];

        Assert.AreEqual(SearchTermKind.Text, term.Kind);
        Assert.IsFalse(term.NameOnly, "it searches contents too");
        Assert.IsTrue(term.Matches("needless.txt",       isName: true),  "the looser match, on the name");
        Assert.IsTrue(term.Matches("a needless remark",  isName: false), "and in the content");
    }

    [TestMethod]
    public void OnlyANameShapedPatternIsAFilenameGlob()
    {
        var glob = new GlobTermRecognizer();

        // Name-shaped — an extension or a path — so these are file-only.
        foreach (var name in new[] { "*.txt", "something*.*", "*a*b?c.*d*", @"src\**\*.cs" })
            Assert.IsNotNull(glob.Recognize(name), $"'{name}' is a filename glob");

        // No filename shape — these match name AND content.
        foreach (var word in new[] { "*term*", "term*", "*term", "*term1*term2*term?" })
            Assert.IsNull(glob.Recognize(word), $"'{word}' is a content wildcard, not a filename glob");
    }

    [TestMethod]
    public void AQuotedPhraseIsBoundedTheSameWay()
    {
        // Boundary-checked rather than tokenised, so a phrase behaves like one long word.
        var term = new SearchTerm(SearchTermKind.Text, ["the lost dog"]);

        Assert.IsTrue(term.Matches("we found the lost dog yesterday", isName: false));
        Assert.IsFalse(term.Matches("the lost dogma of it", isName: false));
    }

    [TestMethod]
    public void AlternativesAreOrEdWithinTheTerm()
    {
        var term = Glob("*.txt|*.md");

        Assert.AreEqual(2, term.Alternatives.Count);
        Assert.IsTrue(term.Matches("notes.md",  isName: true));
        Assert.IsTrue(term.Matches("notes.txt", isName: true));
        Assert.IsFalse(term.Matches("notes.pdf", isName: true));
    }

    // ── Wildcards widen a literal term, inside content as well as on a name ────

    [TestMethod]
    public void APrefixWildcardMatchesAWordStartingWithIt()
    {
        // "fig*" is the word starting "fig" — so it finds "configure"? No: whole-word still holds, the
        // wildcard only relaxes the END. It finds "figure" and "fig", not "prefigure".
        var term = new SearchTerm(SearchTermKind.Text, ["fig*"]);

        Assert.IsTrue(term.Matches("draw the figure", isName: false));
        Assert.IsTrue(term.Matches("a fig, ripe",     isName: false));
        Assert.IsFalse(term.Matches("we prefigure it", isName: false), "the word still starts at a boundary");
        Assert.IsFalse(term.Matches("nothing here",    isName: false));
    }

    [TestMethod]
    public void ASuffixWildcardMatchesAWordEndingWithIt()
    {
        var term = new SearchTerm(SearchTermKind.Text, ["*fig"]);

        Assert.IsTrue(term.Matches("the config value", isName: false));
        Assert.IsTrue(term.Matches("just fig",          isName: false));
        Assert.IsFalse(term.Matches("configure it",     isName: false), "the word still ends at a boundary");
    }

    [TestMethod]
    public void SurroundingWildcardsAreTheSubstringEscapeHatch()
    {
        // The looser match the whole-word rule deliberately withholds — asked for explicitly.
        var term = new SearchTerm(SearchTermKind.Text, ["*fig*"]);

        Assert.IsTrue(term.Matches("reconfigure now", isName: false));
        Assert.IsTrue(term.Matches("the config",       isName: false));
        Assert.IsFalse(term.Matches("nothing here",    isName: false));
    }

    [TestMethod]
    public void BarePartial_DoesNotMatchALongerWord_ButPartialStarDoes()
    {
        // The exact contrast the reported bug is about: plain "fig" is the word, "fig*" opts into the prefix.
        Assert.IsFalse(new SearchTerm(SearchTermKind.Text, ["fig"]).Matches("configure", isName: false));
        Assert.IsFalse(new SearchTerm(SearchTermKind.Text, ["fig"]).Matches("figure",    isName: false));
        Assert.IsTrue (new SearchTerm(SearchTermKind.Text, ["fig*"]).Matches("figure",   isName: false));
    }

    [TestMethod]
    public void AQuestionMarkIsExactlyOneWordCharacter()
    {
        var term = new SearchTerm(SearchTermKind.Text, ["f?g"]);

        Assert.IsTrue(term.Matches("fig tree", isName: false));
        Assert.IsTrue(term.Matches("a fog",    isName: false));
        Assert.IsFalse(term.Matches("flag",    isName: false), "? is one character, not any run");
    }

    [TestMethod]
    public void QuotingTurnsAWildcardBackIntoLiteralText()
    {
        // Exact is the parser's record that the user quoted the term — "partial*" is a search for an
        // asterisk, not a prefix. Set here directly; SearchSyntax sets it when it unquotes.
        var literal  = new SearchTerm(SearchTermKind.Text, ["fig*"], Exact: true);
        var wildcard = new SearchTerm(SearchTermKind.Text, ["fig*"]);

        Assert.IsTrue(literal.Matches("fig* is literal here", isName: false), "the asterisk is a character");
        Assert.IsFalse(literal.Matches("the figure",          isName: false), "quoted, it is not a prefix");
        Assert.IsTrue(wildcard.Matches("the figure",          isName: false), "unquoted, it is");
    }

    [TestMethod]
    public void QuotingIsWhatSearchSyntaxRecordsAsExact()
    {
        // End to end: the quotes are stripped, but the "as written" intent survives on the term.
        var quoted = SearchSyntax.ParseTerms("\"fig*\"")[0];
        var bare   = SearchSyntax.ParseTerms("fig*")[0];

        Assert.IsTrue(quoted.Exact);
        Assert.IsFalse(bare.Exact);
        Assert.IsFalse(quoted.Matches("the figure", isName: false));
        Assert.IsTrue(bare.Matches("the figure",    isName: false));
    }

    // ── Occurrences agree with Matches, span by span ──────────────────────────

    [TestMethod]
    public void OccurrencesPointAtEveryMatchedWord()
    {
        var term = new SearchTerm(SearchTermKind.Text, ["fig*"]);
        const string text = "a figure and a fig, but not prefigure";

        var spans = term.Occurrences(text).ToList();

        Assert.AreEqual(2, spans.Count, "figure and fig — not the embedded one in prefigure");
        foreach (var (index, length) in spans)
            StringAssert.StartsWith(text.Substring(index, length), "fig");
        // The count a page shows and the spans it paints come from the same call, so they cannot disagree.
        Assert.AreEqual(term.Matches(text, isName: false), spans.Count > 0);
    }

    [TestMethod]
    public void Occurrences_AreEmpty_ForANameScopedTerm()
    {
        // There is no name in a body being painted, so a glob contributes no spans to it.
        Assert.AreEqual(0, Glob("*.txt").Occurrences("see notes.txt inside").Count());
    }
}
