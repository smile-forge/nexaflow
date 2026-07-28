using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's single "?" search route. It replaced five per-feature handlers: a page declares
/// <see cref="ISearchable"/> and this routes to it, so scoring, prefix forcing and regex syntax are
/// decided once rather than per feature.
/// </summary>
[TestClass]
[CoversNode("ai-intent-symbol")]
public class SearchQueryHandlerTests
{
    private sealed class Page : IPageViewModel, ISearchable
    {
        public SearchRequest? LastRequest;
        public bool?          LastDisplay;
        public SearchOutcome  Result   = SearchOutcome.None();
        public float          Score    = 0.6f;
        public bool           Regexable = true;

        public string GetContext() => "fake";
        public string SearchTargetDescription => "the fake page";
        public float ScoreQuery(string input) => Score;

        public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        {
            LastRequest = request;
            LastDisplay = display;
            return Task.FromResult(Result);
        }
    }

    private sealed class PlainPage : IPageViewModel
    {
        public string GetContext() => "not searchable";
    }

    private static SearchQueryHandler Handler() => new();

    [TestMethod]
    public void Symbol_IsTheQuestionMark() => Assert.AreEqual("?", Handler().Symbol);

    // ── CanProcess ────────────────────────────────────────────────────────────

    [TestMethod]
    public void NonSearchablePage_IsNeverClaimed()
    {
        Assert.AreEqual(0f, Handler().CanProcess("alpha", true,  new PlainPage()));
        Assert.AreEqual(0f, Handler().CanProcess("alpha", false, new PlainPage()));
        Assert.AreEqual(0f, Handler().CanProcess("alpha", true,  null));
    }

    [TestMethod]
    public void Prefixed_WinsOutright_WhateverThePageScores()
    {
        var page = new Page { Score = 0f };

        // The user typed "?" — that is the point of the symbol, so the page's own scoring is bypassed.
        Assert.AreEqual(1f, Handler().CanProcess("anything at all", true, page));
    }

    [TestMethod]
    public void Prefixed_EmptyQuery_IsRejected()
        => Assert.AreEqual(0f, Handler().CanProcess("   ", true, new Page()));

    [TestMethod]
    public void BareProse_IsLeftToTheAgent()
    {
        var page = new Page { Score = 0.9f };

        // Long natural language reaches the agent, which can still search via the page's tools.
        Assert.AreEqual(0f, Handler().CanProcess(
            "do you think you can find the office documents mentioning george", false, page));
    }

    [TestMethod]
    public void BareShortInput_UsesThePagesOwnScore()
        => Assert.AreEqual(0.6f, Handler().CanProcess("budget report", false, new Page { Score = 0.6f }));

    [TestMethod]
    public void PageScore_IsClampedIntoRange()
    {
        Assert.AreEqual(1f, Handler().CanProcess("alpha", false, new Page { Score = 4f }));
        Assert.AreEqual(0f, Handler().CanProcess("alpha", false, new Page { Score = -2f }));
    }

    // ── ProcessAsync ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DelimitedPattern_ReachesThePageAsRegex()
    {
        var page = new Page();

        await Handler().ProcessAsync(@"/alpha\d+/c", true, page);

        Assert.IsTrue(page.LastRequest!.IsRegex);
        Assert.AreEqual(@"alpha\d+", page.LastRequest.Text);
        Assert.IsTrue(page.LastRequest.MatchCase);
    }

    [TestMethod]
    public async Task PlainInput_ReachesThePageAsLiteral()
    {
        var page = new Page();

        await Handler().ProcessAsync("config.json", true, page);

        Assert.IsFalse(page.LastRequest!.IsRegex);
        Assert.AreEqual("config.json", page.LastRequest.Text);
        Assert.AreEqual(1, page.LastRequest.Terms.Count);
    }

    [TestMethod]
    public async Task SeveralWords_BecomeSeveralAndedTerms()
    {
        var page = new Page();

        // "config.json cache" means both must appear — not that the phrase does. Quoting is how a user
        // asks for the phrase.
        await Handler().ProcessAsync("config.json cache", true, page);

        Assert.AreEqual(2, page.LastRequest!.Terms.Count);
        CollectionAssert.AreEqual(
            new[] { "config.json", "cache" },
            page.LastRequest.Terms.Select(t => t.Value).ToArray());
    }

    [TestMethod]
    public async Task QuotedInput_StaysOnePhrase()
    {
        var page = new Page();

        await Handler().ProcessAsync("\"config.json cache\"", true, page);

        Assert.AreEqual(1, page.LastRequest!.Terms.Count);
        Assert.AreEqual("config.json cache", page.LastRequest.Text);
    }

    [TestMethod]
    public async Task PageRecognizers_DecideWhichTermKindsItCanReceive()
    {
        // The page offers none, so glob syntax arrives as ordinary text it can actually honour rather than
        // a name-scoped term it would have to refuse.
        var page = new Page();

        await Handler().ProcessAsync("*.txt", true, page);

        Assert.IsFalse(page.LastRequest!.HasNameOnlyTerms);
    }

    [TestMethod]
    public async Task UserDrivenSearch_AsksThePageToDisplay()
    {
        var page = new Page();

        await Handler().ProcessAsync("alpha", true, page);

        Assert.IsTrue(page.LastDisplay, "a search the user typed must light up the page's own search UI");
    }

    [TestMethod]
    public async Task SuccessfulSearch_SaysNothingInChat()
    {
        var page = new Page { Result = SearchOutcome.Found([new SearchHit("1", "line 2")]) };

        // The page's own results are the feedback; a chat message would just be noise.
        Assert.IsNull(await Handler().ProcessAsync("alpha", true, page));
    }

    [TestMethod]
    public async Task FailedSearch_SurfacesTheReason()
    {
        var page = new Page { Result = SearchOutcome.Unsupported("This page can't run regular expressions.") };

        var reply = await Handler().ProcessAsync(@"/alpha\d+/", true, page);

        StringAssert.Contains(reply!, "can't run regular expressions");
    }

    [TestMethod]
    public async Task NonSearchablePage_ExplainsItself()
        => StringAssert.Contains(
            (await Handler().ProcessAsync("alpha", true, new PlainPage()))!, "doesn't support searching");
}
