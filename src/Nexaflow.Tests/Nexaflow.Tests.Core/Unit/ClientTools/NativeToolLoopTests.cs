using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The agent loop with a model that calls tools in a protocol of its own: its tools are offered to it in that protocol
/// rather than described in its instructions, its calls come back structured, and what came of each goes back as a tool
/// message answering it — a plan and a suggestion for the input box included.
/// </summary>
[TestClass]
[CoversNode("chat-interface")]
public class NativeToolLoopTests
{
    /// <summary>A model that calls tools natively, each step scripted: what it writes and the calls it makes.</summary>
    private sealed class NativeModel(params (string Text, LlmToolCall[] Calls)[] steps) : ILlmProvider
    {
        private readonly Queue<(string Text, LlmToolCall[] Calls)> _steps = new(steps);

        public List<IReadOnlyList<LlmMessage>> Conversations { get; } = [];
        public List<IReadOnlyList<LlmTool>> Offered { get; } = [];

        public string Name => "native";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool SupportsToolCalls => true;

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => throw new AssertFailedException("a model that calls tools natively is offered them in its own protocol");

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                            CancellationToken ct = default)
        {
            Conversations.Add([.. messages]);
            Offered.Add(tools);
            var (text, calls) = _steps.Count > 0 ? _steps.Dequeue() : ("Done.", []);
            return Streamed(text, calls);
        }

