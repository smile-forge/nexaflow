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
            IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
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

    private sealed class FakeApprover : IAIResponseHandler
    {
        public bool BatchResult = true;
        public bool PlanResult  = true;
        public int  BatchRequests;
        public int  PlanRequests;
        public string? Final;

        public void BeginResponse(string userText) { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        { BatchRequests++; return Task.FromResult(BatchResult); }
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        { PlanRequests++; return Task.FromResult(PlanResult); }
        public void ReportProgress(string message) { }
        public void ShowFinal(string finalMarkdown) => Final = finalMarkdown;
        public void Abort() { }
    }

    private sealed class RecordingTool(string name, ToolSafety safety, bool parallelizable = false, bool exempt = false) : IClientTool
    {
        private int _invocations;
        public int Invocations => _invocations;

        public string Name => name;
        public string Description => $"{name} tool";
        public IReadOnlyList<ClientToolParameter> Parameters => [];
        public ToolSafety Safety => safety;
        public bool Parallelizable => parallelizable;
        public bool ExemptFromRepeatGuard => exempt;

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

    // Records the attachments handed to each completion turn, and reports an image capability.
    private sealed class AttachmentRecordingProvider(IEnumerable<string> responses, bool supportsImages) : ILlmProvider
    {
        private readonly Queue<string> _responses = new(responses);
        public List<IReadOnlyList<LlmAttachment>?> Calls { get; } = [];
        public string LastToolResultsText { get; private set; } = string.Empty;

        public string Name => "fake";
        public bool SupportsImages => supportsImages;

        public Task<LlmResponse?> CompleteAsync(
            IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var lastUser = messages.LastOrDefault(m => m.Role == LlmRole.User);
            var lastText = lastUser?.Text ?? string.Empty;
            if (lastText.Contains("Most relevant tool numbers"))
                return Task.FromResult<LlmResponse?>(new LlmResponse("1,2,3,4"));

            LastToolResultsText = lastText;
            Calls.Add(lastUser?.Attachments);   // attachments now ride the message, not a side parameter
            var text = _responses.Count > 0 ? _responses.Dequeue() : "done";
            return Task.FromResult<LlmResponse?>(new LlmResponse(text));
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["m"]);
    }

    // A read-only tool that "captures" an image and returns it for the model to view (mirrors the web
    // view's capture_web_page tool).
    private sealed class ImageCaptureTool(byte[] png) : IClientTool
    {
        public int Invocations;
        public string Name => "capture_web_page";
        public string Description => "capture the current page as an image";
        public IReadOnlyList<ClientToolParameter> Parameters => [];
        public ToolSafety Safety => ToolSafety.ReadOnly;
        public bool Parallelizable => false;

        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            Invocations++;
            return Task.FromResult(
                ToolResult.Ok("captured", "screenshot attached")
                with { Images = [new ContextImage(png, "image/png", "http://x")] });
        }
    }

    // A vision model that records the images it was asked to describe and returns a fixed description.
    private sealed class DescribingImageProvider(string description) : ILlmProvider
    {
        public List<LlmAttachment> Seen { get; } = [];
        public string Name => "img";
        public bool SupportsImages => true;

        public Task<LlmResponse?> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Seen.AddRange(messages.SelectMany(m => m.Attachments ?? []));
            return Task.FromResult<LlmResponse?>(new LlmResponse(description));
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["v"]);
    }

    // Conversation on c1, a distinct image-recognition model on c2.
    private static AIService BuildServiceWithImageModel(ILlmProvider conversation, ILlmProvider image)
    {
        var svc = new AIService(new WorkspaceRuntime(), Path.GetTempPath());
        svc.Register("c1", conversation);
        svc.Register("c2", image);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns = [new ProviderModelPair { Id = "c1", ProviderName = "conv", Model = "m" },
                       new ProviderModelPair { Id = "c2", ProviderName = "img",  Model = "v" }],
            Assignments = { ["Conversation"] = "c1", ["Disambiguation"] = "c1", ["ImageRecognition"] = "c2" }
        });
        return svc;
    }

    private static AIService BuildService(ILlmProvider provider)
    {
        var svc = new AIService(new WorkspaceRuntime(), Path.GetTempPath());
        svc.Register("c1", provider);   // execution instances are keyed by column id
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
    public async Task RepeatedIdenticalBatch_ReturnsStoppedMessage()
    {
        // The model keeps emitting the SAME tool call → the loop guard stops it after a few repeats.
        var tool     = new RecordingTool("get_x", ToolSafety.ReadOnly);
        var provider = new ScriptedLlmProvider(Enumerable.Repeat(ToolBlock("get_x"), 50));
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "loop", true, approver, default);

        StringAssert.Contains(res!.Text!, "repeated tool call");
        Assert.AreEqual(2, tool.Invocations);   // ran on the first two, stopped before the 3rd (MaxIdenticalBatches)
    }

    [TestMethod]
    public async Task ExemptTool_RepeatedIdentically_BypassesRepeatGuard()
    {
        // A scroll-like tool whose identical repeats are real progress: it must run past the repeat
        // guard, bounded only by the overall step cap — not stopped after 2 like a normal tool.
        var tool     = new RecordingTool("scroll", ToolSafety.ReadOnly, exempt: true);
        var provider = new ScriptedLlmProvider(Enumerable.Repeat(ToolBlock("scroll"), 50));
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "scroll", true, approver, default);

        StringAssert.Contains(res!.Text!, "max steps");   // hit the step cap, not the repeat guard
        Assert.AreEqual(20, tool.Invocations);            // MaxAgentSteps
    }

    [TestMethod]
    public async Task IterationCap_ReturnsStoppedMessage()
    {
        // Distinct calls each step (so the repeat guard never fires) → the hard step cap stops it.
        var tool     = new RecordingTool("get_x", ToolSafety.ReadOnly);
        var provider = new ScriptedLlmProvider(Enumerable.Range(0, 50).Select(i => ToolBlock("get_x", $"{{\"i\":{i}}}")));
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "loop", true, approver, default);

        StringAssert.Contains(res!.Text!, "max steps");
        Assert.AreEqual(20, tool.Invocations);   // MaxAgentSteps
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

    // ── Visual context: the capture tool feeds an image to the next turn ─────

    [TestMethod]
    public async Task ImageTool_AttachesCapturedImage_ToFollowingTurn_ForVisionModel()
    {
        var png      = new byte[] { 1, 2, 3, 4 };
        var tool     = new ImageCaptureTool(png);
        // Turn 1: the model calls the capture tool. Turn 2: it answers using the attached image.
        var provider = new AttachmentRecordingProvider([ToolBlock("capture_web_page"), "I can see it."],
                                                       supportsImages: true);

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "what's on the page?", true,
                                                   new FakeApprover(), default);

        Assert.AreEqual(1, tool.Invocations);
        Assert.AreEqual(2, provider.Calls.Count);
        Assert.IsNull(provider.Calls[0], "no image before the tool is called");
        Assert.IsNotNull(provider.Calls[1], "the captured image rides the turn after the tool ran");
        Assert.AreEqual(1, provider.Calls[1]!.Count);
        Assert.IsTrue(provider.Calls[1]![0].IsImage);
        CollectionAssert.AreEqual(png, provider.Calls[1]![0].Bytes);
    }

    [TestMethod]
    public async Task ImageTool_ImageNotSent_AndModelTold_WhenModelLacksVision()
    {
        var tool     = new ImageCaptureTool([9, 9, 9]);
        var provider = new AttachmentRecordingProvider([ToolBlock("capture_web_page"), "Okay."],
                                                       supportsImages: false);

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "what's on the page?", true,
                                                   new FakeApprover(), default);

        Assert.AreEqual(1, tool.Invocations, "the tool still runs — the model just can't see the result");
        Assert.IsNull(provider.Calls[1], "a text-only model must not be sent images");
        StringAssert.Contains(provider.LastToolResultsText, "can't view images");
    }

    [TestMethod]
    public async Task SeparateImageModel_DescribesImage_AndPassesTextToTextOnlyConversationModel()
    {
        var png   = new byte[] { 1, 2, 3 };
        var tool  = new ImageCaptureTool(png);
        // Conversation model is text-only; a distinct vision model is assigned to Image Recognition.
        var conv  = new AttachmentRecordingProvider([ToolBlock("capture_web_page"), "Got it."], supportsImages: false);
        var image = new DescribingImageProvider("A blue page titled WEBVIEW2 TEST PAGE.");

        await BuildServiceWithImageModel(conv, image)
            .RunAgentAsync(new TestPage(tool), "what's on the page?", true, new FakeApprover(), default);

        // The image model saw the screenshot...
        Assert.AreEqual(1, image.Seen.Count);
        Assert.IsTrue(image.Seen[0].IsImage);
        CollectionAssert.AreEqual(png, image.Seen[0].Bytes);

        // ...and the conversation model got the description as TEXT, with no image attachment.
        Assert.IsNull(conv.Calls[1], "the text-only conversation model must not receive the image");
        StringAssert.Contains(conv.LastToolResultsText, "WEBVIEW2 TEST PAGE");
    }
}
