using NSubstitute;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.AIChat.ViewModels.Timeline;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// The conversation thread: what a turn renders, what it persists, and rewinding out of it.
/// </summary>
[TestClass]
public class ConversationTimelineTests
{
    private IAIService     _ai    = null!;
    private IShellServices _shell = null!;

    [TestInitialize]
    public void Init()
    {
        _ai    = Substitute.For<IAIService>();
        _shell = Substitute.For<IShellServices>();

        // The substitute's default drops the action on the floor, which would no-op every UI-marshalled
        // path in the view-model (BeginResponse, ShowFinal, Abort…). Run it inline instead.
        _shell.RunOnUiAsync(Arg.Any<Action>())
              .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    private ConversationViewModel NewConversation()
        => new(_ai, _shell, new AiChatConfig(), new Page());

    private async Task<ConversationViewModel> LoadedAsync(params ConversationMessage[] messages)
    {
        var record = new ConversationRecord { Id = "c1", Messages = [.. messages] };
        _ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>([record]));

        var convo = NewConversation();
        await convo.LoadAsync("c1");
        return convo;
    }

    private static ConversationMessage User(string text, int minutesAgo)
        => new() { Text = text, IsUser = true, Timestamp = DateTime.Now.AddMinutes(-minutesAgo) };

    private static ConversationMessage Assistant(string text, int minutesAgo)
        => new() { Text = text, IsUser = false, Timestamp = DateTime.Now.AddMinutes(-minutesAgo) };

    // ── An empty final answer ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-thread")]
    public async Task ShowFinal_EmptyAnswer_RendersANoticeInsteadOfABlankBubble()
    {
        var convo = await LoadedAsync();

        convo.BeginResponse("do something");
        convo.ShowFinal("   ");

        // A model can end a turn saying nothing (most often after a denied tool batch). It used to render
        // as an empty bubble, which reads as a broken UI.
        Assert.IsFalse(convo.Timeline.OfType<TimelineAssistantMessage>().Any(),
            "an empty answer was rendered as an assistant bubble");
        Assert.IsTrue(convo.Timeline.OfType<TimelineNotice>().Any(),
            "an empty answer should be reported as a notice");
    }

    [TestMethod]
    [CoversNode("aichat-thread")]
    public async Task ShowFinal_EmptyAnswer_DoesNotPersistAnEmptyMessage()
    {
        var convo = await LoadedAsync();

        convo.BeginResponse("do something");
        convo.ShowFinal("");

        // The user's message happened, so it's kept; inventing an assistant message to pair with it wouldn't
        // be true, and an empty one would sit in the transcript for ever.
        var persisted = convo.Conversation!.Messages;
        Assert.AreEqual(1, persisted.Count);
        Assert.IsTrue(persisted[0].IsUser);
    }

    [TestMethod]
    [CoversNode("aichat-thread")]
    public async Task ShowFinal_RealAnswer_RendersAndPersistsIt()
    {
        var convo = await LoadedAsync();

        convo.BeginResponse("do something");
        convo.ShowFinal("here you go");

        Assert.AreEqual(1, convo.Timeline.OfType<TimelineAssistantMessage>().Count());
        Assert.AreEqual(2, convo.Conversation!.Messages.Count);
    }

    // ── Timestamps + the rewind affordance ────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task OnlyTheNewestUserMessageOffersRewind()
    {
        var convo = await LoadedAsync(
            User("first", 30), Assistant("a1", 29),
            User("second", 5), Assistant("a2", 4));

        var users = convo.Timeline.OfType<TimelineUserMessage>().ToList();

        Assert.AreEqual(2, users.Count);
        Assert.IsFalse(users[0].IsLast);
        Assert.IsTrue(users[1].IsLast, "rewind belongs on the newest user message only");
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindTo_TruncatesTheThreadAndTheRecord()
    {
        var convo = await LoadedAsync(
            User("first", 30), Assistant("a1", 29),
            User("second", 5), Assistant("a2", 4));

        var last = convo.Timeline.OfType<TimelineUserMessage>().Last();
        await convo.RewindToCommand.ExecuteAsync(last);

        // "second" and everything after it is gone — from the thread, the token count and the saved record.
        CollectionAssert.AreEqual(new[] { "first", "a1" }, convo.Messages.Select(m => m.Text).ToList());
        CollectionAssert.AreEqual(new[] { "first", "a1" }, convo.Conversation!.Messages.Select(m => m.Text).ToList());
        Assert.AreEqual(2, convo.Timeline.Count);
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindTo_HandsTheTextBackToTheAiInput()
    {
        var convo = await LoadedAsync(User("first", 30), Assistant("a1", 29), User("second", 5));

        var last = convo.Timeline.OfType<TimelineUserMessage>().Last();
        await convo.RewindToCommand.ExecuteAsync(last);

        _shell.Received(1).InsertChatInput("second");
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindTo_PersistsTheTruncation()
    {
        var convo = await LoadedAsync(User("first", 30), Assistant("a1", 29), User("second", 5));

        var last = convo.Timeline.OfType<TimelineUserMessage>().Last();
        await convo.RewindToCommand.ExecuteAsync(last);

        await _ai.Received().SaveConversationAsync(Arg.Is<ConversationRecord>(r => r.Messages.Count == 2));
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindTo_AfterRewind_ThePreviousMessageBecomesTheRewindPoint()
    {
        var convo = await LoadedAsync(User("first", 30), Assistant("a1", 29), User("second", 5));

        await convo.RewindToCommand.ExecuteAsync(convo.Timeline.OfType<TimelineUserMessage>().Last());

        var remaining = convo.Timeline.OfType<TimelineUserMessage>().Single();
        Assert.AreEqual("first", remaining.Text);
        Assert.IsTrue(remaining.IsLast);
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindTo_WhileTheAgentIsRunning_IsRefused()
    {
        var convo = await LoadedAsync(User("first", 30), Assistant("a1", 29));

        convo.BeginResponse("second");           // a turn is now in flight
        var last = convo.Timeline.OfType<TimelineUserMessage>().Last();

        await convo.RewindToCommand.ExecuteAsync(last);

        // A running agent owns the message list through its pending-turn bookkeeping; truncating underneath
        // it would leave the turn writing into a conversation that no longer exists.
        Assert.AreEqual(3, convo.Messages.Count);
        _shell.DidNotReceive().InsertChatInput(Arg.Any<string>());
    }

    [TestMethod]
    [CoversNode("aichat-rewind")]
    public async Task RewindIsNotOfferedWhileTheAgentIsRunning()
    {
        var convo = await LoadedAsync(User("first", 30), Assistant("a1", 29));

        convo.BeginResponse("second");

        Assert.IsFalse(convo.Timeline.OfType<TimelineUserMessage>().Any(u => u.IsLast),
            "no user message should offer rewind mid-turn");
    }
}
