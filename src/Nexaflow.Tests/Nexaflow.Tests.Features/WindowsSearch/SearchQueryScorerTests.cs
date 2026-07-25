using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch;

[TestClass]
[CoversNode("search-query-scorer")]
public class SearchQueryScorerTests
{
    [TestMethod]
    public void Score_Null_ReturnsZero()
    {
        Assert.AreEqual(0f, SearchQueryScorer.Score(string.Empty));
    }

    [TestMethod]
    public void Score_Whitespace_ReturnsZero()
    {
        Assert.AreEqual(0f, SearchQueryScorer.Score("   "));
    }

    [TestMethod]
    public void Score_FileGlob_IsHigh()
    {
        float score = SearchQueryScorer.Score("*.cs");

        Assert.IsTrue(score >= 0.7f, $"Expected glob score ≥ 0.7, got {score}");
    }

    [TestMethod]
    public void Score_AqsSizeFilter_IsHigh()
    {
        float score = SearchQueryScorer.Score("size:>100mb");

        Assert.IsTrue(score >= 0.6f, $"Expected AQS filter score ≥ 0.6, got {score}");
    }

    [TestMethod]
    public void Score_QuotedTerm_IsHigh()
    {
        float score = SearchQueryScorer.Score("\"config.json\"");

        Assert.IsTrue(score >= 0.6f, $"Expected quoted term score ≥ 0.6, got {score}");
    }

    [TestMethod]
    public void Score_ShortSearchableTerms_IsModerate()
    {
        float score = SearchQueryScorer.Score("crash log");

        // Short query, no sentence structure → moderate score
        Assert.IsTrue(score >= 0.3f, $"Expected score ≥ 0.3, got {score}");
    }

    [TestMethod]
    public void Score_ConversationalSentence_IsLow()
    {
        float score = SearchQueryScorer.Score("Can you help me find the configuration file, please?");

        Assert.IsTrue(score < 0.3f, $"Expected conversational sentence score < 0.3, got {score}");
    }

    [TestMethod]
    public void Score_VerbHeavy_PenalizedRelativeToKeywords()
    {
        float verbHeavy = SearchQueryScorer.Score("I am trying to fix and make it work");
        float globs     = SearchQueryScorer.Score("*.log *.txt");

        Assert.IsTrue(globs > verbHeavy, "Glob query should outscore verb-heavy text");
    }

    [TestMethod]
    public void Score_PrefixSyntax_IsHigh()
    {
        float score = SearchQueryScorer.Score("+report -draft");

        Assert.IsTrue(score >= 0.5f, $"Expected prefix-syntax score ≥ 0.5, got {score}");
    }

    [TestMethod]
    public void Score_VeryLongInput_IsLower()
    {
        var longInput = string.Join(" ", Enumerable.Repeat("word", 20));
        float score = SearchQueryScorer.Score(longInput);

        // Long inputs are penalized
        Assert.IsTrue(score <= 0.5f, $"Expected long input score ≤ 0.5, got {score}");
    }

    [TestMethod]
    public void Score_AlwaysInZeroToOneRange()
    {
        var inputs = new[] { "*.cs", "hello", "size:>1mb", "the quick brown fox", "\"term\"", "+a -b c" };
        foreach (var input in inputs)
        {
            float score = SearchQueryScorer.Score(input);
            Assert.IsTrue(score >= 0f && score <= 1f, $"Score {score} out of [0,1] for '{input}'");
        }
    }
}
