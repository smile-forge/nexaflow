using System.Text;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Catalog;
using Nexaflow.Providers.Local.Harness;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local;

/// <summary>
/// In-process LLM provider that runs a local GGUF model via LlamaSharp. Each execution instance is bound
/// to one model from the catalog; a model-agnostic capability instance only enumerates the variants the
/// host can run. <see cref="CompleteAsync"/> runs the provider's own server-side tool loop (calculator,
/// later MCP) entirely here, returning only the final visible text to Nexaflow's outer client loop.
/// </summary>
public sealed class LocalLlmProvider : ILlmProvider, IDisposable
{
    public const  string ProviderName = "Local";
    public string Name => ProviderName;

    private const int MaxResponseTokens = 4096;
    private const int MaxToolRounds     = 10;

    private readonly IBackgroundActivityManager _activity;
    private readonly LocalConfig                _config;
    private readonly IHostCapabilityService     _caps;
    private readonly string                     _model;

    private readonly Lock _managerLock = new();
    private IReadOnlyList<LocalModelVariant>? _variants;
    private LocalModelManager? _manager;

    public LocalLlmProvider(
        IBackgroundActivityManager activity,
        LocalConfig                config,
        ProviderModel              model,
        IHostCapabilityService     caps)
    {
        _activity = activity;
        _config   = config;
        _caps     = caps;
        _model    = model.Model;
    }

    private IReadOnlyList<LocalModelVariant> Variants =>
        _variants ??= LocalModelCatalog.Load(_config.ResolvedModelsDir);

    private LocalModelVariant? BoundVariant =>
        string.IsNullOrEmpty(_model) ? null : LocalModelCatalog.Find(Variants, _model);

    // -1 (auto) → offload all layers. LlamaSharp keeps layers on the CPU when no GPU backend is active,
    // so requesting full offload is safe on CPU-only hosts; 0 forces CPU; a positive value caps it.
    private int GpuLayers => _config.GpuLayerCount >= 0 ? _config.GpuLayerCount : 99;

    private uint ContextSize(LocalModelVariant v)
    {
        int c = _config.ContextSize > 0 ? _config.ContextSize
              : v.ContextSize     > 0 ? v.ContextSize
              : 8192;
        return (uint)c;
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var ids = LocalModelCatalog.FittingHost(Variants, _caps.Report).Select(v => v.Id).ToList();
            return Task.FromResult<IReadOnlyList<string>>(ids);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    public Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        var v = BoundVariant;
        if (v is null) return Task.FromResult<ModelInfo?>(null);
        int ctx = _config.ContextSize > 0 ? _config.ContextSize : v.ContextSize;
        return Task.FromResult<ModelInfo?>(ctx > 0 ? new ModelInfo(ctx, v.Display) : null);
    }

    public async Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage>     messages,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken             ct = default)
    {
        var v = BoundVariant
                ?? throw new LlmProviderException($"Local model '{_model}' is not in the catalog.");

        // Make sure the GGUF is present (own activity); download runs off the UI thread.
        var path = await EnsureDownloadedAsync(v, ct);

        var activity = _activity.StartActivity($"Local ({v.Display})…");
        try
        {
            var manager = GetOrCreateManager(v, path);
            await manager.LoadAsync(ct);

            var registry = ServerToolRegistry.Build(_config.EnabledServerTools);
            var harness  = HarnessFactory.Create(v.FamilyKind);
            var options  = new HarnessOptions(_config.ThinkingMode);

            var prompt = WithAttachments(messages, attachments);
            var text = await ServerToolLoop.RunAsync(
                harness, prompt, options, registry,
                (p, ap, c) => manager.InferAsync(p, ap, MaxResponseTokens, c),
                MaxToolRounds, ct);

            activity.Complete();
            return string.IsNullOrEmpty(text) ? null : new LlmResponse(text);
        }
        catch (OperationCanceledException)
        {
            activity.Fail("cancelled");
            throw;
        }
        catch (LlmProviderException)
        {
            activity.Fail("error");
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"Local inference failed: {ex.Message}", ex);
        }
    }

    public async Task WarmupAsync(CancellationToken ct = default)
    {
        var v = BoundVariant;
        if (v is null) return;

        // Don't kick off a multi-GB download (or load a missing/corrupt file) at startup — only
        // pre-load a model that's already present and valid (the "spin-up", like Ollama's warm-up).
        // First use downloads on demand and surfaces real errors to the user.
        if (!LocalModelDownloader.IsPresent(v, _config.ResolvedModelsDir)) return;

        var activity = _activity.StartActivity($"Loading {v.Display}…");
        try
        {
            var path = LocalModelDownloader.PrimaryPath(v, _config.ResolvedModelsDir);
            await GetOrCreateManager(v, path).LoadAsync(ct);
            activity.Complete();
        }
        catch (OperationCanceledException) { activity.Complete(); }
        catch (Exception ex)               { activity.Fail(ex.Message); }   // best-effort; CompleteAsync surfaces real failures
    }

    public async Task CooldownAsync(CancellationToken ct = default)
    {
        LocalModelManager? mgr;
        lock (_managerLock) mgr = _manager;
        if (mgr is not null) await mgr.UnloadAsync();
    }

    public void Dispose()
    {
        LocalModelManager? mgr;
        lock (_managerLock) { mgr = _manager; _manager = null; }
        mgr?.Dispose();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private Task<string> EnsureDownloadedAsync(LocalModelVariant v, CancellationToken ct)
        => LocalModelDownloader.EnsureAsync(v, _config.ResolvedModelsDir, _activity, ct);

    private LocalModelManager GetOrCreateManager(LocalModelVariant v, string path)
    {
        lock (_managerLock)
            return _manager ??= new LocalModelManager(path, ContextSize(v), GpuLayers);
    }

    /// <summary>Appends attachment paths to the last user message (the model can't read files directly).</summary>
    private static IReadOnlyList<LlmMessage> WithAttachments(
        IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0) return messages;

        var list = messages.ToList();
        int last = list.FindLastIndex(m => m.Role == LlmRole.User);
        if (last < 0) return messages;

        var sb = new StringBuilder(list[last].Text);
        sb.AppendLine().AppendLine().AppendLine("Attached files:");
        foreach (var a in attachments) sb.Append("  ").AppendLine(a.FilePath);
        list[last] = list[last] with { Text = sb.ToString() };
        return list;
    }
}
