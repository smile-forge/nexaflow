using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// What every provider of a model Nexaflow runs itself shares: the runtime this machine runs owned models on, fetching a
/// model's files — with the CUDA pack, where it suits the machine — and having the host hold the model's parts while it is warm.
/// Where each part runs the host decides, beside every other model held: on a GPU, or on its CPU build where no GPU has room, a
/// language model with the context its GPU leaves it. A request names a part (<see cref="ModelRef"/>), never where it runs.
/// </summary>
internal sealed class OwnedModelRuntime(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostHolds holds,
                                        MachineCards hardware)
{
    /// <summary>How long a request may go without a word from the host — a load takes seconds, a token milliseconds.</summary>
    internal static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    /// <summary>One hold at a time: taking the model on, and letting it go.</summary>
    private readonly SemaphoreSlim _holding = new(1, 1);
    private HostHold? _hold;
    private HostedModel? _held;

    /// <summary>When the provider's model is in use, for the app to load it ahead or let it go.</summary>
    public ModelUseTracker ModelUse { get; } = new();

    public IAiHost Host => host;

    public IModelAssetStore Assets => assets;

    public IBackgroundActivityManager Activities => activities;

    /// <summary>What owned models run on here — CUDA where the pack suits this machine, the CPU otherwise — and so
    /// which of a model's builds this machine fetches first.</summary>
    public string ExecutionProvider => CudaRuntimePack.Applies(hardware) ? OwnedModels.Cuda : OwnedModels.Cpu;

    /// <summary>What the host is expected to run on: the CUDA pack, where this machine runs it and the host's store holds it — the host
    /// takes it from there itself.</summary>
    public AiHostEnvironment Environment
        => CudaRuntimePack.Applies(hardware) && CudaRuntimePack.IsIn(assets) ? AiHostEnvironment.OnCuda : AiHostEnvironment.Cpu;

    public CatalogModel CatalogOf(HostedEntry model) => model.CatalogFor(ExecutionProvider);

    /// <summary>The owned models of <paramref name="kind"/> this machine can run — those with a build for its runtime
    /// that fit it.</summary>
    public IEnumerable<HostedModel> Runnable(ModelKind kind) => OwnedModels.OfKind(kind).Where(m => m.RunsOn(ExecutionProvider) && Fits(m));

    /// <summary>The owned models of <paramref name="kind"/> this machine cannot run: with no build for its runtime, or too large
    /// for its GPUs.</summary>
    public IEnumerable<UnrunnableModel> Unrunnable(ModelKind kind)
        => OwnedModels.OfKind(kind)
            .Where(m => !m.RunsOn(ExecutionProvider) || !Fits(m))
            .Select(m => m.RunsOn(ExecutionProvider)
                ? new UnrunnableModel(CatalogOf(m), UnrunnableReason.TooLargeForThisMachine)
                : new UnrunnableModel(m.Catalog, UnrunnableReason.NoBuildForThisMachine));

    /// <summary>The CUDA pack every owned model is fetched with where it suits this machine; null where they run on its CPU.</summary>
    public LocalRuntime? Runtime => CudaRuntimePack.Applies(hardware) ? new LocalRuntime(CudaRuntimePack.Id, "CUDA runtime", CudaRuntimePack.Files) : null;

    /// <summary>
    /// Whether <paramref name="model"/> fits this machine, as its GPUs are expected before the host has run: every part
    /// placed — a picture's, a clip's or a tool's parts each on a GPU in turn — and a language model on a GPU, what it keeps in this
    /// machine's memory beside it (the layers of a cut, the experts of a mixture) within the host's share of it, but for the catalog's
    /// first, which runs wherever there is none.
    /// </summary>
    public bool Fits(HostedEntry model)
    {
        var gpus = ModelPlacement.Expected(hardware);
        var needs = NeedsOf(model);
        if (model.LoadsOnDemand)
        {
            var none = ModelPlacement.Plan(gpus, []);
            return needs.All(n => ModelPlacement.PlaceOnDemand(none, n) is not null);
        }
        long memory = host.Memory.BudgetBytes;
        var placement = ModelPlacement.Plan(gpus, needs, memory);
        if (placement.Unplaced.Count > 0) return false;
        if (model.PartsFor(ExecutionProvider).Where(p => p.LoadsOnDemand).Any(p => ModelPlacement.PlaceOnDemand(placement, PlacementNeeds.Of(model, p)) is null))
            return false;
        return model is not HostedModel { Kind: HostedKind.Language } language || language == OwnedModels.OfKind(ModelKind.Language).First()
               || needs.All(n => n.CpuOnly || placement.Of(n.Id)!.Device is not null) && placement.Placed.Sum(s => s.HostBytes) <= memory;
    }

    /// <summary>What placing each of <paramref name="model"/>'s parts held with it on this machine takes — all but those it loads
    /// on demand.</summary>
    public IReadOnlyList<PlacementNeed> NeedsOf(HostedEntry model) => PlacementNeeds.Of(model, ExecutionProvider);

    /// <summary>It has a build for this machine, and that build's files are here and verified.</summary>
    public bool IsHere(HostedEntry model) => model.RunsOn(ExecutionProvider) && CatalogOf(model).Files.All(assets.Has);

    /// <summary>Everything the model needs to run here is here: its files, and the CUDA pack where it suits the machine.</summary>
    public bool IsReady(HostedEntry model) => IsHere(model) && (!CudaRuntimePack.Applies(hardware) || CudaRuntimePack.IsIn(assets));

    /// <summary>Whether any part of <paramref name="model"/> held here has made way on its GPU for a picture being drawn — whoever
    /// draws it.</summary>
    public bool IsMakingWay(HostedEntry model) => _hold is { } hold && _held?.Id == model.Id && hold.AnyAway;

    /// <summary>Raised when the held model's parts make way for a picture, are back after it, or are placed again.</summary>
    public event EventHandler? MadeWayChanged;

    /// <summary>One of the files <paramref name="model"/> needs here: its own, or the CUDA pack's where the pack suits the
    /// machine — its arriving or going changes whether the model is ready, as <see cref="IsReady"/> reads it.</summary>
    public bool IsFileOf(HostedEntry model, ModelAsset asset)
        => CatalogOf(model).Files.Contains(asset) || (Runtime?.Files.Contains(asset) ?? false);

    /// <summary>Has the host fetch files of a model that is here — a LoRA's, the first time a picture asks for it. The store refuses
    /// it when its drive has no room, and the activity says so.</summary>
    private async Task<bool> FetchFilesAsync(string name, IReadOnlyList<ModelAsset> files, CancellationToken ct)
    {
        if (files.All(assets.Has)) return true;

        var activity = activities.StartActivity($"Downloading {name}…");
        var lastPercent = -1;
        var progress = new Progress<AssetProgress>(p =>
        {
            var percent = (int)(p.Fraction * 100);
            if (percent == lastPercent) return;
            lastPercent = percent;
            activity.Update($"Downloading {name}… {percent}%");
        });
        try
        {
            await assets.EnsureAllAsync(files, progress, ct);
            activity.Complete();
            return true;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);   // the files stay away until a later ask fetches them
            return false;
        }
    }

    /// <summary>Has the host hold the model's parts beside every other model held — once its files, and the CUDA pack where it suits
    /// the machine, are here: warming a model never downloads it, a download being the user's to start. Never throws: a model that will
    /// not load says why when a request asks for it.</summary>
    public async Task WarmupAsync(HostedModel model, CancellationToken ct)
    {
        if (!IsReady(model)) return;
        try
        {
            await HoldAsync(model, ct);
            ModelUse.Loaded();
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException or LlmProviderException) { }
    }

    /// <summary>The hold on <paramref name="model"/>'s parts held with it, opened where none is — or where the host ended the last one.</summary>
    /// <exception cref="AiHostException">The host refused it, or could not be reached.</exception>
    private async Task<HostHold> HoldAsync(HostedModel model, CancellationToken ct)
    {
        await _holding.WaitAsync(ct);
        try
        {
            if (_hold is { Ended: false } open && _held?.Id == model.Id) return open;
            if (_hold is { } stale) await LetGoAsync(stale);
            host.Environment = Environment;
            var parts = PlacementNeeds.Held(model, ExecutionProvider).Select(p => new ModelRef(model.Id, p.Name)).ToList();
            var hold = await holds.HoldAsync(parts, RequestPriority.Interactive, ct: ct);
            hold.Changed += OnHoldChanged;
            _hold = hold;
            _held = model;
            return hold;
        }
        finally { _holding.Release(); }
    }

    private void OnHoldChanged(object? sender, EventArgs e) => MadeWayChanged?.Invoke(this, EventArgs.Empty);

    private async Task LetGoAsync(HostHold hold)
    {
        hold.Changed -= OnHoldChanged;
        _hold = null;
        _held = null;
        await hold.DisposeAsync();
        MadeWayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets the host unload the model's parts; the host itself stays for any other model, and the other models
    /// stay where they are.</summary>
    public async Task CooldownAsync(HostedModel model, CancellationToken ct)
    {
        try
        {
            await _holding.WaitAsync(ct);
        }
        catch (OperationCanceledException) { return; }
        try
        {
            if (_hold is { } hold) await LetGoAsync(hold);
        }
        finally { _holding.Release(); }
    }

    /// <summary>
    /// Has the host hold <paramref name="model"/>'s parts at <paramref name="priority"/> for as long as the returned value is not
    /// disposed — a voice for its conversation, ahead of everything else placed. The model's own hold stays as it is.
    /// </summary>
    /// <exception cref="LlmProviderException">Its files are not here yet, or the host refused the hold.</exception>
    public async Task<HostHold> HoldForAsync(HostedModel model, RequestPriority priority, CancellationToken ct)
    {
        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        host.Environment = Environment;
        var parts = PlacementNeeds.Held(model, ExecutionProvider).Select(p => new ModelRef(model.Id, p.Name)).ToList();
        try
        {
            return await holds.HoldAsync(parts, priority, ct: ct);
        }
        catch (AiHostException ex)
        {
            throw new LlmProviderException($"{model.Catalog.DisplayName} could not be held: {ex.Message}", ex);
        }
    }

    /// <summary>One of the model's parts held with it, as a request names it — the model held first, where it is not.</summary>
    /// <exception cref="LlmProviderException">Its files are not here yet, or the host holds the part nowhere: no GPU has room for
    /// it and it has no CPU build, or it would not load.</exception>
    public async Task<ModelRef> PlacedAsync(HostedModel model, string partName, CancellationToken ct)
    {
        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        HostHold hold;
        try
        {
            hold = await HoldAsync(model, ct);
        }
        catch (AiHostException ex)
        {
            throw new LlmProviderException($"{model.Catalog.DisplayName} could not be held: {ex.Message}", ex);
        }
        var part = new ModelRef(model.Id, partName);
        return hold.PlacementOf(part) is not null ? part
            : throw new LlmProviderException(hold.WhyUnplaced(part) is { } why
                ? $"{model.Catalog.DisplayName} did not load: {why}"
                : $"{model.Catalog.DisplayName} does not fit beside the other models on this machine's GPUs.");
    }

    /// <summary>Where a language model's part runs, as the host holds it; null before it is held.</summary>
    private PartPlacement? PlacementOf(HostedModel model, string partName)
        => _hold is { } hold && _held?.Id == model.Id ? hold.PlacementOf(new ModelRef(model.Id, partName)) : null;

    /// <summary>The context a language model's part is held with; its catalog's own until it is held.</summary>
    public int ContextOf(HostedModel model, string partName)
        => PlacementOf(model, partName)?.ContextTokens ?? model.PartNamed(partName, ExecutionProvider).ContextTokens;

    /// <summary>How many replies a language model's part writes at once, as it is held; as many as it holds where nothing gives it
    /// room for more until it is held.</summary>
    public int RepliesOf(HostedModel model, string partName) => PlacementOf(model, partName)?.Sequences ?? ModelSpec.DefaultSequences;

    /// <summary>What a language model's part gives work nobody is watching, as it is held: replies at once, each with a context of
    /// its own; null where it has no context for such work, and does it in its one context.</summary>
    public BackgroundCapacity? BackgroundOf(HostedModel model, string partName)
        => PlacementOf(model, partName) is { BackgroundSequences: > 0 } placed
            ? new BackgroundCapacity(placed.BackgroundTokens, placed.BackgroundSequences)
            : null;

    /// <summary>A model that is here, as a request names it — one whose parts load on demand, a picture's or a clip's — the host told
    /// what it runs on.</summary>
    /// <exception cref="LlmProviderException">It is not on this machine yet.</exception>
    public string Ready(HostedEntry model)
    {
        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        host.Environment = Environment;
        return model.Id;
    }

    /// <summary>
    /// <paramref name="files"/> of <paramref name="model"/> that come only when a request first needs them — a LoRA's — fetched into the
    /// host's store where they are not there yet.
    /// </summary>
    /// <exception cref="LlmProviderException">The model is not on this machine, or the files could not be fetched.</exception>
    public async Task FetchAsync(HostedEntry model, string name, IReadOnlyList<ModelAsset> files, CancellationToken ct)
    {
        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        if (!await FetchFilesAsync(name, files, ct) || !files.All(assets.Has)) throw new LlmProviderException($"{name} could not be downloaded.");
    }
}
