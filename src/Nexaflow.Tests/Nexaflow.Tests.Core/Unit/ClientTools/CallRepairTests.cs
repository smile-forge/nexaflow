using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A call the model meant but wrote so it could not be read is written again in a context of its own — the call as
/// written, why it did not read, and an example made from the tool's definition — and the call written again takes the
/// attempt's place: the conversation goes on as if it had been written so. One that still cannot be read is told why.
/// </summary>
[TestClass]
[CoversNode("chat-interface")]
public class CallRepairTests
{
    private sealed record Request(IReadOnlyList<LlmMessage> Messages, IReadOnlyList<LlmTool> Tools)
    {
        public bool IsRepair => Messages[0].Text.Contains("could not be read");
        public bool IsPick   => Messages[^1].Text.Contains("Most relevant tool numbers");
    }

    /// <summary>A model answering each conversation step, and each request to write a call again, as told.</summary>
    private sealed class Model(bool native, Func<Request, int, (string Text, LlmToolCall[] Calls)> answer) : ILlmProvider
    {
        public List<Request> Requests { get; } = [];
        public IEnumerable<Request> Steps   => Requests.Where(r => !r.IsRepair && !r.IsPick);
        public IEnumerable<Request> Repairs => Requests.Where(r => r.IsRepair);

        public string Name => "model";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool SupportsToolCalls => native;

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => Answer(new([.. messages], []));

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                            CancellationToken ct = default)
            => Answer(new([.. messages], tools));

        private IAsyncEnumerable<LlmStreamEvent> Answer(Request request)
        {
            var step = Steps.Count();
            Requests.Add(request);
            var (text, calls) = request.IsPick ? ("1", []) : answer(request, step);
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

    private sealed class User : IAIResponseHandler
    {
        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
    }

    private static IClientTool AddNote(List<string> notes) => new DelegateClientTool(
        "add_note", "Add a note.", [new("text", "The note.")], ToolSafety.SafeOperation, (args, _) =>
        {
            notes.Add((string?)args["text"] ?? "");
            return Task.FromResult(ToolResult.Ok("note added", "The note was added."));
        });

    private static AIService Service(ILlmProvider model)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, TestTemp.Root);
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.AiService))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [svc]);
        typeof(WorkspaceRuntime).GetProperty(nameof(WorkspaceRuntime.ShellServices))!.GetSetMethod(nonPublic: true)!.Invoke(ws, [new ShellServices(ws)]);
        svc.Register("c1", model);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = "model", Model = "m" }],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("c1"), ["Disambiguation"] = AbilityChain.Of("c1") },
        });
        return svc;
    }

    private const string Canonical = "```client_tool\n{\"tool\":\"add_note\",\"arguments\":{\"text\":\"buy milk\"}}\n```";

    [TestMethod]
    public async Task AFencedCallThatDidNotRead_IsWrittenAgainFromAnExample_AndTakesTheAttemptsPlace()
    {
        var notes = new List<string>();
        var model = new Model(native: false, (request, step) =>
            request.IsRepair ? ("Here it is:\n" + Canonical, [])
            : step == 0     ? ("Adding it.\n```client_tool\n{\"tool\": \"add_note\", \"arguments\": {\"text\": \"buy milk\"}\n```", [])
            :                 ("Added.", []));

        var response = await Service(model).RunAgentAsync(new Page(AddNote(notes)), "Note: buy milk.", true, new User());

        Assert.AreEqual("Added.", response?.Text);
        CollectionAssert.AreEqual(new[] { "buy milk" }, notes);

        var repair = model.Repairs.Single();
        Assert.AreEqual(2, repair.Messages.Count, "a context of its own");
        StringAssert.Contains(repair.Messages[1].Text, "{\"tool\":\"add_note\",\"arguments\":{\"text\":\"<text>\"}}");
        StringAssert.Contains(repair.Messages[1].Text, "invalid JSON");

        var remembered = model.Steps.ElementAt(1).Messages;
        Assert.AreEqual(Canonical, remembered.Single(m => m.Role == LlmRole.Assistant).Text, "the call as one that read would be written");
        Assert.IsFalse(remembered.Any(m => m.Text.Contains("Adding it.") || m.Text.Contains("Fix the block")), "the attempt is not remembered");
    }

    [TestMethod]
    public async Task ANativeCallThatDidNotRead_IsMadeAgainWithOnlyItsToolOffered_AndKeepsItsPlace()
    {
        var notes = new List<string>();
        var model = new Model(native: true, (request, step) =>
            request.IsRepair ? ("", [new LlmToolCall("r1", "add_note", new JsonObject { ["text"] = "buy milk" })])
            : step == 0     ? ("", [new LlmToolCall("c1", "add_note", []) { Unread = "{text: buy milk" }])
            :                 ("Added.", []));

        await Service(model).RunAgentAsync(new Page(AddNote(notes)), "Note: buy milk.", true, new User());

        CollectionAssert.AreEqual(new[] { "buy milk" }, notes);
        var repair = model.Repairs.Single();
        CollectionAssert.AreEqual(new[] { "add_note" }, repair.Tools.Select(t => t.Name).ToArray());
        StringAssert.Contains(repair.Messages[1].Text, "{text: buy milk");

        var remembered = model.Steps.ElementAt(1).Messages;
        var call = remembered.Single(m => m.Role == LlmRole.Assistant).ToolCalls!.Single();
        Assert.AreEqual(("c1", "buy milk", null), (call.Id, (string?)call.Arguments["text"], call.Unread));
        Assert.AreEqual("c1", remembered[^1].Answers?.Id);
    }

    [TestMethod]
    public async Task ACallThatStillDoesNotRead_IsToldWhy()
    {
        var model = new Model(native: false, (request, step) =>
            request.IsRepair ? ("I am not sure.", [])
            : step == 0     ? ("```client_tool\n{\"tool\": \"add_note\", \"arguments\": {\"text\": \"buy milk\"}\n```", [])
            :                 ("Sorry.", []));

        await Service(model).RunAgentAsync(new Page(AddNote([])), "Note: buy milk.", true, new User());

        var told = model.Steps.ElementAt(1).Messages[^1].Text;
        StringAssert.StartsWith(told, "TOOL RESULTS");
        StringAssert.Contains(told, "invalid JSON");
    }
}
