using Nexaflow.Assistant.Requests;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Localization;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Nexaflow.Tests.Core.Unit.Requests;

/// <summary>
/// What the AI bar sends is answered by the query handler that clearly owns it, else by the handler the model picks, else by the
/// agent; on the page when the page shows the assistant itself, in the bar otherwise. Words sent during a run join it and those left
/// over run after it; the user's cancel offers their words again; a failure is reported and ends the run; and a request nothing could
/// answer says why.
/// </summary>
[TestClass]
[CoversNode("chat-interface")]
public class AssistantRequestsTests
{
    /// <summary>Where a reply is shown: what it was asked to show, and the words queued to join the run.</summary>
    private class Surface : IAIResponseHandler
    {
        public readonly List<string> Begun = [];
        public readonly List<string> Final = [];
        public readonly List<string> Progress = [];
        public readonly List<string> Queued = [];
        public int Aborted;

        public virtual string? ConversationName => null;
        public void BeginResponse(string userText) => Begun.Add(userText);
        public void ReportProgress(string message) => Progress.Add(message);
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
        public void ShowFinal(string finalMarkdown) => Final.Add(finalMarkdown);
        public void Abort() => Aborted++;
        public void Enqueue(string text) => Queued.Add(text);

        public IReadOnlyList<string> DrainQueue()
        {
            var drained = Queued.ToList();
            Queued.Clear();
            return drained;
        }
    }

    /// <summary>A page that shows the assistant itself, holding a conversation of its own.</summary>
    private sealed class ConversationPage : Surface, IPageViewModel
    {
        public override string? ConversationName => "chat-3";
        public string GetContext() => "A conversation.";
    }

    private sealed class Page : IPageViewModel
    {
        public string GetContext() => "A page.";
    }

    private sealed class Bar : IAssistantBar
    {
        public readonly Surface Shown = new();
        public readonly AgentRunOptions Options = AgentRunOptions.Typed with { Conversation = "bar" };
        public readonly List<string> Prefilled = [];
        public IReadOnlyList<string> TouchedPaths = [];

        public IAIResponseHandler Surface => Shown;
        public bool IncludeContext => true;
        public AgentRunOptions RunOptions() => Options;
        public Task PrefillAsync(string text) { Prefilled.Add(text); return Task.CompletedTask; }
        public void Touched(IReadOnlyList<string> paths) => TouchedPaths = paths;
    }

    private static (IAnswersRequests Assistant, Bar Bar, AssistantRequests Requests) Setup()
    {
        var assistant = Substitute.For<IAnswersRequests>();
        assistant.ScoreHandlers(Arg.Any<string>(), Arg.Any<IPageViewModel?>()).Returns(ci => ([], null, ci.ArgAt<string>(0), false));
        assistant.StatusOf(Arg.Any<AiAbility>()).Returns(new AiAbilityStatus(AiAbilityState.Ready));
        var bar = new Bar();
        return (assistant, bar, new AssistantRequests(() => assistant, bar));
    }

