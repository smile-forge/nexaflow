using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexaflow.Core.Services.ShellAi;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Core;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

[TestClass]
[CoversNode("chat-interface")]
public class AgentLoopTests
{
    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class ScriptedLlmProvider(IEnumerable<string> responses, string rankReply = "1,2,3,4") : ILlmProvider
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> SystemPrompts { get; } = [];

        /// <summary>Every conversation this model was asked to continue, tool ranking aside.</summary>
        public List<IReadOnlyList<LlmMessage>> Conversations { get; } = [];

        /// <summary>Every request this model was sent, tool ranking included.</summary>
        public int Calls { get; private set; }

        public string        ProviderName { get; init; } = "fake";
        public DataResidency Residency    { get; init; } = DataResidency.LeavesMachine;
        public bool          IsAvailable  { get; init; } = true;

        /// <summary>Which model this one, judging, sends a request to.</summary>
        public string Judgement { get; init; } = "large";

        public string Name => ProviderName;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            var lastUser = messages.LastOrDefault(m => m.Role == LlmRole.User)?.Text ?? string.Empty;
            if (lastUser.Contains("Most relevant tool numbers"))
                return Streamed(rankReply, ct);
            if (lastUser.Contains("Which assistant should take it?"))
                return Streamed(Judgement, ct);

