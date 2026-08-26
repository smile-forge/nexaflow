using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The conversation browser answering <c>?</c> over three saved conversations.
/// <para>
/// Two things beyond the shared contract. A conversation is searched by <em>what was said in it</em>, not
/// only by its title — the title is a generated slug nobody remembers. And the search ignores the date
/// filter: it defaults to the last 7 days, so searching within it would quietly answer "did we discuss
/// this recently", and its empty result would read as "we never discussed this".
/// </para>
/// </summary>
[TestClass]
[CoversNode("aichat-search")]
public class AiChatSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    /// <summary>A conversation id that was never saved.</summary>
    protected override SearchHit UnknownHit => new("nowhere", "not on this page");

    private const string ByTitle   = "by-title";     // the term is in its title, active today
    private const string ByMessage = "by-message";   // the term is in a message — and it is a YEAR old
    private const string Quiet     = "quiet";

    private IAIService _ai = null!;
    private IShellServices _shell = null!;

    [TestInitialize]
    public void Init()
    {
        _ai = Substitute.For<IAIService>();
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>())
              .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        _shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>())
              .Returns(ci => ci.Arg<Func<Task<SearchOutcome>>>()());
        _shell.RunOnUiAsync(Arg.Any<Func<Task<bool>>>())
              .Returns(ci => ci.Arg<Func<Task<bool>>>()());
    }

    private static ConversationRecord Record(string id, string title, string message, int daysAgo) => new()
    {
        Id        = id,
        Title     = title,
        StartedAt = DateTime.Now.AddDays(-daysAgo),
        Messages  =
        [
            new ConversationMessage
            {
                Text      = message,
                IsUser    = true,
                Timestamp = DateTime.Now.AddDays(-daysAgo),
            },
        ],
    };

    private async Task<AiChatViewModel> BuildAsync()
    {
        _ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>(
        [
            Record(ByTitle,   "alpha42-notes-a1b2c", "just checking in",           0),
            Record(ByMessage, "chat-session-d3e4f",  "we should use alpha42 here", 365),
            Record(Quiet,     "ledger-review-g5h6i", "nothing to see",             0),
        ]));

        var vm = new AiChatViewModel(_ai, _shell, new AiChatConfig(), new Page());
        await vm.RefreshAsync();
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await BuildAsync();

    protected override string Snapshot(ISearchable page)
    {
        var vm = (AiChatViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|{vm.SelectedFilter}|" +
               string.Join(",", Listed(vm));
    }

    private static string[] Listed(AiChatViewModel vm) =>
        vm.Items.Select(i => i.Record.Id).ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    private static string[] Ids(SearchOutcome outcome) =>
        outcome.Hits.Select(h => h.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ── Browser-specific behaviour beyond the shared contract ─────────────────

    [TestMethod]
    public void AConversationMatchesOnItsTitle_OrOnWhatWasSaidInIt() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { ByMessage, ByTitle }, Ids(outcome),
            "the title is a generated slug — what people remember is what was said");
    });

    [TestMethod]
    public void TheSearchReachesPastTheDateFilter() => WithPage(async page =>
    {
        var vm = (AiChatViewModel)page;
        Assert.AreEqual("Last 7 days", vm.SelectedFilter);
        Assert.IsFalse(Listed(vm).Contains(ByMessage), "the year-old conversation is outside the filter");

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEquivalent(new[] { ByTitle, ByMessage }, Listed(vm),
            "searching inside the default 7-day window would answer \"did we discuss this recently\" " +
            "and read as \"we never discussed this\"");
    });

    [TestMethod]
    public void TheHitPreviewNamesTheMessageThatMatched() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        var byMessage = outcome.Hits.Single(h => h.Id == ByMessage);
        StringAssert.Contains(byMessage.Preview ?? "", "we should use alpha42 here",
            "previewing the conversation's first line instead would make every hit read the same");
    });

    [TestMethod]
    public void ChangingTheDateFilter_DropsTheSearch() => WithPage(async page =>
    {
        var vm = (AiChatViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);
        Assert.IsTrue(vm.IsSearchActive);

        vm.SelectedFilter = "Today";

        Assert.IsFalse(vm.IsSearchActive, "picking a date range is a different question");
        CollectionAssert.AreEquivalent(new[] { ByTitle, Quiet }, Listed(vm));
    });

    [TestMethod]
    public void ClearSearch_HandsTheDateFilterBack() => WithPage(async page =>
    {
        var vm = (AiChatViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        CollectionAssert.AreEquivalent(new[] { ByTitle, Quiet }, Listed(vm),
            "back to the last 7 days — the year-old one is out of range again");
    });

    [TestMethod]
    public void ShowResults_ListsOnlyTheChosenConversations() => WithPage(async page =>
    {
        var vm = (AiChatViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == ByMessage);

        var narrowed = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(narrowed);
        CollectionAssert.AreEqual(new[] { ByMessage }, Listed(vm));
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

}
