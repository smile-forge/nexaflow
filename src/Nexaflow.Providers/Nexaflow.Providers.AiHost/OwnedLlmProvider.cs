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
public sealed class OwnedLlmProvider : ILlmProvider, ILocalModelCatalog, IAsyncDisposable
{
    public const string ProviderName = "Nexaflow";

    /// <summary>The longest a reply may run before it is cut off.</summary>
    internal const int MaxReplyTokens = 4096;

    private readonly OwnedModelRuntime _runtime;
    private readonly OwnedModel?       _model;

    public OwnedLlmProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host,
                            ComputeHardwareReport hardware, ProviderModel model)
    {
        _runtime = new OwnedModelRuntime(activities, assets, host, hardware);
        _model   = OwnedModels.OfKind(ModelKind.Language).FirstOrDefault(m => m.Id == model.Model);
        assets.Changed += OnAssetChanged;
    }

    public string Name => ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;
    public bool SupportsImages => _model?.Images ?? false;
    public bool SupportsAudio  => _model?.Audio ?? false;
    public bool SupportsToolCalls => _model?.ToolCalls ?? false;

    public IReadOnlyList<CatalogModel> Catalog => [.. OwnedModels.OfKind(ModelKind.Language).Select(_runtime.CatalogOf)];

    /// <summary>Its files are here and verified.</summary>
    public bool IsAvailable => _model is not null && _runtime.IsHere(_model);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _runtime.IsFileOf(_model, asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. OwnedModels.OfKind(ModelKind.Language).Select(m => m.Id)]);

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult(_model is null ? null : new ModelInfo(_model.Part(OwnedRoles.Chat, _runtime.ExecutionProvider).ContextTokens, _model.Catalog.DisplayName));

    /// <summary>Fetches what is missing, then has the host load the model and keep it. Never throws: a model that will
    /// not come is unavailable, and its chain moves on.</summary>
    public Task WarmupAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.WarmupAsync(_model, ct);

    /// <summary>Unloads the model from the host; the host itself stays for any other.</summary>
    public Task CooldownAsync(CancellationToken ct = default) => _model is null ? Task.CompletedTask : _runtime.CooldownAsync(_model, ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => StreamAsync(messages, LlmUrgency.Interactive, ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, LlmUrgency urgency, CancellationToken ct = default)
        => StreamAsync(messages, [], urgency, ct);

    /// <summary>The model's reply; offered tools, a model that calls them in its own syntax is offered them in its own chat
    /// template, and its calls come back as it makes them.</summary>
    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                        CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{ProviderName} has no model to answer with.");
        var request = Turns(messages, _model);
        IReadOnlyList<ChatTool>? offered = _model.ToolCalls && tools.Count > 0
            ? [.. tools.Select(t => new ChatTool(t.Name, t.Description, t.Parameters.ToJsonString()))]
            : null;
        var priority = urgency == LlmUrgency.Background ? RequestPriority.Background : RequestPriority.Interactive;
        return LlmStreamRunner.RunAsync(_runtime.Activities, $"{ProviderName} ({_model.Catalog.DisplayName})…", ProviderName,
            token => Events(_model, request, offered, priority, token), ct);
    }

    /// <summary>The reply as the host writes it. A host lost before a word of it — it crashed, and the next request
    /// starts another — is asked once more; after a word, the reader has seen part of a reply and it is not.</summary>
    private async IAsyncEnumerable<LlmStreamEvent> Events(OwnedModel model, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ChatTool>? tools,
                                                          RequestPriority priority, [EnumeratorCancellation] CancellationToken ct)
    {
        var host = _runtime.Host;
        host.Environment = _runtime.Environment;
        var spec = _runtime.Spec(model, OwnedRoles.Chat);
        for (int attempt = 0; ; attempt++)
        {
            bool written = false;
            await using var replies = host.SendAsync(id => new Generate(id, spec, turns, MaxReplyTokens, priority, tools), OwnedModelRuntime.StallLimit, ct)
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
            }
        }
    }

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
        return ValueTask.CompletedTask;
    }
}
