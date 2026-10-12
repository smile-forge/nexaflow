using System.Runtime.CompilerServices;
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

    /// <summary>How long a request may go without a word from the host — a load takes seconds, a token milliseconds.</summary>
    private static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    private readonly IBackgroundActivityManager _activities;
    private readonly IModelAssetStore           _assets;
    private readonly IAiHost                    _host;
    private readonly ComputeHardwareReport      _hardware;
    private readonly OwnedModel?                _model;

    public OwnedLlmProvider(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host,
                            ComputeHardwareReport hardware, ProviderModel model)
    {
        _activities = activities;
        _assets     = assets;
        _host       = host;
        _hardware   = hardware;
        _model      = OwnedModels.All.FirstOrDefault(m => m.Id == model.Model);
        _assets.Changed += OnAssetChanged;
    }

    public string Name => ProviderName;
    public ModelHarness Harness => ModelHarness.Owned;
    public DataResidency Residency => DataResidency.StaysOnMachine;
    public bool SupportsImages => _model?.Images ?? false;
    public bool SupportsAudio  => _model?.Audio ?? false;

    public IReadOnlyList<CatalogModel> Catalog => [.. OwnedModels.All.Select(m => m.Catalog)];

    /// <summary>Its files are here and verified.</summary>
    public bool IsAvailable => _model is not null && _model.Catalog.Files.All(f => _assets.Locate(f) is not null);

    public event EventHandler? AvailabilityChanged;

    private void OnAssetChanged(object? sender, ModelAsset asset)
    {
        if (_model is not null && _model.Catalog.Files.Contains(asset)) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([.. OwnedModels.All.Select(m => m.Id)]);

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult(_model is null ? null : new ModelInfo(_model.ContextTokens, _model.Catalog.DisplayName));

    /// <summary>The runtime the host should run on: the CUDA pack, where it suits this machine and is here.</summary>
    private AiHostEnvironment Environment
        => CudaRuntimePack.Applies(_hardware) && CudaRuntimePack.Locate(_assets) is { } pack ? new AiHostEnvironment(pack) : AiHostEnvironment.Cpu;

    /// <summary>
    /// Fetches what is missing — the model's files, and the CUDA pack where it suits this machine — reporting how far
    /// it has got, then has the host load the model and keep it. Never throws: a model that will not come is
    /// unavailable, and its chain moves on.
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (_model is null) return;
        if (!await FetchAsync(_model, ct)) return;

        try
        {
            _host.Environment = Environment;
            await foreach (var _ in _host.SendAsync(id => new LoadModel(id, Spec(_model)), StallLimit, ct)) { }
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException)
        {
            // A model that will not load says why when a request asks for it.
        }
    }

    private async Task<bool> FetchAsync(OwnedModel model, CancellationToken ct)
    {
        var files = model.Catalog.Files.ToList();
        if (CudaRuntimePack.Applies(_hardware)) files.AddRange(CudaRuntimePack.Files);
        if (files.All(f => _assets.Locate(f) is not null)) return true;

        var activity = _activities.StartActivity($"Downloading {model.Catalog.DisplayName}…");
        var lastPercent = -1;
        var progress = new Progress<AssetProgress>(p =>
        {
            var percent = (int)(p.Fraction * 100);
            if (percent == lastPercent) return;
            lastPercent = percent;
            activity.Update($"Downloading {model.Catalog.DisplayName}… {percent}%");
        });
        try
        {
            await _assets.EnsureAllAsync(files, progress, ct);
            activity.Complete();
            return true;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);   // the model stays unavailable until a later warm-up fetches it
            return false;
        }
    }

    /// <summary>Unloads the model from the host; the host itself stays for any other.</summary>
    public async Task CooldownAsync(CancellationToken ct = default)
    {
        if (_model is null) return;
        try
        {
            await foreach (var _ in _host.SendAsync(id => new UnloadModel(id, _model.Id), StallLimit, ct)) { }
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException) { }
    }

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => StreamAsync(messages, LlmUrgency.Interactive, ct);

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, LlmUrgency urgency, CancellationToken ct = default)
    {
        if (_model is null) throw new LlmProviderException($"{ProviderName} has no model to answer with.");
        var request = Turns(messages, _model);
        var priority = urgency == LlmUrgency.Background ? RequestPriority.Background : RequestPriority.Interactive;
        return LlmStreamRunner.RunAsync(_activities, $"{ProviderName} ({_model.Catalog.DisplayName})…", ProviderName,
            token => Events(_model, request, priority, token), ct);
    }

    /// <summary>The reply as the host writes it. A host lost before a word of it — it crashed, and the next request
    /// starts another — is asked once more; after a word, the reader has seen part of a reply and it is not.</summary>
    private async IAsyncEnumerable<LlmStreamEvent> Events(OwnedModel model, IReadOnlyList<ChatTurn> turns, RequestPriority priority,
                                                          [EnumeratorCancellation] CancellationToken ct)
    {
        _host.Environment = Environment;
        for (int attempt = 0; ; attempt++)
        {
            bool written = false;
            await using var replies = _host.SendAsync(id => new Generate(id, Spec(model), turns, MaxReplyTokens, priority), StallLimit, ct)
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
            }
        }
    }

    /// <summary>What the host needs to run the model. Its folder is where its files are: a file's path, less its place
    /// inside the model's folder — whatever root the store keeps them under.</summary>
    private ModelSpec Spec(OwnedModel model)
    {
        var file   = model.Catalog.Files[0];
        var path   = _assets.Locate(file) ?? throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        var inside = file.RelativePath[(model.Folder.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        var folder = path[..^inside.Length].TrimEnd(Path.DirectorySeparatorChar);
        return new ModelSpec(model.Id, folder, model.Adapter, model.MemoryBytes, model.ContextTokens);
    }

    /// <summary>The conversation as the host takes it: images and recordings the model reads go as media, where it reads
    /// them; any other attachment is named in the text, as for any provider that cannot read it.</summary>
    internal static IReadOnlyList<ChatTurn> Turns(IReadOnlyList<LlmMessage> messages, OwnedModel model)
        => [.. messages.Select(m =>
        {
            var role = m.Role switch { LlmRole.System => TurnRole.System, LlmRole.Assistant => TurnRole.Assistant, _ => TurnRole.User };
            var read = (m.Attachments ?? []).Where(a => (a.IsImage && model.Images) || (a.IsAudio && model.Audio)).ToList();
            var named = (m.Attachments ?? []).Except(read).Select(a => $"[attached: {a.FilePath}]").ToList();
            string text = named.Count == 0 ? m.Text : m.Text + "\n" + string.Join("\n", named);
            return new ChatTurn(role, text, read.Count == 0 ? null
                : [.. read.Select(a => new ChatMedia(a.IsImage ? MediaKind.Image : MediaKind.Audio, a.Bytes is null ? a.FilePath : null, a.Bytes))]);
        })];

    public ValueTask DisposeAsync()
    {
        _assets.Changed -= OnAssetChanged;
        return ValueTask.CompletedTask;
    }
}
