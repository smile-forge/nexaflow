using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.AIChat.ViewModels.Timeline;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// One open conversation answering <c>?</c>.
/// <para>
/// What is worth pinning beyond the shared contract: this page <em>marks</em> and never filters — a thread
/// is read in order, so hiding the messages that missed would leave replies with nothing to reply to — and
/// both sides of the conversation are searched, since half of what was said came from the assistant.
/// Stepping asks the view to scroll, and asks again even when the same hit comes round twice.
/// </para>
/// </summary>
[TestClass]
[CoversNode("aichat-conversation-search")]
public class ConversationSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    /// <summary>A message position past the end of this thread. The conformance default is a GUID, which this page
    /// can reject on shape alone — that would prove nothing about the position lookup.</summary>
    protected override SearchHit UnknownHit => new("99", "not on this page");

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

    //  0  you       : "how do we wire alpha42 in"   ← hit
    //  1  assistant : "alpha42 goes in the header"  ← hit (the assistant's half counts too)
    //  2  you       : "thanks"
    //  3  assistant : "any time"
    private static ConversationMessage User(string text, int minutesAgo)
        => new() { Text = text, IsUser = true, Timestamp = DateTime.Now.AddMinutes(-minutesAgo) };

    private static ConversationMessage Assistant(string text, int minutesAgo)
        => new() { Text = text, IsUser = false, Timestamp = DateTime.Now.AddMinutes(-minutesAgo) };

    private async Task<ConversationViewModel> BuildAsync()
    {
        var record = new ConversationRecord
        {
            Id = "c1",
            Messages =
            [
                User("how do we wire alpha42 in", 40),
                Assistant("alpha42 goes in the header", 39),
                User("thanks", 20),
                Assistant("any time", 19),
            ],
        };
        _ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>([record]));

        var vm = new ConversationViewModel(_ai, _shell, new AiChatConfig(), new Page());
        await vm.LoadAsync("c1");
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await BuildAsync();

    protected override string Snapshot(ISearchable page)
    {
        var vm = (ConversationViewModel)page;
        return $"{vm.IsSearchActive}|{vm.SearchMatchCount}|{vm.CurrentSearchTerm}|" +
               $"{vm.ScrollToTimelineIndex}|{string.Join(",", Marked(vm))}";
    }

    private static int[] Marked(ConversationViewModel vm) =>
        vm.Timeline.Select((item, i) => (item, i))
                   .Where(x => x.item switch
                   {
                       TimelineUserMessage u      => u.IsSearchHit,
                       TimelineAssistantMessage a => a.IsSearchHit,
                       _                          => false,
                   })
                   .Select(x => x.i)
                   .ToArray();

    private static SearchRequest Query(string text) => SearchSyntax.ParseRequest(text);

    // ── Conversation-specific behaviour beyond the shared contract ────────────

    [TestMethod]
    public void BothSidesOfTheConversationAreSearched() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { "0", "1" }, outcome.Hits.Select(h => h.Id).ToArray(),
            "half of what was said came from the assistant");
        CollectionAssert.AreEqual(new[] { "you", "assistant" }, outcome.Hits.Select(h => h.Label).ToArray());
    });

    [TestMethod]
    public void DisplayingSearch_MarksTheHits_AndLeavesEveryMessageInTheThread() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        var before = vm.Timeline.Count;

        await vm.SearchAsync(Query("alpha42"), display: true, default);

        CollectionAssert.AreEqual(new[] { 0, 1 }, Marked(vm));
        Assert.AreEqual(before, vm.Timeline.Count,
            "hiding what missed would leave replies with nothing to reply to");
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.AreEqual(0, vm.ScrollToTimelineIndex, "and the thread moves to the first hit");
    });

    [TestMethod]
    public void FindNext_WalksEveryHit_AndWraps() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(1, vm.ScrollToTimelineIndex);

        vm.FindNextMatchCommand.Execute(null);
        Assert.AreEqual(0, vm.ScrollToTimelineIndex, "then wraps back to the first");
    });

    [TestMethod]
    public void SteppingOntoTheSameHitTwice_StillAsksTheThreadToMove() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        await vm.SearchAsync(Query("wire"), display: true, default);
        Assert.AreEqual(1, vm.SearchMatchCount, "one hit, so 'next' lands on it again");

        var raised = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.ScrollToTimelineIndex)) raised++;
        };

        vm.FindNextMatchCommand.Execute(null);

        Assert.IsTrue(raised >= 2,
            "the index is reset to -1 first — without it the same value raises nothing and the thread sits still");
        Assert.AreEqual(0, vm.ScrollToTimelineIndex);
    });

    [TestMethod]
    public void ToolBatchesAndNoticesAreNotMessages() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        vm.BeginResponse("say nothing");
        vm.ShowFinal("   ");   // an empty answer renders a notice, not a bubble
        Assert.IsTrue(vm.Timeline.OfType<TimelineNotice>().Any(), "the notice really is in the thread");

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        CollectionAssert.AreEqual(new[] { "0", "1" }, outcome.Hits.Select(h => h.Id).ToArray(),
            "a notice or a tool batch is machinery, not something that was said");
    });

    [TestMethod]
    public void ClearSearch_DropsEveryMark() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        await vm.SearchAsync(Query("alpha42"), display: true, default);

        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.AreEqual(0, Marked(vm).Length);
        Assert.AreEqual(-1, vm.ScrollToTimelineIndex);
    });

    [TestMethod]
    public void ShowResults_MarksOnlyTheMessagesTheAgentChose() => WithPage(async page =>
    {
        var vm = (ConversationViewModel)page;
        var found = await vm.SearchAsync(Query("alpha42"), display: false, default);
        var chosen = found.Hits.Single(h => h.Id == "1");

        var marked = await vm.ShowResultsAsync([chosen], default);

        Assert.IsTrue(marked);
        CollectionAssert.AreEqual(new[] { 1 }, Marked(vm));
        Assert.AreEqual(1, vm.ScrollToTimelineIndex);
    });

    [TestMethod]
    public void AnEmptyConversation_SaysSo_RatherThanReportingNoMatches() => RunUnpumped(async () =>
    {
        var vm = new ConversationViewModel(_ai, _shell, new AiChatConfig(), new Page());

        var outcome = await vm.SearchAsync(Query("alpha42"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount);
        Assert.IsFalse(outcome.Failed);
        StringAssert.Contains(outcome.Message ?? "", "no messages");
    });
}
