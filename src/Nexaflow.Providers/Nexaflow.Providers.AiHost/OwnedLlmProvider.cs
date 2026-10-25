using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// A language model Nexaflow runs itself, in its AI host: the whole harness is Nexaflow's — the model's own chat
/// template, a context kept across turns — and nothing leaves this machine. Its files come from the asset store, and
/// on a machine the CUDA pack suits, the pack with them; the model is available once its files are here, and is held
/// loaded while an ability uses it.
/// </summary>
public sealed class OwnedLlmProvider : ILlmProvider, ILocalModelCatalog, IHostModelCatalog, ITracksModelUse, IAsyncDisposable
{
    public const string ProviderName = "Nexaflow";

    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedLlmProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup,
                            ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, setup, hardware);
        _model   = OwnedModels.OfKind(ModelKind.Language).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
        _runtime.MadeWayChanged += OnMadeWayChanged;
    }

    public string Name => ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;
    public bool SupportsImages => _model?.Images ?? false;
    public bool SupportsAudio  => _model?.Audio ?? false;
    public bool SupportsToolCalls => _model?.ToolCalls ?? false;

    public IReadOnlyList<CatalogModel> Catalog => [.. _runtime.Runnable(ModelKind.Language).Select(_runtime.CatalogOf)];

    public IReadOnlyList<UnrunnableModel> Unrunnable => [.. _runtime.Unrunnable(ModelKind.Language)];

    public LocalRuntime? RuntimeOf(string modelId) => _runtime.Runtime;

    public bool LoadsOnDemand => false;

    public IReadOnlyList<PlacementNeed> NeedsOf(string modelId)
        => OwnedModels.OfKind(ModelKind.Language).FirstOrDefault(m => m.Id == modelId) is { } model ? _runtime.NeedsOf(model) : [];

    /// <summary>Its files are here and verified.</summary>
    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public bool IsMakingWay => _model is not null && _runtime.IsMakingWay(_model);

    private void OnMadeWayChanged(object? sender, EventArgs e) => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. _runtime.Runnable(ModelKind.Language).Select(m => m.Id)]);

    /// <summary>The model, with the context its GPU gives it once it is placed.</summary>
    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult(_model is null ? null : new ModelInfo(_runtime.ContextOf(_model, OwnedRoles.Chat), _model.Catalog.DisplayName, _runtime.RepliesOf(_model, OwnedRoles.Chat)));

    /// <summary>Fetches what is missing, then has the host load the model and keep it. Never throws: a model that will
    /// not come is unavailable, and its chain moves on.</summary>
    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    /// <summary>Unloads the model from the host; the host itself stays for any other.</summary>
    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    public ModelUseTracker ModelUse => _runtime.ModelUse;

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => StreamAsync(messages, LlmUrgency.Interactive, ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, LlmUrgency urgency, CancellationToken ct = default)
        => StreamAsync(messages, [], urgency, ct);

    /// <summary>The model's reply; offered tools, a model that calls them in its own syntax is offered them in its own chat
    /// template, and its calls come back as it makes them.</summary>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                        CancellationToken ct = default)
        => StreamAsync(messages, tools, urgency, LlmThinking.Allowed, ct);

    /// <summary>The same, the model reasoning first or not as <paramref name="thinking"/> says. A reply may be as long as
    /// the model's context leaves room for; it ends with why it ended, and how full the context now is.</summary>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                        LlmThinking thinking, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{ProviderName} has no model to answer with.");
        var request = Turns(messages, _model);
        IReadOnlyList<ChatTool>? offered = _model.ToolCalls && tools.Count > 0
            ? [.. tools.Select(t => new ChatTool(t.Name, t.Description, t.Parameters.ToJsonString()))]
            : null;
        var priority = urgency == LlmUrgency.Background ? RequestPriority.Background : RequestPriority.Interactive;
        return LlmStreamRunner.RunAsync(_runtime.Activities, $"{ProviderName} ({_model.Catalog.DisplayName})…", ProviderName,
            token => Events(_model, request, offered, priority, thinking == LlmThinking.Allowed, token), ct);
    }

    /// <summary>The reply as the host writes it. A host lost before a word of it — it crashed, and the next request
    /// starts another — is asked once more; after a word, the reader has seen part of a reply and it is not.</summary>
    private async IAsyncEnumerable<LlmStreamEvent> Events(OwnedModel model, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ChatTool>? tools,
                                                          RequestPriority priority, bool thinks, [EnumeratorCancellation] CancellationToken ct)
    {
        using var use = await _runtime.ModelUse.BeginAsync(ct);
        var host = _runtime.Host;
        host.Environment = _runtime.Environment;
        var spec = await _runtime.PlacedAsync(model, OwnedRoles.Chat, ct);
        for (int attempt = 0; ; attempt++)
        {
            bool written = false;
            await using var replies = host.SendAsync(id => new Generate(id, spec, turns, MaxNewTokens: null, priority, tools, thinks, SamplingOf(model, thinks)), OwnedModelRuntime.StallLimit, ct)
                                          .GetAsyncEnumerator(ct);
            while (true)
            {
                try
                {
                    if (!await replies.MoveNextAsync()) yield break;
                }
                catch (AiHostException ex) when (ex.Failure == AiHostFailure.Lost && !written && attempt == 0)
                {
                    break;
                }
                if (replies.Current is TextDelta delta)
                {
                    written = true;
                    yield return new LlmTextDelta(delta.Text);
                }
                else if (replies.Current is ToolCallMade made)
                {
                    written = true;
                    yield return new LlmToolCallRequested(LlmToolCall.Read(made.Call.Id, made.Call.Name, made.Call.Arguments));
                }
                else if (replies.Current is GenerationEnded ended)
                {
                    yield return new LlmReplyEnded(ended.Reason switch
                    {
                        StopReason.MaxTokens   => LlmReplyEnd.LengthLimit,
                        StopReason.ContextFull => LlmReplyEnd.ContextFull,
                        StopReason.Circling    => LlmReplyEnd.Circling,
                        _                      => LlmReplyEnd.Finished,
                    }, ended.PromptTokens + ended.GeneratedTokens);
                }
            }
        }
    }

    /// <summary>Names a seed every reply's draws start from: the same requests in the same order on a fresh host then draw the
    /// same replies — for tests of the model's own behaviour. Unset, every reply draws afresh.</summary>
    internal const string SeedVariable = "NEXAFLOW_AI_SEED";

    /// <summary>How the model chooses its tokens for a request that reasons first or not, as its makers recommend; null —
    /// the likeliest each time — for a model whose makers recommend nothing, or under <see cref="GreedyVariable"/>.</summary>
    internal static TokenSampling? SamplingOf(OwnedModel model, bool thinks)
    {
        if (Environment.GetEnvironmentVariable(GreedyVariable) is { Length: > 0 }) return null;
        if (model.TokenSampling?.For(thinks) is not { } sampling) return null;
        return uint.TryParse(Environment.GetEnvironmentVariable(SeedVariable), out var seed) ? sampling with { Seed = seed } : sampling;
    }

    /// <summary>Set, every reply takes the likeliest token each time — for tests of the app's own paths through a real model,
    /// which want the same replies run after run whatever came before them.</summary>
    internal const string GreedyVariable = "NEXAFLOW_AI_GREEDY";

    /// <summary>The conversation as the host takes it: images and recordings the model reads go as media, where it reads
    /// them; any other attachment is named in the text, as for any provider that cannot read it. A turn's tool calls, and
    /// what answers one, go with it.</summary>
    internal static IReadOnlyList<ChatTurn> Turns(IReadOnlyList<LlmMessage> messages, OwnedModel model)
        => [.. messages.Select(m =>
        {
            var role = m.Role switch
            {
                LlmRole.System    => TurnRole.System,
                LlmRole.Assistant => TurnRole.Assistant,
                LlmRole.Tool      => TurnRole.Tool,
                _                 => TurnRole.User,
            };
            var read = (m.Attachments ?? []).Where(a => (a.IsImage && model.Images) || (a.IsAudio && model.Audio)).ToList();
            var named = (m.Attachments ?? []).Except(read).Select(a => $"[attached: {a.FilePath}]").ToList();
            string text = named.Count == 0 ? m.Text : m.Text + "\n" + string.Join("\n", named);
            return new ChatTurn(role, text, read.Count == 0 ? null
                : [.. read.Select(a => new ChatMedia(a.IsImage ? MediaKind.Image : MediaKind.Audio, a.Bytes is null ? a.FilePath : null, a.Bytes))])
            {
                ToolCalls = m.ToolCalls is { Count: > 0 } calls ? [.. calls.Select(Call)] : null,
                Answers   = m.Answers is { } answered ? Call(answered) : null,
            };
        })];

    private static ChatToolCall Call(LlmToolCall call) => new(call.Id, call.Name, call.Arguments.ToJsonString());

    public ValueTask DisposeAsync()
    {
        _runtime.Assets.Changed -= OnAssetChanged;
        _runtime.MadeWayChanged -= OnMadeWayChanged;
        return ValueTask.CompletedTask;
    }
}
