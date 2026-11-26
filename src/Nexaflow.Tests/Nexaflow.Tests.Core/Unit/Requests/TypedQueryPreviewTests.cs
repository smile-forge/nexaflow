using Nexaflow.Assistant.Requests;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit.Requests;

/// <summary>
/// As the user types into the AI bar, the words are scored once typing pauses: the handler that clearly owns them shows its symbol
/// and offers its completion; words no handler owns leave the assistant listening. A completion that arrives after the words changed
/// is dropped, and an empty bar points at nothing.
/// </summary>
[TestClass]
[CoversNode("aibar-input-group")]
public class TypedQueryPreviewTests
{
    /// <summary>A timer the test fires by hand: the pause has passed.</summary>
    private sealed class HandTimer(Action tick) : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Restart() => IsRunning = true;
        public void Dispose() => IsRunning = false;

        public void Elapse()
        {
            Assert.IsTrue(IsRunning, "the pause is timed only while the words wait to be scored");
            IsRunning = false;
            tick();
        }
    }

    private sealed class HandUi : IUiThread
    {
        public HandTimer? Timer;
        public bool IsCurrent => true;
        public void Run(Action action) => action();
        public T Run<T>(Func<T> func) => func();
        public Task RunAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> RunAsync<T>(Func<Task<T>> action) => action();
        public void Post(Action action) => action();
        public void PostInBackground(Action action) => action();
        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once) => Timer = new HandTimer(tick) { Interval = interval };
    }

    private static (IAIService Assistant, HandUi Ui, TypedQueryPreview Preview) Setup()
    {
        var assistant = Substitute.For<IAIService>();
        assistant.ScoreHandlers(Arg.Any<string>(), Arg.Any<IPageViewModel?>()).Returns(ci => ([], null, ci.ArgAt<string>(0), false));
        var ui = new HandUi();
        return (assistant, ui, new TypedQueryPreview(ui, () => assistant, () => null));
    }

    private static IQueryHandler Owner(string symbol, Task<string?> completion)
    {
        var handler = Substitute.For<IQueryHandler>();
        handler.Symbol.Returns(symbol);
        handler.CompleteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IPageViewModel?>()).Returns(completion);
        return handler;
    }

    [TestMethod]
    public void TheWords_AreScoredOnlyOnceTypingPauses()
    {
        var (assistant, ui, preview) = Setup();

        preview.Update("sum 4");
        preview.Update("sum 40");

        assistant.DidNotReceiveWithAnyArgs().ScoreHandlers(default!, default);
        ui.Timer!.Elapse();
        assistant.Received(1).ScoreHandlers("sum 40", null);
    }

    [TestMethod]
    public void WordsAHandlerOwns_ShowItsSymbol_AndItsCompletion()
    {
        var (assistant, ui, preview) = Setup();
        var owner = Owner(">", Task.FromResult<string?>("git status"));
        assistant.ScoreHandlers("git st", null).Returns(([(owner, 1f)], owner, "git st", false));

        preview.Update("git st");
        ui.Timer!.Elapse();

        Assert.AreEqual(new QueryPreview(">", Listening: false, Completion: "git status"), preview.Current);
    }

    [TestMethod]
    public void WordsNoHandlerOwns_LeaveTheAssistantListening()
    {
        var (_, ui, preview) = Setup();

        preview.Update("why is the sky blue");
        ui.Timer!.Elapse();

        Assert.AreEqual(new QueryPreview(null, Listening: true, Completion: null), preview.Current);
    }

    [TestMethod]
    public void ACompletionThatArrivesAfterTheWordsChanged_IsDropped()
    {
        var (assistant, ui, preview) = Setup();
        var completing = new TaskCompletionSource<string?>();
        var owner = Owner(">", completing.Task);
        assistant.ScoreHandlers("git st", null).Returns(([(owner, 1f)], owner, "git st", false));

        preview.Update("git st");
        ui.Timer!.Elapse();
        preview.Update("git sta");
        completing.SetResult("git status");

        Assert.IsNull(preview.Current.Completion, "the completion was for words no longer in the bar");
        Assert.AreEqual(">", preview.Current.Symbol, "the symbol stays until the new words are scored");
    }

    [TestMethod]
    public void AnEmptyBar_PointsAtNothing_AndNothingIsScored()
    {
        var (assistant, ui, preview) = Setup();
        var changes = 0;
        preview.Changed += (_, _) => changes++;

        preview.Update("why");
        ui.Timer!.Elapse();
        preview.Update("");

        Assert.AreEqual(QueryPreview.None, preview.Current);
        Assert.IsFalse(ui.Timer.IsRunning);
        Assert.AreEqual(2, changes, "listening, then nothing");
        assistant.Received(1).ScoreHandlers(Arg.Any<string>(), Arg.Any<IPageViewModel?>());
    }
}
