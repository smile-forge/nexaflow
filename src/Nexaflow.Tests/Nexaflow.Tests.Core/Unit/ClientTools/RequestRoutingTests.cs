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

        /// <summary>Which model this one, judging, sends a request to.</summary>
        public string Judgement { get; init; } = "large";

        /// <summary>How much it reads, in tokens; null when it does not say.</summary>
        public int? Context { get; init; }

        /// <summary>Every request it answered, judging and tool ranking aside.</summary>
        public List<IReadOnlyList<LlmMessage>> Asked { get; } = [];

        public int Judged { get; private set; }

        public string Name => name;
        public DataResidency Residency => residency;
        public bool IsAvailable => true;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var last = messages.Last(m => m.Role == LlmRole.User).Text;
            if (last.Contains("Which assistant should take it?")) { Judged++; return Said(Judgement); }
            if (last.Contains("Most relevant tool numbers")) return Said("1,2,3,4");
            Asked.Add([.. messages]);
            return Said(_replies.Count > 0 ? _replies.Dequeue() : "done");
        }

        public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
            => Task.FromResult(Context is { } tokens ? new ModelInfo(tokens) : null);

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["m"]);

        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text)
        {
            await Task.Yield();
            yield return new LlmTextDelta(text);
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
}
