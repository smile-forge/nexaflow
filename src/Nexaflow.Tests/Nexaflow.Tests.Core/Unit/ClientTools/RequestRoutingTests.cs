using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

/// <summary>
/// Where a request goes when its row holds a model on this machine and one elsewhere: light work, judged so, to the machine's;
/// heavy work, or a request too long for it, to the larger one. A row of one kind is never judged. The machine's model, stuck,
/// hands the request over with what it did; the larger model may hand reading to the machine's.
/// </summary>
[TestClass]
[CoversNode("agent-routing")]
public class RequestRoutingTests
{
    /// <summary>A model that says its lines in turn, judges as told, and keeps every request it was asked to answer.</summary>
    private sealed class Model(string name, DataResidency residency, params string[] replies) : ILlmProvider
    {
        private readonly Queue<string> _replies = new(replies);

        public const string OutOfRoom = "(out of room)";

        /// <summary>Which model this one, judging, sends a request to.</summary>
        public string Judgement { get; init; } = "large";

        /// <summary>Whether this one, judging, finds a spoken request needs thought: "quick" or "think".</summary>
        public string Weighing { get; init; } = "think";

        /// <summary>How much it reads, in tokens; null when it does not say.</summary>
        public int? Context { get; init; }

        /// <summary>Every request it answered, judging and tool ranking aside.</summary>
        public List<IReadOnlyList<LlmMessage>> Asked { get; } = [];

        public int Judged { get; private set; }

        /// <summary>Every request to condense a conversation it was given, in order.</summary>
        public List<string> Condensings { get; } = [];

        /// <summary>How much of its context each reply says the conversation fills; null for a model that does not say.</summary>
        public int? Fills { get; init; }

        public string Name => name;
        public DataResidency Residency => residency;
        public bool IsAvailable => true;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var last = messages.Last(m => m.Role == LlmRole.User).Text;
            if (last.Contains("Which assistant should take it?")) { Judged++; return Said(Judgement); }
            if (last.Contains("Quick or think?")) { Judged++; return Said(Weighing); }
            if (last.Contains("Write the notes")) { Condensings.Add(last); return Said($"NOTES {Condensings.Count}: the user asked about foxes."); }
            if (last.Contains("Most relevant tool numbers")) return Said("1,2,3,4");
            Asked.Add([.. messages]);
            return Said(_replies.Count > 0 ? _replies.Dequeue() : "done", Fills);
        }

