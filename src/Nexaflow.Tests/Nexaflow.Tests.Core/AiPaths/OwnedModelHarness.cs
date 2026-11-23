using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// The real agent loop on Nexaflow's own model, where a machine has it: <c>NEXAFLOW_TEST_STORE</c> names the test model store,
/// laid out as the AI host keeps it, holding Gemma 4 E4B — and, for the GPU, the CUDA pack — which a host of the harness keeps,
/// downloading nothing; the provider runs the model there as the app does. One host and one loaded model serve every test of a run.
/// </summary>
internal static class OwnedModelHarness
{
    /// <summary>The variable naming the test model store, which the AI host suites share.</summary>
    private const string StoreVariable = "NEXAFLOW_TEST_STORE";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static AiHostClient? _host;
    private static ILlmProvider? _model;

    /// <summary>The machine's setup the model is held through, once it is loaded.</summary>
    public static HostModelSetup? Setup { get; private set; }

    /// <summary>The loaded model, or an inconclusive test on a machine without it.</summary>
    public static async Task<ILlmProvider> ModelAsync()
    {
        if (Environment.GetEnvironmentVariable(StoreVariable) is not { Length: > 0 } root)
        {
            Assert.Inconclusive($"{StoreVariable} names no model store.");
            throw new InvalidOperationException();   // Inconclusive throws; this is never reached
        }

        await Gate.WaitAsync();
        try
        {
            if (_model is not null) return _model;

            // These tests check the app's own paths through the model, not the model: it takes the likeliest token each
            // time, so a run gives the same replies as the last, whatever its sampling would draw.
            Environment.SetEnvironmentVariable("NEXAFLOW_AI_GREEDY", "1");
            _host = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"),
                                                               store: root, downloads: false));
            var store = new AiHostModelStore(_host);
            await store.RefreshAsync();
            Setup = new HostModelSetup(_host);
            var provider = new OwnedLlmProvider(new Activities(), store, _host, Setup, MachineCards.Probe(), new ProviderModel("gemma-4-e4b-it"));
            if (!provider.IsAvailable) Assert.Inconclusive($"{root} does not hold Gemma 4 E4B's files.");
            await provider.WarmupAsync();
            return _model = provider;
        }
        finally { Gate.Release(); }
    }

    public static async Task StopAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        _host = null;
        _model = null;
        Setup = null;
    }

    /// <summary>An AIService whose every language role is <paramref name="model"/>.</summary>
    public static AIService Service(ILlmProvider model)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, TestTemp.Root);
        Set(nameof(WorkspaceRuntime.AiService), svc);
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));
        svc.Register("c1", model);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = model.Name, Model = "gemma-4-e4b-it" }],
            Assignments =
            {
                ["GeneralIntelligence"] = AbilityChain.Of("c1"),
                ["Disambiguation"]   = AbilityChain.Of("c1"),
                ["ImageRecognition"] = AbilityChain.Of("c1"),
            },
        });
        return svc;

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    private sealed class Activities : IBackgroundActivityManager
    {
        public IActivityHandle StartActivity(string label) => new Handle();
        private sealed class Handle : IActivityHandle
        {
            public void Update(string label) { }
            public void Complete() { }
            public void Fail(string? reason = null) { }
        }
    }
}

/// <summary>
/// Everything the model was asked and answered in a run, around the real provider — how a test tells a run that ended
/// cleanly from one that hit the step cap, the repeat guard, or a tool block it could not write.
/// </summary>
internal sealed class RecordingModel(ILlmProvider inner) : ILlmProvider
{
    public List<(IReadOnlyList<LlmMessage> Asked, string Answered)> Turns { get; } = [];

    public string Name => inner.Name;
    public DataResidency Residency => inner.Residency;
    public ModelHarness Harness => inner.Harness;
    public bool SupportsImages => inner.SupportsImages;
    public bool SupportsAudio => inner.SupportsAudio;
    public bool SupportsToolCalls => inner.SupportsToolCalls;
    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => inner.GetAvailableModelsAsync(ct);
    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default) => inner.GetModelInfoAsync(ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => StreamAsync(messages, LlmUrgency.Interactive, ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, LlmUrgency urgency, CancellationToken ct = default)
        => StreamAsync(messages, [], urgency, ct);

    /// <summary>The reply, recorded as the model wrote it: its prose, then each call it made in its own protocol.</summary>
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        var answer = new StringBuilder();
        await foreach (var e in inner.StreamAsync(messages, tools, urgency, ct))
        {
            if (e is LlmTextDelta delta) answer.Append(delta.Text);
            if (e is LlmToolCallRequested call) answer.Append($"\n[call {call.Call.Name} {call.Call.Arguments.ToJsonString()}]");
            yield return e;
        }
        Turns.Add(([.. messages], answer.ToString()));
    }

    /// <summary>Turns where the harness had to tell the model its tool block could not be read.</summary>
    public int MalformedBlocks => Turns.Count(t => t.Asked[^1].Text.Contains("Fix the block and try again"));

    /// <summary>Every model turn of the run, for a failure's message.</summary>
    public string Transcript => string.Join("\n---\n", Turns.Select(t => $"> {Last(t.Asked)}\n< {t.Answered}"));

    private static string Last(IReadOnlyList<LlmMessage> asked)
    {
        var text = asked[^1].Text;
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}

/// <summary>Approves or declines every tool batch as the test says, and records what it was asked.</summary>
internal sealed class Approver(bool approve = true) : IAIResponseHandler
{
    public List<IReadOnlyList<string>> Batches { get; } = [];
    public string? Final { get; private set; }

    public void BeginResponse(string userText) { }

    public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        Batches.Add([.. batch.Select(c => c.Tool)]);
        return Task.FromResult(approve);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(approve);
    public void ReportProgress(string message) { }
    public void ShowFinal(string finalMarkdown) => Final = finalMarkdown;
    public void Abort() { }
}

/// <summary>Which of English, French and German a reply is written in, by its commonest small words.</summary>
internal static class ReplyLanguage
{
    private static readonly Dictionary<string, string[]> Words = new()
    {
        ["en"] = ["the", "is", "and", "of", "to", "a", "in", "it", "you", "that"],
        ["fr"] = ["le", "la", "les", "est", "et", "de", "des", "un", "une", "du", "vous", "que"],
        ["de"] = ["der", "die", "das", "ist", "und", "ein", "eine", "nicht", "sie", "ich", "mit", "von"],
    };

    public static string Of(string text)
    {
        var tokens = text.ToLowerInvariant().Split([' ', '\n', ',', '.', '!', '?', ':', ';', '\'', '’', '"'], StringSplitOptions.RemoveEmptyEntries);
        return Words.MaxBy(w => tokens.Count(w.Value.Contains)).Key;
    }
}
