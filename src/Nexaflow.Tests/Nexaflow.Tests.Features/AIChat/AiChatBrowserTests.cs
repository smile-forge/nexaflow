using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// The AI Chat browser: the list of saved conversations, the date filter over it, the row actions, and the
/// analysis overlay.
/// <para>
/// The filter is the part worth pinning. It keys on <i>last activity</i>, not start time — a conversation
/// begun three weeks ago and used this morning belongs in "Last 7 days", and filtering on start would hide
/// exactly the conversation the user is most likely reaching for. Delete is the only destructive action
/// here, so it is tested at the confirmation.
/// </para>
/// </summary>
[TestClass]
public class AiChatBrowserTests
{
    private IAIService _ai = null!;
    private IShellServices _shell = null!;

    [TestInitialize]
    public void Init()
    {
        _ai = Substitute.For<IAIService>();
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>())
              .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    private static ConversationRecord Record(string id, int daysSinceLastActivity) => new()
    {
        Id = id,
        Messages =
        [
            new ConversationMessage
            {
                Text = $"message in {id}",
                IsUser = true,
                Timestamp = DateTime.Now.AddDays(-daysSinceLastActivity),
            },
        ],
    };

    private async Task<AiChatViewModel> BrowserOver(params ConversationRecord[] records)
    {
        _ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>(records));
        var vm = new AiChatViewModel(_ai, _shell, new AiChatConfig(), new Page());
        await vm.RefreshAsync();
        return vm;
    }

    // ── The list and its filter ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-browser-list")]
    public async Task ConversationsAreListedNewestFirst()
    {
        var vm = await BrowserOver(Record("old", 3), Record("newest", 0), Record("middle", 1));

        CollectionAssert.AreEqual(new[] { "newest", "middle", "old" },
                                  vm.Items.Select(i => i.Record.Id).ToArray(),
                                  "the browser is a recency list — the one you want is at the top");
    }

    [TestMethod]
    [CoversNode("aichat-browser-filter")]
    public async Task TheFilterKeepsAConversationThatIsStillBeingUsed_HoweverOldItIs()
    {
        // Started three weeks ago, last spoken to today. Filtering on start time would hide it.
        var vm = await BrowserOver(Record("long-running", daysSinceLastActivity: 0),
                                   Record("finished", daysSinceLastActivity: 30));

        Assert.AreEqual("Last 7 days", vm.SelectedFilter, "the default window");
        CollectionAssert.AreEqual(new[] { "long-running" }, vm.Items.Select(i => i.Record.Id).ToArray());
    }

    [TestMethod]
    [CoversNode("aichat-browser-filter")]
    public async Task WideningTheFilterBringsTheOlderOnesBack()
    {
        var vm = await BrowserOver(Record("recent", 0), Record("older", 30));

        Assert.AreEqual(1, vm.Items.Count);

        vm.SelectedFilter = "This year";

        Assert.AreEqual(2, vm.Items.Count, "changing the filter re-runs it without a manual refresh");
    }

    [TestMethod]
    [CoversNode("aichat-browser-filter")]
    public async Task NarrowingToTodayExcludesYesterday()
    {
        var vm = await BrowserOver(Record("today", 0), Record("yesterday", 2));

        vm.SelectedFilter = "Today";

        CollectionAssert.AreEqual(new[] { "today" }, vm.Items.Select(i => i.Record.Id).ToArray());
    }

    // ── Row actions ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-new")]
    public async Task NewConversationOpensAnEmptyTab_WithNoIdToLoad()
    {
        var opened = new List<Dictionary<string, string>>();
        _shell.When(s => s.OpenTab("Conversation", Arg.Any<Dictionary<string, string>>()))
              .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        var vm = await BrowserOver();

        vm.NewConversationCommand.Execute(null);

        Assert.AreEqual(0, opened.Single().Count,
                        "a blank conversation has no record yet — it is persisted on its first exchange");
    }

    [TestMethod]
    [CoversNode("aichat-row-actions")]
    public async Task OpeningARowLoadsThatConversation()
    {
        var opened = new List<Dictionary<string, string>>();
        _shell.When(s => s.OpenTab("Conversation", Arg.Any<Dictionary<string, string>>()))
              .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        var vm = await BrowserOver(Record("c1", 0));

        vm.OpenConversationCommand.Execute(vm.Items.Single());

        Assert.AreEqual("c1", opened.Single()["conversationId"]);
    }

    [TestMethod]
    [CoversNode("aichat-row-actions")]
    public async Task DecliningTheDeleteConfirmationKeepsTheConversation()
    {
        var vm = await BrowserOver(Record("c1", 0));

        // Decline: run the cancel callback (arg 3), not the confirm one (arg 2).
        _shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(),
                                            Arg.Any<Action>(), Arg.Any<Action>()))
              .Do(ci => ((Action)ci[3]).Invoke());

        vm.DeleteConversationCommand.Execute(vm.Items.Single());

        Assert.AreEqual(1, vm.Items.Count, "a declined delete must leave the row alone");
        _ = _ai.DidNotReceiveWithAnyArgs().DeleteConversationAsync(default!);
    }

    [TestMethod]
    [CoversNode("aichat-row-actions")]
    public async Task ConfirmingTheDeleteRemovesItFromDiskAndFromTheList()
    {
        var vm = await BrowserOver(Record("c1", 0), Record("c2", 0));
        _shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(),
                                            Arg.Any<Action>(), Arg.Any<Action>()))
              .Do(ci => ((Action)ci[2]).Invoke());

        vm.DeleteConversationCommand.Execute(vm.Items.First(i => i.Record.Id == "c1"));

        await _ai.Received().DeleteConversationAsync("c1");
        CollectionAssert.AreEqual(new[] { "c2" }, vm.Items.Select(i => i.Record.Id).ToArray());
    }

    // ── Analysis overlay ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-analysis-sections")]
    public async Task TheAnalysisOverlayOpensOnARowAndClosesAgain()
    {
        var vm = await BrowserOver(Record("c1", 0));
        Assert.IsFalse(vm.IsAnalysisOverlayOpen);

        vm.ShowAnalysisCommand.Execute(vm.Items.Single());
        Assert.IsTrue(vm.IsAnalysisOverlayOpen);
        Assert.AreEqual("c1", vm.AnalysisOverlayItem!.Record.Id);

        vm.CloseAnalysisCommand.Execute(null);
        Assert.IsFalse(vm.IsAnalysisOverlayOpen);
    }

    [TestMethod]
    [CoversNode("aichat-analysis-sections")]
    public async Task ChangingTheFilterClosesTheOverlay_RatherThanLeavingItOverADroppedRow()
    {
        var vm = await BrowserOver(Record("recent", 0), Record("older", 30));
        vm.SelectedFilter = "This year";
        vm.ShowAnalysisCommand.Execute(vm.Items.First(i => i.Record.Id == "older"));
        Assert.IsTrue(vm.IsAnalysisOverlayOpen);

        vm.SelectedFilter = "Today";

        Assert.IsFalse(vm.IsAnalysisOverlayOpen,
                       "the overlay was describing a row the list no longer contains");
    }
}