        private static async IAsyncEnumerable<LlmStreamEvent> Streamed(string text, LlmToolCall[] calls, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (text.Length > 0) yield return new LlmTextDelta(text);
            foreach (var call in calls) yield return new LlmToolCallRequested(call);
        }
    }

    private sealed class Page(params IClientTool[] tools) : IPageViewModel
    {
        public string GetContext() => "A notes page.";
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
    }

    /// <summary>Answers approvals as told, and records what it was asked.</summary>
    private sealed class User(bool approve = true) : IAIResponseHandler
    {
        public List<IReadOnlyList<string>> Batches { get; } = [];
        public List<string> Plans { get; } = [];

        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }

        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        {
            Batches.Add([.. batch.Select(c => c.Tool)]);
            return Task.FromResult(approve);
        }

        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        {
            Plans.Add(plan.Title);
            return Task.FromResult(approve);
        }
    }

    private static IClientTool AddNote(List<JsonObject> calls) => new DelegateClientTool(
        "add_note", "Add a note.", [new("text", "The note.")], ToolSafety.RequiresApproval, (args, _) =>
        {
            calls.Add(args);
            return Task.FromResult(ToolResult.Ok("note added", "The note was added."));
        });

    private static AIService Service(ILlmProvider model)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, Path.GetTempPath());
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.AiService))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [svc]);
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.ShellServices))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [new ShellServices(ws)]);
        svc.Register("c1", model);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = "native", Model = "m" }],
            Assignments = { ["Conversation"] = AbilityChain.Of("c1"), ["Disambiguation"] = AbilityChain.Of("c1") },
        });
        return svc;
    }

    private static LlmToolCall Call(string id, string name, JsonObject arguments) => new(id, name, arguments);

    [TestMethod]
    public async Task ANativeModel_IsOfferedItsTools_AndHearsWhatCameOfEachCall_InItsOwnProtocol()
    {
        var calls = new List<JsonObject>();
        var model = new NativeModel(("", [Call("c1", "add_note", new JsonObject { ["text"] = "buy milk" })]), ("Added.", []));

        var response = await Service(model).RunAgentAsync(new Page(AddNote(calls)), "Note: buy milk.", true, new User());

        Assert.AreEqual("Added.", response?.Text);
        Assert.AreEqual("buy milk", (string?)calls.Single()["text"]);

        var system = model.Conversations[0][0].Text;
        Assert.IsFalse(system.Contains("client_tool"), "no fenced format to learn: the tools come in its own protocol");
        CollectionAssert.IsSubsetOf(new[] { "add_note", "propose_plan", "suggest_input" }, model.Offered[0].Select(t => t.Name).ToArray());
        var declared = model.Offered[0].Single(t => t.Name == "add_note");
        Assert.AreEqual("string", (string?)declared.Parameters["properties"]!["text"]!["type"]);

        var answered = model.Conversations[1];
        var said = answered[^2];
        Assert.AreEqual(LlmRole.Assistant, said.Role);
        Assert.AreEqual("c1", said.ToolCalls!.Single().Id, "the call is remembered as made");
        Assert.AreEqual(LlmRole.Tool, answered[^1].Role);
        Assert.AreEqual("c1", answered[^1].Answers!.Id, "and what came of it answers it");
        StringAssert.Contains(answered[^1].Text, "The note was added.");
    }

    [TestMethod]
    public async Task ADeclinedCall_IsAnsweredAsNotRun()
    {
        var calls = new List<JsonObject>();
        var model = new NativeModel(("", [Call("c1", "add_note", new JsonObject { ["text"] = "x" })]), ("Left it.", []));

        await Service(model).RunAgentAsync(new Page(AddNote(calls)), "Note x.", true, new User(approve: false));

        Assert.AreEqual(0, calls.Count);
        var answer = model.Conversations[1][^1];
        Assert.AreEqual(("c1", LlmRole.Tool), (answer.Answers?.Id, answer.Role));
        StringAssert.StartsWith(answer.Text, "Not run:");
    }

    /// <summary>A tool whose calls need approval only when they ask to be drawn well.</summary>
    private sealed class Drawing(List<JsonObject> calls) : IClientTool
    {
        public string Name => "draw";
        public string Description => "Draw.";
        public IReadOnlyList<ClientToolParameter> Parameters => [new("quality", "How well.", Required: false, Choices: ["standard", "high"])];
        public ToolSafety Safety => ToolSafety.SafeOperation;
        public ToolSafety SafetyOf(JsonObject arguments) =>
            (string?)arguments["quality"] == "high" ? ToolSafety.RequiresApproval : ToolSafety.SafeOperation;
        public bool Parallelizable => false;

        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            calls.Add(arguments);
            return Task.FromResult(ToolResult.Ok("drawn", "Drawn."));
        }
    }

    [TestMethod]
    public async Task ACallIsPutToTheUser_WhenWhatItAsksFor_NeedsApproval()
    {
        var calls = new List<JsonObject>();
        var model = new NativeModel(("", [Call("c1", "draw", new JsonObject { ["quality"] = "standard" })]),
                                    ("", [Call("c2", "draw", new JsonObject { ["quality"] = "high" })]),
                                    ("Both drawn.", []));
        var user  = new User();

        await Service(model).RunAgentAsync(new Page(new Drawing(calls)), "Draw twice.", true, user);

        Assert.AreEqual(2, calls.Count);
        Assert.AreEqual(1, user.Batches.Count, "only the high-quality call was asked about");
        CollectionAssert.AreEqual(new[] { "draw" }, user.Batches[0].ToArray());
    }

    [TestMethod]
    public async Task APlan_IsProposedByCallingForIt_PutToTheUser_AndTheAnswerGoesBackToTheCall()
    {
        var plan = new JsonObject
        {
            ["title"] = "Tidy the notes",
            ["steps"] = new JsonArray(new JsonObject { ["title"] = "Add one", ["tool"] = "add_note" }),
        };
        var model = new NativeModel(("", [Call("p1", "propose_plan", plan)]), ("Ready.", []));
        var user  = new User();

        await Service(model).RunAgentAsync(new Page(AddNote([])), "Tidy my notes.", true, user);

        CollectionAssert.AreEqual(new[] { "Tidy the notes" }, user.Plans);
        var answer = model.Conversations[1][^1];
        Assert.AreEqual("p1", answer.Answers?.Id);
        StringAssert.Contains(answer.Text, "approved");
    }

    [TestMethod]
    public async Task ASuggestion_FillsInTheInputBox()
    {
        var model = new NativeModel(("", [Call("s1", "suggest_input", new JsonObject { ["text"] = "git status" })]));

        var response = await Service(model).RunAgentAsync(new Page(), "What should I run?", true, new User());

        Assert.AreEqual((AiResponseKind.Prefill, "git status"), (response?.Kind, response?.Text));
    }

    [TestMethod]
    public void AnArrayParameter_SaysWhatItHolds_AsSomeProvidersRequire()
    {
        var tool = new DelegateClientTool("edit", "Edit.",
            [new("edits", "The edits.", Type: "array", ItemType: "object"), new("tags", "Any tags.", Required: false, Type: "array")],
            ToolSafety.RequiresApproval, (_, _) => Task.FromResult(ToolResult.Ok("", "")));

        var properties = NativeToolProtocol.Declare(tool).Parameters["properties"]!;

        Assert.AreEqual("object", (string?)properties["edits"]!["items"]!["type"]);
        Assert.AreEqual(0, properties["tags"]!["items"]!.AsObject().Count, "items of any kind");
    }

    [TestMethod]
    public void AParameterOfAFewValues_NamesThem_NativelyAsAnEnum_AndInTextAsChoices()
    {
        var tool = new DelegateClientTool("draw", "Draw.",
            [new("shape", "Its shape.", Required: false, Choices: ["square", "landscape"])],
            ToolSafety.SafeOperation, (_, _) => Task.FromResult(ToolResult.Ok("", "")));

        var shape = NativeToolProtocol.Declare(tool).Parameters["properties"]!["shape"]!;

        CollectionAssert.AreEqual(new[] { "square", "landscape" }, shape["enum"]!.AsArray().Select(c => (string?)c).ToArray());
        StringAssert.Contains(ToolCatalog.Describe([tool]), "one of square | landscape");
        Assert.AreEqual("square", (string?)ToolCatalog.Example(tool)["shape"], "the example uses a value it takes");
    }

    [TestMethod]
    public async Task ARun_ReportsWhatItAddedToTheConversation_TheCallsAndWhatCameOfThem()
    {
        var model = new NativeModel(("", [Call("c1", "add_note", new JsonObject { ["text"] = "buy milk" })]), ("Added.", []));
        IReadOnlyList<LlmMessage> ran = [];

        await Service(model).RunAgentAsync(new Page(AddNote([])), "Note: buy milk.", true, new User(), new AgentRunOptions { Ran = r => ran = r });

        CollectionAssert.AreEqual(new[] { LlmRole.User, LlmRole.Assistant, LlmRole.Tool, LlmRole.Assistant }, ran.Select(m => m.Role).ToArray());
        Assert.AreEqual("c1", ran[1].ToolCalls!.Single().Id);
        Assert.AreEqual("Added.", ran[^1].Text);
    }

    [TestMethod]
    public async Task ARunStoppedWhileACallRuns_ReportsTheCallAsInterrupted_NotUnanswered()
    {
        using var stop = new CancellationTokenSource();
        var stopping = new DelegateClientTool("add_note", "Add a note.", [new("text", "The note.")], ToolSafety.SafeOperation, (_, _) =>
        {
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        });
        var model = new NativeModel(("", [Call("c1", "add_note", new JsonObject { ["text"] = "x" })]));
        IReadOnlyList<LlmMessage> ran = [];

        var response = await Service(model).RunAgentAsync(new Page(stopping), "Note x.", true, new User(), new AgentRunOptions { Ran = r => ran = r }, stop.Token);

        Assert.IsNull(response);
        Assert.AreEqual(("c1", LlmRole.Tool), (ran[^1].Answers?.Id, ran[^1].Role));
        StringAssert.StartsWith(ran[^1].Text, "Interrupted");
    }
}
