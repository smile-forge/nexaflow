using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Search;

/// <summary>
/// The <c>?</c> bar's syntax. Parsed in one place so no page re-derives it — and so regex is something the
/// user <em>declares</em> rather than something the shell infers from stray punctuation.
/// </summary>
[TestClass]
[CoversNode("ai-intent-symbol")]
public class SearchSyntaxTests
{
    [TestMethod]
    public void PlainText_IsLiteral()
    {
        var r = SearchSyntax.Parse("config.json cache");

        Assert.IsFalse(r.IsRegex, "punctuation alone must not promote text to a regex — that ambiguity is the bug");
        Assert.AreEqual("config.json cache", r.Text);
    }

    [TestMethod]
    public void DelimitedPattern_IsRegex()
    {
        var r = SearchSyntax.Parse("/alpha\\d+/");

        Assert.IsTrue(r.IsRegex);
        Assert.AreEqual(@"alpha\d+", r.Text);
        Assert.IsFalse(r.MatchCase, "searches are case-insensitive unless asked otherwise");
    }

    [TestMethod]
    public void UnterminatedPattern_IsStillRegex()
    {
        // The user is mid-type; the status dot should already say "regex".
        var r = SearchSyntax.Parse("/alpha");

        Assert.IsTrue(r.IsRegex);
        Assert.AreEqual("alpha", r.Text);
    }

    [TestMethod]
    public void CaseFlag_TurnsOnMatchCase()
    {
        Assert.IsTrue(SearchSyntax.Parse("/Error/c").MatchCase);
        Assert.IsFalse(SearchSyntax.Parse("/Error/i").MatchCase);
    }

    [TestMethod]
    public void PathLikeInput_StaysLiteral()
    {
        // "/api/v1/users" would otherwise parse as regex "api/v1" with flags "users".
        var r = SearchSyntax.Parse("/api/v1/users");

        Assert.IsFalse(r.IsRegex);
        Assert.AreEqual("/api/v1/users", r.Text);
    }

    [TestMethod]
    public void EmptyPattern_StaysLiteral()
    {
        // "//" as a regex matches everything — never silently do that.
        var r = SearchSyntax.Parse("//");

        Assert.IsFalse(r.IsRegex);
    }

    [TestMethod]
    public void Format_RoundTrips()
    {
        foreach (var input in new[] { "plain text", @"/alpha\d+/", "/Error/c" })
            Assert.AreEqual(
                SearchSyntax.Parse(input),
                SearchSyntax.Parse(SearchSyntax.Format(SearchSyntax.Parse(input))),
                $"'{input}' did not survive a Format/Parse round trip");
    }

    [TestMethod]
    public void MalformedRegex_IsReportedNotSwallowed()
    {
        var r = SearchSyntax.Parse("/[unclosed/");

        Assert.IsTrue(r.IsRegex);
        Assert.IsFalse(r.TryCompileRegex(out _, out var error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Matches_HonoursModeAndCase()
    {
        Assert.IsTrue(new SearchRequest(@"alpha\d+", IsRegex: true).Matches("see alpha42 here"));
        Assert.IsFalse(new SearchRequest(@"alpha\d+").Matches("see alpha42 here"), "literal mode must not compile");
        Assert.IsTrue(new SearchRequest("ALPHA").Matches("alpha"));
        Assert.IsFalse(new SearchRequest("ALPHA", MatchCase: true).Matches("alpha"));
    }

    [TestMethod]
    public void Prose_IsLeftToTheAgent()
    {
        Assert.IsFalse(SearchScoring.LooksLikeProse("budget report"));
        Assert.IsFalse(SearchScoring.LooksLikeProse("\"annual budget report\" 2024"));
        Assert.IsTrue(SearchScoring.LooksLikeProse("do you think you can search for office documents with george"));
    }
}
