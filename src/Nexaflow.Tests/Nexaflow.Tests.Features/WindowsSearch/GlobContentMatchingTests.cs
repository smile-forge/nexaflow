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
public class GlobContentMatchingTests
{
    private static SearchTerm Glob(string token) =>
        new GlobTermRecognizer().Recognize(token)
        ?? throw new AssertFailedException($"'{token}' was not recognised as a glob");

    // ── The wildcard must stop at whitespace ──────────────────────────────────

    [TestMethod]
    public void TrailingStar_MatchesTheWordNotTheRestOfTheLine()
    {
        // The whole point: "book*" in "bookcase in the corner" is a hit on "bookcase". Anchored the way a
        // filename glob is, it would instead ask whether the entire text is one word starting "book".
        var term = Glob("book*");

        Assert.IsTrue(term.Matches("bookcase in the corner", isName: false));
        Assert.IsTrue(term.Matches("the bookcase", isName: false));
        Assert.IsFalse(term.Matches("a notebook", isName: false),
            "the wildcard extends the word, it does not float to the middle of another one");
    }

    [TestMethod]
    public void LeadingStar_IsBoundedAtTheStartOfTheWord()
    {
        var term = Glob("*case");

        Assert.IsTrue(term.Matches("the bookcase stands", isName: false));
        Assert.IsFalse(term.Matches("bookcases everywhere", isName: false),
            "the token ends at 'case'; 'bookcases' is a different word");
    }

    [TestMethod]
    public void AWildcardNeverSpansASpace()
    {
        // The failure this guards: "*" compiled as ".*" happily swallows the whole document, so every
        // file matches and the search looks like it worked.
        var term = Glob("the*corner");

        Assert.IsFalse(term.Matches("the bookcase in the corner", isName: false));
        Assert.IsTrue(term.Matches("theothercorner", isName: false));
    }

    [TestMethod]
    public void QuestionMark_IsOneNonSpaceCharacter()
    {
        var term = Glob("boo?");

        Assert.IsTrue(term.Matches("book on the shelf", isName: false));
        Assert.IsFalse(term.Matches("boo hoo", isName: false));
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

    [TestMethod]
    public void TheSameTermAnswersBothQuestions()
    {
        // One constraint the user typed once, applied to whichever side is being asked about.
        var term = Glob("report*");

        Assert.IsTrue(term.Matches("report-2026.pdf", isName: true));
        Assert.IsTrue(term.Matches("see reports for detail", isName: false));
    }

    // ── It is no longer name-scoped ───────────────────────────────────────────

    [TestMethod]
    public void AGlobNoLongerRulesOutAFileByNameAlone()
    {
        // Previously name-only, so a file whose CONTENTS matched was discarded before anything read it.
        var request = SearchSyntax.ParseRequest("*.txt", [new GlobTermRecognizer()]);

        Assert.IsFalse(request.HasNameOnlyTerms,
            "a glob now asks about contents too, so a page with no filenames must not be told otherwise");
        Assert.IsFalse(request.NameRulesItOut("readme.md"),
            "the name failing is no longer the end of the question");
    }

    [TestMethod]
    public void AlternativesKeepBothForms()
    {
        var term = Glob("*.txt|*.md");

        Assert.AreEqual(2, term.Alternatives.Count);
        Assert.AreEqual(2, term.ContentForms.Count);
        Assert.IsTrue(term.Matches("notes.md", isName: true));
        Assert.IsTrue(term.Matches("see notes.txt here", isName: false));
    }
}
