using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// What every provider of a model Nexaflow runs itself shares: the runtime this machine runs owned models on, fetching a
/// model's files — with the CUDA pack, where it suits the machine — and having the host hold the model's parts where
/// the machine's setup places them: each on a GPU, or on its CPU build where no GPU has room, a language model with
/// the context its GPU leaves it. When the setup moves them — another model taken on — they are loaded again there.
/// </summary>
internal sealed class OwnedModelRuntime(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup,
                                        ComputeHardwareReport hardware) : IModelHolder
{
    /// <summary>How long a request may go without a word from the host — a load takes seconds, a token milliseconds.</summary>
    internal static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    /// <summary>Each held part as the host holds it, by its id: where it runs, and its files' folder on this machine.</summary>
    private readonly Dictionary<string, ModelSpec> _placed = new(StringComparer.Ordinal);
    /// <summary>Why a part placed somewhere did not load there, by its id.</summary>
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private OwnedModel? _held;

    public IAiHost Host => host;

    public IModelAssetStore Assets => assets;

    public IBackgroundActivityManager Activities => activities;

    /// <summary>What owned models run on here — CUDA where the pack suits this machine, the CPU otherwise — and so
    /// which of a model's builds this machine fetches first.</summary>
    public string ExecutionProvider => CudaRuntimePack.Applies(hardware) ? OwnedModels.Cuda : OwnedModels.Cpu;

    /// <summary>What the host should be started with: the CUDA pack, once it is here.</summary>
    public AiHostEnvironment Environment
        => CudaRuntimePack.Applies(hardware) && CudaRuntimePack.Locate(assets) is { } pack ? new AiHostEnvironment(pack) : AiHostEnvironment.Cpu;

    public CatalogModel CatalogOf(OwnedModel model) => model.CatalogFor(ExecutionProvider);

    /// <summary>The owned models of <paramref name="kind"/> this machine can run — those with a build for its runtime
    /// that fit it.</summary>
    public IEnumerable<OwnedModel> Runnable(ModelKind kind) => OwnedModels.OfKind(kind).Where(m => m.RunsOn(ExecutionProvider) && Fits(m));

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
    /// placed — a picture's parts each on a GPU in turn — and a language model on a GPU, but for the catalog's first,
    /// which runs wherever there is none.
    /// </summary>
    public bool Fits(OwnedModel model)
    {
        var gpus = ModelPlacement.Expected(hardware);
        var needs = NeedsOf(model);
        if (model.Kind == ModelKind.ImageGeneration)
        {
            var none = ModelPlacement.Plan(gpus, []);
            return needs.All(n => ModelPlacement.PlaceOnDemand(none, n) is not null);
        }
        var placement = ModelPlacement.Plan(gpus, needs);
        if (placement.Unplaced.Count > 0) return false;
        return model.Kind != ModelKind.Language || model == OwnedModels.OfKind(ModelKind.Language).First()
               || needs.All(n => n.CpuOnly || placement.Of(n.Id)!.Device is not null);
    }

    /// <summary>What the setup needs to place each of <paramref name="model"/>'s parts on this machine.</summary>
    public IReadOnlyList<PlacementNeed> NeedsOf(OwnedModel model) => [.. model.PartsFor(ExecutionProvider).Select(p => Need(model, p))];

    /// <summary>It has a build for this machine, and that build's files are here and verified.</summary>
    public bool IsHere(OwnedModel model) => model.RunsOn(ExecutionProvider) && CatalogOf(model).Files.All(f => assets.Locate(f) is not null);

    /// <summary>Everything the model needs to run here is here: its files, and the CUDA pack where it suits the machine.</summary>
    public bool IsReady(OwnedModel model) => IsHere(model) && (!CudaRuntimePack.Applies(hardware) || CudaRuntimePack.Locate(assets) is not null);

    /// <summary>Whether <paramref name="asset"/> is one of the model's files on this machine.</summary>
    public bool IsFileOf(OwnedModel model, ModelAsset asset) => CatalogOf(model).Files.Contains(asset);

    private async Task<bool> FetchFilesAsync(OwnedModel model, IReadOnlyList<ModelAsset> files, CancellationToken ct)
    {
        if (files.All(f => assets.Locate(f) is not null)) return true;

        string name = model.Catalog.DisplayName;
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
            activity.Fail(ex.Message);   // the model stays unavailable until a later warm-up fetches it
            return false;
        }
    }

    /// <summary>Has the setup place the model's parts beside every other model held, the host holding them where they are
    /// placed — once its files, and the CUDA pack where it suits the machine, are here: warming a model never downloads it, a
    /// download being the user's to start. Never throws: a model that will not load says why when a request asks for it.</summary>
    public async Task WarmupAsync(OwnedModel model, CancellationToken ct)
    {
        if (!IsReady(model)) return;
        try
        {
            await HoldAsync(model, ct);
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException or LlmProviderException) { }
    }

    private async Task HoldAsync(OwnedModel model, CancellationToken ct)
    {
        _held = model;
        host.Environment = Environment;
        await setup.HoldAsync(this, NeedsOf(model), ct);
    }

    /// <summary>Lets the host unload the model's parts; the host itself stays for any other model, and the other models
    /// stay where they are.</summary>
    public async Task CooldownAsync(OwnedModel model, CancellationToken ct)
    {
        try
        {
            await setup.LetGoAsync(this, ct);
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException) { }
        lock (_gate)
        {
            _placed.Clear();
            _failed.Clear();
        }
        _held = null;
    }

    /// <summary>Has the host hold the model's parts as <paramref name="placement"/> places them — fetching a CPU build
    /// the first time a part is placed on one. A part placed nowhere is unavailable until the plan changes.</summary>
    public async Task ApplyAsync(Placement placement, CancellationToken ct)
    {
        if (_held is not { } model) return;
        foreach (var id in model.PartsFor(ExecutionProvider).Select(p => PartId(model, p)).Distinct())
        {
            try
            {
                if (placement.Of(id) is not { } placed)
                {
                    lock (_gate) _placed.Remove(id);
                    continue;
                }
                var spec = await ResolvedAsync(model, placed, ct);
                await foreach (var _ in host.SendAsync(r => new LoadModel(r, spec), StallLimit, ct)) { }
                lock (_gate)
                {
                    _placed[id] = spec;
                    _failed.Remove(id);
                }
            }
            catch (Exception ex) when (ex is AiHostException or OperationCanceledException or LlmProviderException)
            {
                lock (_gate)
                {
                    _placed.Remove(id);
                    _failed[id] = ex.Message;
                }
            }
        }
    }

    /// <summary>The spec the host holds one of the model's parts as, where the setup placed it — the model taken on
    /// first, when it is not held yet.</summary>
    /// <exception cref="LlmProviderException">Its files are not here yet, or no GPU has room for it and it has no CPU
    /// build.</exception>
    public async Task<ModelSpec> PlacedAsync(OwnedModel model, string partName, CancellationToken ct)
    {
        string id = $"{model.Id}/{partName}";
        lock (_gate)
            if (_placed.TryGetValue(id, out var held)) return held;

        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        await HoldAsync(model, ct);
        lock (_gate)
            return _placed.TryGetValue(id, out var placed) ? placed
                : throw new LlmProviderException(_failed.TryGetValue(id, out var why)
                    ? $"{model.Catalog.DisplayName} did not load: {why}"
                    : $"{model.Catalog.DisplayName} does not fit beside the other models on this machine's GPUs.");
    }

    /// <summary>The context a language model's part is held with; its catalog's own until it is placed.</summary>
    public int ContextOf(OwnedModel model, string partName)
    {
        lock (_gate)
            if (_placed.TryGetValue($"{model.Id}/{partName}", out var placed)) return placed.ContextTokens;
        return model.PartNamed(partName, ExecutionProvider).ContextTokens;
    }

    /// <summary>Where a part loaded on demand — a picture's — runs beside the models held now.</summary>
    /// <exception cref="LlmProviderException">No GPU here has room for it, and it has no CPU build.</exception>
    public async Task<ModelSpec> PlacedOnDemandAsync(OwnedModel model, string partName, CancellationToken ct)
    {
        host.Environment = Environment;
        var part = model.PartNamed(partName, ExecutionProvider);
        var placed = await setup.PlaceOnDemandAsync(Need(model, part), ct)
                     ?? throw new LlmProviderException($"{model.Catalog.DisplayName} needs a GPU with {Gib(part.MemoryBytes)} free for its {part.Role}.");
        return await ResolvedAsync(model, placed, ct);
    }

    /// <summary>What the setup needs to place a part: how it runs on a GPU and on the CPU — the same part where it runs
    /// on either, its CPU build where it has one — and what its context costs.</summary>
    private PlacementNeed Need(OwnedModel model, OwnedPart part)
    {
        var onCpu = part.ExecutionProvider is null ? part
                  : model.Parts.FirstOrDefault(p => p.Name == part.Name && p.ExecutionProvider == OwnedModels.Cpu);
        return new PlacementNeed(Unresolved(model, part), onCpu is null ? null : Unresolved(model, onCpu),
                                 part.BytesPerToken, part.BytesPerToken > 0 ? OwnedModels.ShortestContext : 0);
    }

    /// <summary>A part as the setup plans it: its folder still the catalog's, relative to the asset store.</summary>
    private static ModelSpec Unresolved(OwnedModel model, OwnedPart part)
        => new(PartId(model, part), part.Runner, part.Folder, part.File, part.Adapter, part.MemoryBytes, part.ContextTokens, part.CudaOptions,
               part.Projector);

    /// <summary>A placed spec with its folder where the build it was placed as is on this machine — fetched, if it is
    /// a build this machine has not needed before.</summary>
    /// <exception cref="LlmProviderException">The build's files would not come.</exception>
    private async Task<ModelSpec> ResolvedAsync(OwnedModel model, ModelSpec placed, CancellationToken ct)
    {
        var part = model.Parts.First(p => PartId(model, p) == placed.Id && p.Folder == placed.Folder);
        var files = model.Catalog.Files.Where(f => f.RelativePath.StartsWith(part.Folder + "/", StringComparison.Ordinal)).ToList();
        if (!await FetchFilesAsync(model, files, ct))
            throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");

        // Its folder is where its files are: a file's path, less its place inside the part's folder — whatever root the
        // store keeps them under.
        var file = files[0];
        var path = assets.Locate(file)!;
        var inside = file.RelativePath[(part.Folder.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        return placed with { Folder = path[..^inside.Length].TrimEnd(Path.DirectorySeparatorChar) };
    }

    /// <summary>Where one of <paramref name="part"/>'s files is on this machine.</summary>
    /// <exception cref="LlmProviderException">It is not here yet.</exception>
    public string Locate(OwnedModel model, OwnedPart part, string file)
    {
        var asset = model.Catalog.Files.First(f => f.RelativePath == $"{part.Folder}/{file}");
        return assets.Locate(asset) ?? throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
    }

    /// <summary>The host's name for a part: the model's id, and the part's name in it — the same for each of its builds.</summary>
    private static string PartId(OwnedModel model, OwnedPart part) => $"{model.Id}/{part.Name}";

    private static string Gib(long bytes) => $"{bytes / (double)(1L << 30):0.#} GB";
}