    private static IQueryHandler Handler(string? answer)
    {
        var handler = Substitute.For<IQueryHandler>();
        handler.ProcessAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IPageViewModel?>()).Returns(answer);
        return handler;
    }

    [TestMethod]
    public async Task AClearWinner_AnswersInTheBar_WithoutTheAgent()
    {
        var (assistant, bar, requests) = Setup();
        var winner = Handler("42");
        assistant.ScoreHandlers("sum 40 2", Arg.Any<IPageViewModel?>()).Returns(([(winner, 1f)], winner, "sum 40 2", false));

        await requests.SendAsync("sum 40 2", new Page());

        CollectionAssert.AreEqual(new[] { "sum 40 2" }, bar.Shown.Begun);
        CollectionAssert.AreEqual(new[] { "42" }, bar.Shown.Final);
        await assistant.DidNotReceiveWithAnyArgs().RunAgentAsync(default, default!, default, default!, default!, default);
    }

    [TestMethod]
    public async Task AClearWinnerWithNothingToSay_NeverShowsTheBar()
    {
        var (assistant, bar, requests) = Setup();
        var search = Handler(null);
        assistant.ScoreHandlers("?report", Arg.Any<IPageViewModel?>()).Returns(([(search, 1f)], search, "report", true));

        await requests.SendAsync("?report", new Page());

        Assert.AreEqual(0, bar.Shown.Begun.Count, "a handler that shows its own result never puts the AI's surface up");
        await search.Received(1).ProcessAsync("report", true, Arg.Any<IPageViewModel?>());
    }

    [TestMethod]
    public async Task WithNoClearWinner_TheHandlerTheModelPicks_Answers()
    {
        var (assistant, bar, requests) = Setup();
        var listing = Handler("3 files");
        assistant.ScoreHandlers("list them", Arg.Any<IPageViewModel?>()).Returns(([(listing, 0.4f)], null, "list them", false));
        assistant.DisambiguateToolSelection(Arg.Any<IPageViewModel?>(), "list them", Arg.Any<IReadOnlyList<(IQueryHandler, float)>>()).Returns(listing);

        await requests.SendAsync("list them", new Page());

        CollectionAssert.AreEqual(new[] { "3 files" }, bar.Shown.Final);
        await assistant.DidNotReceiveWithAnyArgs().RunAgentAsync(default, default!, default, default!, default!, default);
    }

    [TestMethod]
    public async Task WithNoHandler_TheAgentAnswersInTheBar_AsATurnOfTheBarsConversation()
    {
        var (assistant, bar, requests) = Setup();
        var page = new Page();
        assistant.RunAgentAsync(page, "why", true, bar.Shown, bar.Options, Arg.Any<CancellationToken>())
                 .Returns(AiResponse.AsMessage("because", [@"C:\a.txt"]));

        await requests.SendAsync("why", page);

        await assistant.Received(1).RunAgentAsync(page, "why", true, bar.Shown, bar.Options, Arg.Any<CancellationToken>());
        CollectionAssert.AreEqual(new[] { @"C:\a.txt" }, bar.TouchedPaths.ToArray(), "the files the run touched go to the bar's Continue as Conversation");
        Assert.IsFalse(requests.IsBusy);
    }

    [TestMethod]
    public async Task OnAPageThatShowsTheAssistant_TheTurnIsAnsweredThere_UnderItsConversation()
    {
        var (assistant, bar, requests) = Setup();
        var page = new ConversationPage();
        AgentRunOptions? asked = null;
        assistant.RunAgentAsync(page, "hello", true, page, Arg.Do<AgentRunOptions>(o => asked = o), Arg.Any<CancellationToken>())
                 .Returns(AiResponse.AsMessage("hi"));

        await requests.SendAsync("hello", page);

        CollectionAssert.AreEqual(new[] { "hello" }, page.Begun);
        Assert.AreEqual(0, bar.Shown.Begun.Count);
        Assert.AreEqual("chat-3", asked?.Conversation);
    }

    [TestMethod]
    public async Task WordsSentDuringARun_JoinIt_AndThoseLeftOver_RunAfterIt()
    {
        var (assistant, bar, requests) = Setup();
        var page = new Page();
        var firstEnds = new TaskCompletionSource<AiResponse?>();
        assistant.RunAgentAsync(page, "first", Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns(firstEnds.Task);
        assistant.RunAgentAsync(page, "second", Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns(AiResponse.AsMessage("done"));

        var run = requests.SendAsync("first", page);
        Assert.IsTrue(requests.IsBusy);
        await requests.SendAsync("second", page);   // joins at once

        CollectionAssert.AreEqual(new[] { "second" }, bar.Shown.Queued, "words sent during a run join it");
        firstEnds.SetResult(AiResponse.AsMessage("one"));
        await run;

        await assistant.Received(1).RunAgentAsync(page, "second", Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(requests.IsBusy);
    }

    [TestMethod]
    public async Task TheUsersCancel_OffersTheirWordsAgain()
    {
        var (assistant, _, requests) = Setup();
        assistant.RunAgentAsync(Arg.Any<IPageViewModel?>(), "rename them", Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns(async ci =>
                 {
                     await Task.Delay(Timeout.Infinite, ci.ArgAt<CancellationToken>(5));
                     return (AiResponse?)null;
                 });
        string? offered = null;
        requests.Cancelled += text => offered = text;

        var run = requests.SendAsync("rename them", new Page());
        requests.Cancel();
        await run;

        Assert.AreEqual("rename them", offered);
        Assert.IsFalse(requests.IsBusy);
    }

    [TestMethod]
    public async Task AFailure_IsReported_AndEndsTheRun()
    {
        var (assistant, bar, requests) = Setup();
        assistant.RunAgentAsync(Arg.Any<IPageViewModel?>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .ThrowsAsync(new InvalidOperationException("the model is gone"));
        Exception? reported = null;
        requests.Failed += ex => reported = ex;

        await requests.SendAsync("why", new Page());

        Assert.AreEqual("the model is gone", reported?.Message);
        Assert.AreEqual(1, bar.Shown.Aborted);
        Assert.IsFalse(requests.IsBusy);
    }

    [TestMethod]
    public async Task ARequestNothingCouldAnswer_SaysWhy_WhenTheWorkspacesOwnModelsAreOutOfReach()
    {
        var (assistant, bar, requests) = Setup();
        assistant.StatusOf(AiAbility.GeneralIntelligence).Returns(new AiAbilityStatus(AiAbilityState.Unavailable));
        assistant.OwnModelsUnreachable.Returns("the AI host service has stopped");
        assistant.RunAgentAsync(Arg.Any<IPageViewModel?>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns((AiResponse?)null);

        await requests.SendAsync("why", new Page());

        CollectionAssert.AreEqual(new[] { Str.Format("Shell.AiBar.LocalAiUnreachable", "the AI host service has stopped") }, bar.Shown.Final);
    }

    [TestMethod]
    public async Task AnAnswerThatFillsTheBar_IsTypedIn_AndEndsTheRun()
    {
        var (assistant, bar, requests) = Setup();
        assistant.RunAgentAsync(Arg.Any<IPageViewModel?>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns(AiResponse.AsPrefill("git commit -m \"\""));

        await requests.SendAsync("commit this", new Page());

        CollectionAssert.AreEqual(new[] { "git commit -m \"\"" }, bar.Prefilled);
        Assert.AreEqual(1, bar.Shown.Aborted, "the surface makes way for the words typed into the bar");
    }

    [TestMethod]
    public async Task WorkThatEndsDuringARun_JoinsIt()
    {
        var (assistant, bar, requests) = Setup();
        var page = new Page();
        var firstEnds = new TaskCompletionSource<AiResponse?>();
        assistant.RunAgentAsync(page, "first", Arg.Any<bool>(), Arg.Any<IAIResponseHandler>(), Arg.Any<AgentRunOptions>(), Arg.Any<CancellationToken>())
                 .Returns(firstEnds.Task);

        var run = requests.SendAsync("first", page);
        await requests.PickUpAsync(page, "the picture", "a.png was made");

        Assert.AreEqual(1, bar.Shown.Queued.Count);
        StringAssert.Contains(bar.Shown.Queued[0], "a.png was made");
        firstEnds.SetResult(AiResponse.AsMessage("one"));
        await run;
    }
}