            SystemPrompts.Add(messages.FirstOrDefault(m => m.Role == LlmRole.System)?.Text ?? string.Empty);
            Conversations.Add([.. messages]);
            return Streamed(_responses.Count > 0 ? _responses.Dequeue() : "done", ct);
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["m"]);
    }

    /// <summary>A reply the way a model sends it: a few characters at a time, so a fence can be cut anywhere.</summary>
    private static async IAsyncEnumerable<LlmStreamEvent> Streamed(
        string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < text.Length; i += 5)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new LlmTextDelta(text.Substring(i, Math.Min(5, text.Length - i)));
        }
    }

    private sealed class FakeApprover : IAIResponseHandler
    {
        public bool BatchResult = true;
        public bool PlanResult  = true;
        public int  BatchRequests;
        public int  PlanRequests;
        public string? Final;
        public string? LastExplanation;

        /// <summary>Every draft shown, in order, and how each draft ended.</summary>
        public List<string> Drafts { get; } = [];
        public List<bool>   DraftEnds { get; } = [];

        public bool ContextReleaseResult;
        public List<IReadOnlyList<string>> ContextReleaseRequests { get; } = [];

        public Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
        { ContextReleaseRequests.Add(destinations); return Task.FromResult(ContextReleaseResult); }

        public void BeginResponse(string userText) { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        { BatchRequests++; LastExplanation = explanation; return Task.FromResult(BatchResult); }
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        { PlanRequests++; return Task.FromResult(PlanResult); }
        public void ReportProgress(string message) { }
        public void ShowReplyDraft(string draftMarkdown) => Drafts.Add(draftMarkdown);
        public void EndReplyDraft(bool keep) => DraftEnds.Add(keep);
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

    /// <summary>A page whose contents are as sensitive as <paramref name="risk"/> says — a process list, say.</summary>
    private sealed class RiskyPage(ContextSecurityRisk risk, params IClientTool[] tools) : IPageViewModel
    {
        public const string Contents = "PROCESS LIST: lsass.exe 712";

        public string GetContext() => Contents;
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
        public ContextSecurityRisk GetContextSecurityRisk() => risk;
    }

    // Records the attachments handed to each completion turn, and reports an image capability.
    private sealed class AttachmentRecordingProvider(IEnumerable<string> responses, bool supportsImages) : ILlmProvider
    {
        private readonly Queue<string> _responses = new(responses);
        public List<IReadOnlyList<LlmAttachment>?> Calls { get; } = [];
        public string LastToolResultsText { get; private set; } = string.Empty;

        public string Name => "fake";
        public bool SupportsImages => supportsImages;
        public DataResidency Residency => DataResidency.LeavesMachine;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var lastUser = messages.LastOrDefault(m => m.Role == LlmRole.User);
            var lastText = lastUser?.Text ?? string.Empty;
            if (lastText.Contains("Most relevant tool numbers"))
                return Streamed("1,2,3,4", ct);

            LastToolResultsText = lastText;
            Calls.Add(lastUser?.Attachments);   // attachments ride the message, not a side parameter
            return Streamed(_responses.Count > 0 ? _responses.Dequeue() : "done", ct);
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
        public ToolSafety Safety => ToolSafety.SafeOperation;
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
        public DataResidency Residency => DataResidency.LeavesMachine;

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            Seen.AddRange(messages.SelectMany(m => m.Attachments ?? []));
            return Streamed(description, ct);
        }

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["v"]);
    }

    /// <summary>
    /// An AIService on a runtime that carries both live services, the way
    /// <c>WorkspaceManager.BootstrapServices</c> leaves one before any feature sees it.
    /// <para>
    /// It matters here because the agent's page catalog is built from this runtime
    /// (<c>get_openable_pages</c> → <c>GetPageCatalog</c>), and that catalog is what tells the agent which
    /// pages it may open. A half-wired runtime silently dropped every registration needing a service it
    /// lacked — so the agent was told AI Chat did not exist.
    /// </para>
    /// </summary>
    private static AIService NewServiceOnAWiredRuntime()
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, TestTemp.Root);
        Set(nameof(WorkspaceRuntime.AiService), svc);
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));
        return svc;

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    // Conversation on c1, a distinct image-recognition model on c2.
    private static AIService BuildServiceWithImageModel(ILlmProvider conversation, ILlmProvider image)
    {
        var svc = NewServiceOnAWiredRuntime();
        svc.Register("c1", conversation);
        svc.Register("c2", image);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns = [new ProviderModelPair { Id = "c1", ProviderName = "conv", Model = "m" },
                       new ProviderModelPair { Id = "c2", ProviderName = "img",  Model = "v" }],
            Assignments =
            {
                ["GeneralIntelligence"]     = AbilityChain.Of("c1"),
                ["Disambiguation"]   = AbilityChain.Of("c1"),
                ["ImageRecognition"] = AbilityChain.Of("c2"),
            }
        });
        return svc;
    }

    private static AIService BuildService(ILlmProvider provider)
    {
        var svc = NewServiceOnAWiredRuntime();
        svc.Register("c1", provider);   // execution instances are keyed by column id
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = "fake", Model = "m" }],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("c1"), ["Disambiguation"] = AbilityChain.Of("c1") }
        });
        return svc;
    }

    /// <summary>A service whose Conversation and Disambiguation abilities are both served by
    /// <paramref name="chain"/>, in that order.</summary>
    private static AIService BuildChainService(params (string Id, ILlmProvider Provider)[] chain)
    {
        var svc = NewServiceOnAWiredRuntime();
        foreach (var (id, provider) in chain) svc.Register(id, provider);

        var ids = chain.Select(c => c.Id).ToArray();
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [.. chain.Select(c => new ProviderModelPair { Id = c.Id, ProviderName = c.Provider.Name, Model = "m" })],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of(ids), ["Disambiguation"] = AbilityChain.Of(ids) }
        });
        return svc;
    }

    private static string ToolBlock(string tool, string args = "{}")
        => $"```client_tool\n{{\"tool\":\"{tool}\",\"arguments\":{args}}}\n```";

    // ── Tests ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReadOnlyTool_AutoRuns_WithoutApproval_AndFinishes()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.SafeOperation);
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
    public async Task Answer_IsShownAsItIsWritten_ThenReplacedByTheFinal()
    {
        const string answer = "The build failed because the test project no longer compiles.";
        var approver = new FakeApprover();

        await BuildService(new ScriptedLlmProvider([answer])).RunAgentAsync(new TestPage(), "why", true, approver, default);

        Assert.IsTrue(approver.Drafts.Count > 0, "the reply was not shown while it was written");
        Assert.IsTrue(approver.Drafts.All(d => answer.StartsWith(d, StringComparison.Ordinal)),
            "every draft is the reply so far");
        Assert.AreEqual(0, approver.DraftEnds.Count, "the final answer takes the draft's place; it is not ended");
        Assert.AreEqual(answer, approver.Final);
    }

    [TestMethod]
    public async Task ARunWithOptions_ContinuesItsConversation_WritesInTheStyleAsked_AndAlwaysOffersItsOwnTools()
    {
        var provider = new ScriptedLlmProvider(["Three o'clock."]);
        var pageTools = Enumerable.Range(1, 6).Select(i => (IClientTool)new RecordingTool($"page_tool_{i}", ToolSafety.SafeOperation)).ToArray();
        var options = new AgentRunOptions
        {
            History    = [new(LlmRole.User, "Hello."), new(LlmRole.Assistant, "Hi —")],
            ReplyStyle = "Speak briefly.",
            Tools      = [new RecordingTool("end_voice_session", ToolSafety.SafeOperation)],
        };

        await BuildService(provider).RunAgentAsync(new TestPage(pageTools), "What time is it?", true, new FakeApprover(), options);

        var conversation = provider.Conversations.Single();
        CollectionAssert.AreEqual(new[] { LlmRole.System, LlmRole.User, LlmRole.Assistant, LlmRole.User }, conversation.Select(m => m.Role).ToArray());
        Assert.AreEqual("Hi —", conversation[2].Text, "what was said before comes between the instructions and the question");
        var system = conversation[0].Text;
        Assert.IsTrue(system.IndexOf("Speak briefly.", StringComparison.Ordinal) is var style and > 0
                      && style < system.IndexOf("# What the user is looking at", StringComparison.Ordinal),
                      "how to reply comes straight after who it is, before what the user is looking at");
        StringAssert.Contains(conversation[0].Text, "end_voice_session", "the caller's own tool is offered though the page's were ranked down");
        Assert.IsFalse(conversation[0].Text.Contains("page_tool_6"), "the page's tools were ranked: four of six");
    }

    [TestMethod]
    public async Task ProseBeforeATool_StandsAsNarration_AndTheToolBlockIsNeverShown()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.SafeOperation);
        var provider = new ScriptedLlmProvider(["Let me look at the file.\n" + ToolBlock("get_x"), "Found it."]);
        var approver = new FakeApprover();

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "look", true, approver, default);

        Assert.IsFalse(approver.Drafts.Any(d => d.Contains("client_tool") || d.Contains("get_x")),
            "a block the agent acts on leaked into a draft");
        CollectionAssert.AreEqual(new[] { true }, approver.DraftEnds);
        Assert.IsTrue(approver.Drafts.Contains("Let me look at the file."), "the narration was not shown whole");
        Assert.AreEqual("Found it.", approver.Final);
    }

    [TestMethod]
    public async Task ProseBeforeAnApproval_GivesWayToTheApprovalPrompt()
    {
        var tool     = new RecordingTool("write_x", ToolSafety.RequiresApproval);
        var provider = new ScriptedLlmProvider(["I'll write the file.\n" + ToolBlock("write_x"), "Written."]);
        var approver = new FakeApprover();

        await BuildService(provider).RunAgentAsync(new TestPage(tool), "write", true, approver, default);

        CollectionAssert.AreEqual(new[] { false }, approver.DraftEnds);
        Assert.AreEqual("I'll write the file.", approver.LastExplanation);
    }

    [TestMethod]
    public async Task AMalformedBlock_TakesItsDraftAway_ForTheRetry()
    {
        var provider = new ScriptedLlmProvider(["Trying.\n```client_tool\n{not json\n```", "Answered."]);
        var approver = new FakeApprover();

        await BuildService(provider).RunAgentAsync(new TestPage(), "go", true, approver, default);

        CollectionAssert.AreEqual(new[] { false }, approver.DraftEnds);
        Assert.AreEqual("Answered.", approver.Final);
    }

    // ── Chains and where a page's contents go ─────────────────────────────

    [TestMethod]
    public async Task AChain_PassesOverAModelThatIsNotAvailable()
    {
        var first  = new ScriptedLlmProvider(["from the first"])  { IsAvailable = false };
        var second = new ScriptedLlmProvider(["from the second"]);

        var res = await BuildChainService(("c1", first), ("c2", second))
            .RunAgentAsync(new TestPage(), "q", true, new FakeApprover(), default);

        Assert.AreEqual("from the second", res!.Text);
        Assert.AreEqual(0, first.Calls);
    }

    [TestMethod]
    public async Task AHighRiskPage_StaysWithTheChainsModel_AndAsksBeforeItsContentsLeave()
    {
        var cloud    = new ScriptedLlmProvider(["from the cloud"]) { ProviderName = "Claude" };
        var local    = new ScriptedLlmProvider(["from here"]) { ProviderName = "Ollama", Residency = DataResidency.StaysOnMachine };
        var approver = new FakeApprover { ContextReleaseResult = true };

        var res = await BuildChainService(("c1", cloud), ("c2", local))
            .RunAgentAsync(new RiskyPage(ContextSecurityRisk.High), "q", true, approver, default);

        Assert.AreEqual("from the cloud", res!.Text, "the page's risk chose the model, not the chain");
        Assert.AreEqual(0, local.Calls);
        CollectionAssert.AreEqual(new[] { "Claude" }, approver.ContextReleaseRequests.Single().ToArray());
        StringAssert.Contains(cloud.SystemPrompts.Single(), RiskyPage.Contents);
    }

    [TestMethod]
    public async Task AHighRiskPage_OnAModelWhoseDataStaysHere_IsNotAskedAbout()
    {
        var local    = new ScriptedLlmProvider(["from here"]) { ProviderName = "Ollama", Residency = DataResidency.StaysOnMachine };
        var approver = new FakeApprover();

        var res = await BuildChainService(("c1", local))
            .RunAgentAsync(new RiskyPage(ContextSecurityRisk.High), "q", true, approver, default);

        Assert.AreEqual("from here", res!.Text);
        Assert.AreEqual(0, approver.ContextReleaseRequests.Count, "nothing was leaving, so nothing needed asking");
    }

    [TestMethod]
    public async Task AHighRiskPage_TheUserDoesNotSend_IsCancelled_AndNothingLeaves()
    {
        var cloud    = new ScriptedLlmProvider(["answer"]) { ProviderName = "Claude" };
        var approver = new FakeApprover { ContextReleaseResult = false };

        var res = await BuildChainService(("c1", cloud))
            .RunAgentAsync(new RiskyPage(ContextSecurityRisk.High), "q", true, approver, default);

        Assert.IsNull(res, "a refused request does not run");
        Assert.AreEqual(0, cloud.Calls, "the model was sent something");
        Assert.IsNull(approver.Final);
    }

    [TestMethod]
    public async Task APageThatIsNotHighRisk_IsNeverAskedAbout()
    {
        var cloud    = new ScriptedLlmProvider(["answer"]) { ProviderName = "Claude" };
        var approver = new FakeApprover();

        await BuildChainService(("c1", cloud))
            .RunAgentAsync(new RiskyPage(ContextSecurityRisk.Medium), "q", true, approver, default);

        Assert.AreEqual(0, approver.ContextReleaseRequests.Count);
        StringAssert.Contains(cloud.SystemPrompts.Single(), RiskyPage.Contents);
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
        var tool     = new RecordingTool("get_x", ToolSafety.SafeOperation);
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
        var tool     = new RecordingTool("scroll", ToolSafety.SafeOperation, exempt: true);
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
        var tool     = new RecordingTool("get_x", ToolSafety.SafeOperation);
        var provider = new ScriptedLlmProvider(Enumerable.Range(0, 50).Select(i => ToolBlock("get_x", $"{{\"i\":{i}}}")));
        var approver = new FakeApprover();

        var res = await BuildService(provider).RunAgentAsync(new TestPage(tool), "loop", true, approver, default);

        StringAssert.Contains(res!.Text!, "max steps");
        Assert.AreEqual(20, tool.Invocations);   // MaxAgentSteps
    }

    [TestMethod]
    public async Task PreCancelledToken_ReturnsNull()
    {
        var tool     = new RecordingTool("get_x", ToolSafety.SafeOperation);
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
                                 .Select(i => (IClientTool)new RecordingTool($"tool_{i}", ToolSafety.SafeOperation))
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
    public async Task ManyTools_AreChosenAgain_OnceAStepsCallsAreAnswered()
    {
        var tools    = Enumerable.Range(1, 6)
                                 .Select(i => (IClientTool)new RecordingTool($"tool_{i}", ToolSafety.SafeOperation))
                                 .ToArray();
        var provider = new ScriptedLlmProvider([ToolBlock("tool_1"), "done"], rankReply: "1,2");

        await BuildService(provider).RunAgentAsync(new TestPage(tools), "hi", true, new FakeApprover(), default);

        Assert.AreEqual(2, provider.Conversations.Count);
        Assert.AreEqual(2, provider.Calls - provider.Conversations.Count, "one choice for the instruction, one once tool_1 was answered");
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

    [TestMethod]
    public async Task ThePagesItCanOpen_AreAskedFor_NotCarriedInEveryRequest()
    {
        var provider = new ScriptedLlmProvider(["Done."]);
        var svc      = BuildService(provider);
        var runtime  = (WorkspaceRuntime)typeof(AIService).GetField("_workspace", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(svc)!;

        await svc.RunAgentAsync(new TestPage([]), "Open the AI chat.", true, new FakeApprover());
        var pages = await new GetOpenablePagesTool(runtime).InvokeAsync([], CancellationToken.None);

        var system = provider.Conversations.Single()[0].Text;
        Assert.IsFalse(system.Contains("Openable pages"), "the catalogue is not in the prompt");
        StringAssert.Contains(system, "get_openable_pages", "the tool that lists it is offered");
    foreach (var (kind, _) in FeatureManager.Instance.GetPageCatalog(runtime))
            StringAssert.Contains(pages.ModelText, $"- {kind}", "the tool lists every page the runtime can open");
    }
}
