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
[CoversNode("search-terms")]
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
    public void TheLooserMatchIsStillAvailableOnANameAsAGlob()
    {
        // Whole-word is safe to impose on a literal because anyone unsure of the ending can say so — on the
        // file name, where a glob applies.
        var term = Glob("needle*");

        Assert.IsTrue(term.Matches("needless.txt", isName: true));
        Assert.IsTrue(term.Matches("needle.txt",   isName: true));
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
}
