using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Requests;

/// <summary>
/// The AI bar's answer, as any toolkit's bar shows it: its one line says what the assistant is doing, the approval it waits on,
/// or its reply in plain words; an approval waits for the user's yes or no, and putting the answer away is a no; a reply joins
/// the bar's conversation, and a request that ends with none leaves the last reply in place.
/// </summary>
[TestClass]
[CoversNode("aibar-bar-reply")]
public class BarReplyTests
{
    private sealed class FakeHost : IBarReplyHost
    {
        public WorkspaceRuntime CurrentRuntime { get; } = TestRuntimes.Unstarted(new Workspace { Name = "Bar" });
        public Page? ActiveTab => null;
        public IPageViewModel? ActivePageViewModel => null;
    }

    private sealed class InlineUi : IUiThread
    {
        public bool IsCurrent => true;
        public void Run(Action action) => action();
        public T Run<T>(Func<T> func) => func();
        public Task RunAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> RunAsync<T>(Func<Task<T>> action) => action();
        public void Post(Action action) => action();
        public void PostInBackground(Action action) => action();
        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority, bool once) => throw new NotSupportedException();
    }

    private static BarReply Reply() => new(new InlineUi(), new FakeHost());

    [TestMethod]
    [CoversNode("aibar-progress-line")]
    public void TheLine_SaysWhatIsBeingDone_ThenTheReplyInPlainWords()
    {
        var reply   = Reply();
        var started = 0;
        reply.Started += (_, _) => started++;

        reply.BeginResponse("What is in this folder?");
        Assert.AreEqual(1, started, "the last answer's details are put away");
        Assert.IsTrue(reply.IsOpen);
        Assert.AreEqual(BarReplyStage.Running, reply.Stage);
        StringAssert.EndsWith(reply.Line, "…");

        reply.ShowFinal("Three **pictures** and a note.");
        Assert.AreEqual(BarReplyStage.Message, reply.Stage);
        Assert.AreEqual("Three pictures and a note.", reply.Line);
    }

    [TestMethod]
    [CoversNode("aibar-tool-approval")]
    public async Task AnApproval_WaitsForTheUsersAnswer_AndPuttingItAwayIsANo()
    {
        var reply = Reply();
        reply.BeginResponse("Tidy the folder");

        var asked = reply.RequestToolBatchApprovalAsync("I'd move the pictures.", [], CancellationToken.None);
        Assert.AreEqual(BarReplyStage.Approval, reply.Stage);
        Assert.AreEqual(reply.Question, reply.Line, "the line asks the question");
        Assert.IsFalse(asked.IsCompleted);

        reply.Approve();
        Assert.IsTrue(await asked);
        Assert.AreEqual(BarReplyStage.Running, reply.Stage, "back to work once allowed");

        var again = reply.RequestToolBatchApprovalAsync("", [], CancellationToken.None);
        reply.Dismiss();
        Assert.IsFalse(await again);
        Assert.IsFalse(reply.IsOpen);
    }

    [TestMethod]
    [CoversNode("aibar-plan-approval")]
    public async Task APlan_IsAskedAboutWithItsSteps_AndACancelledWaitIsANo()
    {
        var reply = Reply();
        using var cancel = new CancellationTokenSource();
        var plan = new ClientPlan { Title = "Sort the photos", Steps = [new ClientPlanStep("Group by year")] };

        var asked = reply.RequestPlanApprovalAsync(plan, cancel.Token);

        Assert.IsTrue(reply.AsksAboutPlan);
        StringAssert.Contains(reply.Text, "1. Group by year");
        cancel.Cancel();
        Assert.IsFalse(await asked);
    }

    [TestMethod]
    [CoversNode("aibar-final-answer")]
    public void AReply_JoinsTheConversation_AndARequestEndingWithNone_LeavesTheLastInPlace()
    {
        var reply = Reply();
        reply.BeginResponse("first");
        reply.ShowFinal("one");
        reply.BeginResponse("second");

        reply.Abort();

        Assert.AreEqual(("first", "one"), reply.Conversation.Exchanges.Single());
        Assert.AreEqual("one", reply.Text);
        Assert.AreEqual(BarReplyStage.Message, reply.Stage);
        Assert.IsTrue(reply.IsOpen);

        var fresh = Reply();
        fresh.BeginResponse("only");
        fresh.Abort();
        Assert.IsFalse(fresh.IsOpen, "nothing said yet: the answer is put away");
    }
}
