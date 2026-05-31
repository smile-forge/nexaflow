using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

[TestClass]
public class AgentLoopTests
{
    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class ScriptedLlmProvider(IEnumerable<string> responses, string rankReply = "1,2,3,4") : ILlmProvider
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> SystemPrompts { get; } = [];

        public string Name => "fake";

        public Task<LlmResponse?> CompleteAsync(
            IReadOnlyList<LlmMessage> messages, string model,
            IReadOnlyList<LlmAttachment>? attachments = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var lastUser = messages.LastOrDefault(m => m.Role == LlmRole.User)?.Text ?? string.Empty;
            if (lastUser.Contains("Most relevant tool numbers"))
                return Task.FromResult<LlmResponse?>(new LlmResponse(rankReply));

            SystemPrompts.Add(messages.FirstOrDefault(m => m.Role == LlmRole.System)?.Text ?? string.Empty);
            var text = _responses.Count > 0 ? _responses.Dequeue() : "done";
            return Task.FromResult<LlmResponse?>(new LlmResponse(text));
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["m"]);
    }

    private sealed class FakeApprover : IToolApprovalCoordinator
    {
        public bool BatchResult = true;
        public bool PlanResult  = true;
        public int  BatchRequests;
        public int  PlanRequests;
        public string? Final;

        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        { BatchRequests++; return Task.FromResult(BatchResult); }
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        { PlanRequests++; return Task.FromResult(PlanResult); }
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) => Final = finalMarkdown;
    }

    private sealed class RecordingTool(string name, ToolSafety safety, bool parallelizable = false) : IClientTool
    {
        private int _invocations;
        public int Invocations => _invocations;

        public string Name => name;
        public string Description => $"{name} tool";
        public IReadOnlyList<ClientToolParameter> Parameters => [];
        public ToolSafety Safety => safety;
        public bool Parallelizable => parallelizable;

        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(ToolResult.Ok($"{name} ran"));
        }
    }

    private sealed class TestPage(params IClientTool[] tools) : IPageViewModel
    {
        public string GetContext() => "Test page.";
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
    }

    private static AIService BuildService(ILlmProvider provider)
    {
        var svc = new AIService(new Workspace(), Path.GetTempPath());
        svc.Register("fake", provider);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = "fake", Model = "m" }],
            Assignments = { ["Conversation"] = "c1", ["Disambiguation"] = "c1" }
        });
        return svc;
    }

    private static string ToolBlock(string tool, string args = "{}")
        => $"```client_tool\n{{\"tool\":\"{tool}\",\"arguments\":{args}}}\n```";

    // ── Tests ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReadOnlyTool_AutoRuns_WithoutApproval_AndFinishes()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.ReadOnly);
        var provider = new ScriptedLlmProvider([ToolBlock("get_x"), "All done."]);
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "do it", true, approver, default);

        Assert.IsNotNull(res);
        Assert.AreEqual(AiResponseKind.Message, res!.Kind);
        Assert.AreEqual("All done.", res.Text);
        Assert.AreEqual(1, tool.Invocations);
        Assert.AreEqual(0, approver.BatchRequests);
        Assert.AreEqual("All done.", approver.Final);
    }

    [TestMethod]
    public async Task MutatingTool_Denied_IsNotExecuted()
    {
        var tool     = new RecordingTool("write_x", ToolSafety.RequiresApproval);
        var provider = new ScriptedLlmProvider([ToolBlock("write_x"), "Cancelled then."]);
        var approver = new FakeApprover { BatchResult = false };

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "write", true, approver, default);

        Assert.AreEqual(0, tool.Invocations);
        Assert.AreEqual(1, approver.BatchRequests);
        Assert.AreEqual("Cancelled then.", res!.Text);
    }

    [TestMethod]
    public async Task MutatingTool_Accepted_Executes()
    {
        var tool     = new RecordingTool("write_x", ToolSafety.RequiresApproval);
        var provider = new ScriptedLlmProvider([ToolBlock("write_x"), "Done."]);
        var approver = new FakeApprover { BatchResult = true };

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "write", true, approver, default);

        Assert.AreEqual(1, tool.Invocations);
        Assert.AreEqual(1, approver.BatchRequests);
    }

    [TestMethod]
    public async Task ParallelBatch_RunsAll_WithOneApproval()
    {
        var tool     = new RecordingTool("write_x", ToolSafety.RequiresApproval, parallelizable: true);
        var batch    = ToolBlock("write_x", "{\"name\":\"a\"}") + "\n" + ToolBlock("write_x", "{\"name\":\"b\"}");
        var provider = new ScriptedLlmProvider([batch, "Both done."]);
        var approver = new FakeApprover { BatchResult = true };

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "write both", true, approver, default);

        Assert.AreEqual(2, tool.Invocations);
        Assert.AreEqual(1, approver.BatchRequests);
    }

    [TestMethod]
    public async Task IterationCap_ReturnsStoppedMessage()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.ReadOnly);
        var provider = new ScriptedLlmProvider(Enumerable.Repeat(ToolBlock("get_x"), 50));
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "loop", true, approver, default);

        StringAssert.Contains(res!.Text!, "max steps");
        Assert.AreEqual(8, tool.Invocations);   // MaxAgentSteps
    }

    [TestMethod]
    public async Task PreCancelledToken_ReturnsNull()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.ReadOnly);
        var provider = new ScriptedLlmProvider([ToolBlock("get_x"), "done"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "x", true, new FakeApprover(), cts.Token);

        Assert.IsNull(res);
        Assert.AreEqual(0, tool.Invocations);
    }

    [TestMethod]
    public async Task ApprovedPlan_RunsToolsWithoutPerBatchApproval()
    {
        var tool     = new RecordingTool("write_x", ToolSafety.RequiresApproval);
        var plan     = "```client_plan\n{\"title\":\"P\",\"steps\":[{\"title\":\"do\",\"tool\":\"write_x\"}]}\n```";
        var provider = new ScriptedLlmProvider([plan, ToolBlock("write_x"), "Finished."]);
        var approver = new FakeApprover { PlanResult = true };

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "multi step", true, approver, default);

        Assert.AreEqual(1, approver.PlanRequests);
        Assert.AreEqual(0, approver.BatchRequests, "plan approval should cover its tool steps");
        Assert.AreEqual(1, tool.Invocations);
        Assert.AreEqual("Finished.", res!.Text);
    }

    [TestMethod]
    public async Task ManyTools_AreFilteredToFourPlusGetCommands()
    {
        var tools    = Enumerable.Range(1, 6)
                                 .Select(i => (IClientTool)new RecordingTool($"tool_{i}", ToolSafety.ReadOnly))
                                 .ToArray();
        var provider = new ScriptedLlmProvider(["done"], rankReply: "1,2");

        await BuildService(provider).RunAgentAsync(new TestPage(tools), "hi", true, new FakeApprover(), default);

        var sys = provider.SystemPrompts.Single();
        StringAssert.Contains(sys, "get_client_commands");
        StringAssert.Contains(sys, "tool_1");
        StringAssert.Contains(sys, "tool_2");
        Assert.IsFalse(sys.Contains("tool_6"), "tools beyond the top-ranked few should be filtered out");
    }

    [TestMethod]
    public async Task UnknownTool_IsReportedNotExecuted_AndLoopContinues()
    {
        var provider = new ScriptedLlmProvider([ToolBlock("does_not_exist"), "Recovered."]);
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(), "x", true, approver, default);

        Assert.AreEqual("Recovered.", res!.Text);
    }
}
