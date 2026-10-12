using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// The real agent loop on Nexaflow's own model, where a machine has it: <c>NEXAFLOW_TEST_GEMMA</c> names a folder
/// holding Gemma 4 E4B as its catalog lays it out, and <c>NEXAFLOW_TEST_RUNTIME_PACK</c>, when set, the CUDA pack it
/// runs on. One host and one loaded model serve every test of a run.
/// </summary>
internal static class OwnedModelHarness
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static AiHostClient? _host;
    private static ILlmProvider? _model;

    /// <summary>The loaded model, or an inconclusive test on a machine without it.</summary>
    public static async Task<ILlmProvider> ModelAsync()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_GEMMA") is not { Length: > 0 } folder)
        {
            Assert.Inconclusive("NEXAFLOW_TEST_GEMMA names no Gemma 4 E4B folder on this machine.");
            throw new InvalidOperationException();   // Inconclusive throws; this is never reached
        }

        await Gate.WaitAsync();
        try
        {
            if (_model is not null) return _model;

            var aiDir = Directory.CreateTempSubdirectory("nexaflow-aipaths-").FullName;
            _host = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir));
            var store = new FolderStore(folder, Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK"));
            var provider = new OwnedLlmProvider(new Activities(), store, _host, ComputeHardware.Probe(), new ProviderModel("gemma-4-e4b-it"));
            if (!provider.IsAvailable) Assert.Inconclusive($"{folder} does not hold Gemma 4 E4B's files.");
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
    }

    /// <summary>An AIService whose every language role is <paramref name="model"/>.</summary>
    public static AIService Service(ILlmProvider model)
    {
        var ws  = new WorkspaceRuntime();
        var svc = new AIService(ws, Path.GetTempPath());
        Set(nameof(WorkspaceRuntime.AiService), svc);
        Set(nameof(WorkspaceRuntime.ShellServices), new ShellServices(ws));
        svc.Register("c1", model);
        svc.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "c1", ProviderName = model.Name, Model = "gemma-4-e4b-it" }],
            Assignments =
            {
                ["Conversation"]     = AbilityChain.Of("c1"),
                ["Disambiguation"]   = AbilityChain.Of("c1"),
                ["ProblemSolving"]   = AbilityChain.Of("c1"),
                ["Analysis"]         = AbilityChain.Of("c1"),
                ["ImageRecognition"] = AbilityChain.Of("c1"),
            },
        });
        return svc;

        void Set(string property, object value) =>
            typeof(WorkspaceRuntime).GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(ws, [value]);
    }

    /// <summary>The model's files where the test machine keeps them: <c>models/&lt;id&gt;/…</c> in its folder, and the
    /// CUDA pack's files in the pack folder. Present is enough — the test machine's copies are trusted.</summary>
    private sealed class FolderStore(string modelFolder, string? pack) : IModelAssetStore
    {
        public event EventHandler<ModelAsset>? Changed { add { } remove { } }

        public string? Locate(ModelAsset asset)
        {
            var parts = asset.RelativePath.Split('/');
            string? path = parts switch
            {
                ["models", _, .. var rest]       => Path.Combine([modelFolder, .. rest]),
                ["runtime", _, var file] when pack is not null => Path.Combine(pack, file),
                _ => null,
            };
            return path is not null && File.Exists(path) ? path : null;
        }

        public Task<string> EnsureAsync(ModelAsset asset, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(Locate(asset) ?? throw new FileNotFoundException("The test machine has no copy.", asset.RelativePath));

        public async Task<IReadOnlyList<string>> EnsureAllAsync(IReadOnlyList<ModelAsset> assets, IProgress<AssetProgress>? progress = null, CancellationToken ct = default)
        {
            var paths = new List<string>();
            foreach (var asset in assets) paths.Add(await EnsureAsync(asset, progress, ct));
            return paths;
        }
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
    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => inner.GetAvailableModelsAsync(ct);
    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default) => inner.GetModelInfoAsync(ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => StreamAsync(messages, LlmUrgency.Interactive, ct);

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, LlmUrgency urgency,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        var answer = new StringBuilder();
        await foreach (var e in inner.StreamAsync(messages, urgency, ct))
        {
            if (e is LlmTextDelta delta) answer.Append(delta.Text);
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