        public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
            => Task.FromResult(Context is { } tokens ? new ModelInfo(tokens) : null);

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["m"]);

        /// <summary>The line, said; <see cref="OutOfRoom"/> is a reply whose whole length went on reasoning — nothing written,
        /// and the reply ended for being too long.</summary>
        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text, int? fills = null)
        {
            await Task.Yield();
            if (text == OutOfRoom)
            {
                yield return new LlmReplyEnded(LlmReplyEnd.LengthLimit, fills);
                yield break;
            }
            yield return new LlmTextDelta(text);
            yield return new LlmReplyEnded(LlmReplyEnd.Finished, fills);
        }
    }

    private sealed class User : IAIResponseHandler
    {
        public string? Final;
        public List<string> Progress { get; } = [];
        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) => Progress.Add(message);
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
        public void ShowFinal(string finalMarkdown) => Final = finalMarkdown;
        public (int Tokens, int Window)? ContextUse;
        public void ReportContextUse(int tokens, int window) => ContextUse = (tokens, window);
        public void Abort() { }
    }

    /// <summary>A tool that says what it was asked, and counts its calls.</summary>
    private sealed class Lookup : IClientTool
    {
        public int Calls;
        public string Name => "look_up";
        public string Description => "Looks a thing up.";
        public IReadOnlyList<ClientToolParameter> Parameters => [new("what", "What to look up.")];
        public ToolSafety Safety => ToolSafety.SafeOperation;
        public bool Parallelizable => false;
        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(ToolResult.Ok("looked", $"Nothing found for {arguments["what"]}."));
        }
    }

    private sealed class Page(params IClientTool[] tools) : IPageViewModel
    {
        public string GetContext() => "A page.";
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
    }

    private static string Call(string what) => $"```client_tool\n{{\"tool\":\"look_up\",\"arguments\":{{\"what\":\"{what}\"}}}}\n```";

    /// <summary>A service whose Conversation row is <paramref name="conversation"/>, in order, judged by <paramref name="judge"/>.</summary>
    private static AIService Service(Model[] conversation, Model? judge)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, Path.GetTempPath());
        Set(nameof(WorkspaceRuntime.AiService), svc);
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));

        var models = conversation.Concat(judge is null ? [] : [judge]).Distinct().ToList();
        var ids    = models.ToDictionary(m => m, m => "c" + models.IndexOf(m));
        foreach (var (model, id) in ids) svc.Register(id, model);

        var config = new AiConfig { Columns = [.. ids.Select(p => new ProviderModelPair { Id = p.Value, ProviderName = p.Key.Name, Model = "m" })] };
        config.Assignments["Conversation"] = AbilityChain.Of([.. conversation.Select(m => ids[m])]);
        if (judge is not null) config.Assignments["Disambiguation"] = AbilityChain.Of(ids[judge]);
        svc.LoadAbilityConfig(config);
        return svc;

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    [TestMethod]
    public async Task LightWork_IsTheMachinesModels()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "from here") { Judgement = "small" };

        var reply = await Service([cloud, local], judge: local).RunAgentAsync(new Page(), "Rename notes.txt to todo.txt.", true, new User());

        Assert.AreEqual("from here", reply!.Text);
        Assert.AreEqual(0, cloud.Asked.Count);
        Assert.AreEqual(1, local.Judged);
    }

    [TestMethod]
    public async Task HeavyWork_IsTheLargerModels()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "from here") { Judgement = "large" };

        var reply = await Service([local, cloud], judge: local).RunAgentAsync(new Page(), "Refactor the parser into three passes.", true, new User());

        Assert.AreEqual("from the cloud", reply!.Text, "the row's order gives way to the judgement");
        Assert.AreEqual(0, local.Asked.Count);
    }

    [TestMethod]
    public async Task ARequestTooLongForTheMachinesModel_IsTheLargerModels_Unjudged()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "from here") { Judgement = "small", Context = 1000 };

        var reply = await Service([local, cloud], judge: local).RunAgentAsync(new Page(), new string('x', 4000), true, new User());

        Assert.AreEqual("from the cloud", reply!.Text);
        Assert.AreEqual(0, local.Judged, "a request it cannot read is not put to the judge");
    }

    [TestMethod]
    public async Task ARowOfOneKind_IsNeverJudged()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var judge = new Model("Nexaflow", DataResidency.StaysOnMachine) { Judgement = "small" };

        var reply = await Service([cloud], judge).RunAgentAsync(new Page(), "Rename notes.txt.", true, new User());

        Assert.AreEqual("from the cloud", reply!.Text);
        Assert.AreEqual(0, judge.Judged);
    }

    [TestMethod]
    public async Task WithNoJudge_TheRowsOrderStands()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "from here");

        var reply = await Service([local, cloud], judge: null).RunAgentAsync(new Page(), "q", true, new User());

        Assert.AreEqual("from here", reply!.Text);
    }

    [TestMethod]
    public async Task TheMachinesModel_Stuck_HandsTheRequestOver_WithWhatItDid()
    {
        var tool  = new Lookup();
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "Found it another way.");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, Call("x"), Call("x"), Call("x")) { Judgement = "small" };
        var user  = new User();
        IReadOnlyList<LlmMessage> ran = [];

        var reply = await Service([cloud, local], judge: local)
            .RunAgentAsync(new Page(tool), "Find x.", true, user, new AgentRunOptions { Ran = r => ran = r });

        Assert.AreEqual("Found it another way.", reply!.Text);
        Assert.AreEqual(2, tool.Calls, "the third, repeated call is not run");
        var handed = cloud.Asked.Single();
        Assert.AreEqual("Find x.", handed.First(m => m.Role == LlmRole.User).Text, "the request itself comes first");
        StringAssert.Contains(handed.Last().Text, "smaller model began");
        Assert.IsTrue(handed.Any(m => m.Text.Contains("Nothing found for x.")), "what it did comes with it");
        Assert.IsTrue(user.Progress.Any(p => p.Contains("Claude")), "the user sees it handed over");

        Assert.AreEqual("Find x.", ran.First().Text);
        Assert.IsFalse(ran.Any(m => m.Text.Contains("smaller model began")), "the hand-over is said to the model, not kept as the user's");
        Assert.AreEqual("Found it another way.", ran.Last().Text);
    }

    [TestMethod]
    public async Task TheLargerModel_MayHandReadingToTheMachinesModel_AndOnlyWhenTheRowHasOne()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "read");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine) { Judgement = "large" };
        await Service([cloud, local], judge: local).RunAgentAsync(new Page(), "Summarise C:\\report.pdf.", true, new User());

        var alone = new Model("Claude", DataResidency.LeavesMachine, "read");
        await Service([alone], judge: local).RunAgentAsync(new Page(), "Summarise C:\\report.pdf.", true, new User());

        StringAssert.Contains(cloud.Asked.Single()[0].Text, "ask_local_model");
        Assert.IsFalse(alone.Asked.Single()[0].Text.Contains("ask_local_model"));
    }

    [TestMethod]
    public async Task AReplyThatRanOutOfRoomBeforeAnswering_IsHandedToTheLargerModel()
    {
        var cloud = new Model("Claude", DataResidency.LeavesMachine, "from the cloud");
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, Model.OutOfRoom) { Judgement = "small" };
        var user  = new User();

        var reply = await Service([cloud, local], judge: local).RunAgentAsync(new Page(), "What do you think of this page?", true, user);

        Assert.AreEqual("from the cloud", reply!.Text, "a reply with nothing in it is not an answer");
        Assert.IsTrue(user.Progress.Any(p => p.Contains("Claude")), "handed over, and the user told");
        StringAssert.Contains(cloud.Asked.Single().Last().Text, "reached the most it may be", "the larger model is told why");
    }

    [TestMethod]
    public async Task AReplyThatRanOutOfRoomBeforeAnswering_WithNoneLargerToHandTo_SaysSo_RatherThanSayingNothing()
    {
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, Model.OutOfRoom);
        var user  = new User();

        var reply = await Service([local], judge: null).RunAgentAsync(new Page(), "What do you think of this page?", true, user);

        StringAssert.Contains(reply!.Text, "(stopped: reply too long)");
        Assert.AreEqual(reply.Text, user.Final, "said to the user, not an empty answer");
    }

    [TestMethod]
    [DataRow("quick", false)]
    [DataRow("think", true)]
    [DataRow("Quick: it is a greeting", false)]
    [DataRow("not sure", true)]
    public async Task WhetherASpokenRequestNeedsThought_IsTheJudgesWord_ThoughtWhenItIsUnclear(string said, bool needs)
    {
        var judge = new Model("Nexaflow", DataResidency.StaysOnMachine) { Weighing = said };

        Assert.AreEqual(needs, await Service([judge], judge).NeedsThoughtAsync("What do you think of it?", [], default));
        Assert.IsTrue(await Service([judge], judge: null).NeedsThoughtAsync("Hello.", [], default), "with no judge, thought");
    }

    private static List<LlmMessage> Exchanges(int count) =>
    [
        .. Enumerable.Range(0, count).SelectMany(i => new LlmMessage[]
        {
            new(LlmRole.User, $"question {i} " + new string('q', 300)),
            new(LlmRole.Assistant, $"answer {i} " + new string('a', 300)),
        }),
    ];

    [TestMethod]
    public async Task AConversationOutgrowingTheModel_HasItsOlderPartCondensedBeforeTheRun_AndTheCallerKeepsItSo()
    {
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "the answer") { Context = 600 };
        IReadOnlyList<LlmMessage>? condensed = null;
        var options = new AgentRunOptions { History = Exchanges(6), HistoryCondensed = c => condensed = c };

        var reply = await Service([local], judge: null).RunAgentAsync(new Page(), "And now?", true, new User(), options);

        Assert.AreEqual("the answer", reply!.Text);
        Assert.AreEqual(2, condensed!.Count, "the newest exchange is kept word for word");
        StringAssert.StartsWith(condensed[0].Text, AIService.CondensedLead, "the notes stand before the request they precede");
        StringAssert.Contains(condensed[0].Text, "NOTES 1");
        StringAssert.Contains(condensed[0].Text, "question 5");
        var read = local.Asked.Single();
        Assert.IsFalse(read.Any(m => m.Text.Contains("question 0")), "the older part is read as notes, not again whole");
        StringAssert.Contains(local.Condensings.Single(), "question 0", "the notes are made from it");
    }

    [TestMethod]
    public async Task AConversationThatFits_IsLeftAsItIs()
    {
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "the answer") { Context = 100_000 };
        IReadOnlyList<LlmMessage>? condensed = null;
        var options = new AgentRunOptions { History = Exchanges(6), HistoryCondensed = c => condensed = c };

        await Service([local], judge: null).RunAgentAsync(new Page(), "And now?", true, new User(), options);

        Assert.IsNull(condensed);
        Assert.AreEqual(0, local.Condensings.Count);
    }

    [TestMethod]
    public async Task AStretchLongerThanTheModelReadsAtOnce_IsCondensedAPartAtATime_TheNotesCarriedOn()
    {
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine) { Context = 200 };
        var stretch = Enumerable.Range(0, 5).Select(i => ("User", $"part {i} " + new string('x', 3000))).ToList();

        var notes = await Service([local], judge: null).CondenseConversationAsync(stretch, earlier: null);

        Assert.AreEqual(3, local.Condensings.Count, "each part about half of what the model reads");
        StringAssert.Contains(local.Condensings[1], "NOTES 1", "the notes so far go into the next part's");
        Assert.AreEqual("NOTES 3: the user asked about foxes.", notes);
    }

    [TestMethod]
    public async Task HowFullTheModelsContextIs_IsToldAfterEachStep_WhereTheModelSays()
    {
        var local = new Model("Nexaflow", DataResidency.StaysOnMachine, "the answer") { Context = 262_144, Fills = 34_210 };
        var user  = new User();

        await Service([local], judge: null).RunAgentAsync(new Page(), "Hello.", true, user);

        Assert.AreEqual((34_210, 262_144), user.ContextUse);
    }

    [TestMethod]
    public void TheContextLine_IsAsLongAsTheShareUsed_AndWarnsWhenItIsNearlyFull()
    {
        var some = new ContextUse(34_210, 262_144);
        Assert.AreEqual(0.13, some.Fill, 0.005);
        Assert.IsFalse(some.IsNearlyFull);
        Assert.AreEqual(some.Fill, some.Used.Value, 1e-9);
        Assert.AreEqual(1 - some.Fill, some.Left.Value, 1e-9);

        Assert.IsTrue(new ContextUse(220_000, 262_144).IsNearlyFull, "the conversation will soon be condensed");
        Assert.AreEqual(1, new ContextUse(300_000, 262_144).Fill, "never past full");
    }
}
