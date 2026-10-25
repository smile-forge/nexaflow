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

    /// <summary>When the provider's model is in use, for the app to load it ahead or let it go.</summary>
    public ModelUseTracker ModelUse { get; } = new();

    public IAiHost Host => host;

    public IModelAssetStore Assets => assets;

    public IBackgroundActivityManager Activities => activities;

    /// <summary>What owned models run on here — CUDA where the pack suits this machine, the CPU otherwise — and so
    /// which of a model's builds this machine fetches first.</summary>
    public string ExecutionProvider => CudaRuntimePack.Applies(hardware) ? OwnedModels.Cuda : OwnedModels.Cpu;

    /// <summary>What the host should be started with: the CUDA pack, once it is here.</summary>
    public AiHostEnvironment Environment
        => CudaRuntimePack.Applies(hardware) && CudaRuntimePack.Locate(assets) is { } pack ? new AiHostEnvironment(pack) : AiHostEnvironment.Cpu;

    public CatalogModel CatalogOf(OwnedEntry model) => model.CatalogFor(ExecutionProvider);

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
    /// placed — a picture's, a clip's or a tool's parts each on a GPU in turn — and a language model on a GPU, but for the
    /// catalog's first, which runs wherever there is none.
    /// </summary>
    public bool Fits(OwnedEntry model)
    {
        var gpus = ModelPlacement.Expected(hardware);
        var needs = NeedsOf(model);
        if (model.LoadsOnDemand)
        {
            var none = ModelPlacement.Plan(gpus, []);
            return needs.All(n => ModelPlacement.PlaceOnDemand(none, n) is not null);
        }
        var placement = ModelPlacement.Plan(gpus, needs);
        if (placement.Unplaced.Count > 0) return false;
        if (model.PartsFor(ExecutionProvider).Where(p => p.LoadsOnDemand).Any(p => ModelPlacement.PlaceOnDemand(placement, Need(model, p)) is null))
            return false;
        return model is not OwnedModel { Kind: ModelKind.Language } language || language == OwnedModels.OfKind(ModelKind.Language).First()
               || needs.All(n => n.CpuOnly || placement.Of(n.Id)!.Device is not null);
    }

    /// <summary>What the setup needs to place each of <paramref name="model"/>'s parts held with it on this machine — all
    /// but those it loads on demand.</summary>
    public IReadOnlyList<PlacementNeed> NeedsOf(OwnedEntry model) => [.. Held(model).Select(p => Need(model, p))];

    /// <summary>The parts of <paramref name="model"/> the host holds with it: all but those it loads on demand.</summary>
    private IEnumerable<OwnedPart> Held(OwnedEntry model) => model.PartsFor(ExecutionProvider).Where(p => !p.LoadsOnDemand);

    /// <summary>It has a build for this machine, and that build's files are here and verified.</summary>
    public bool IsHere(OwnedEntry model) => model.RunsOn(ExecutionProvider) && CatalogOf(model).Files.All(f => assets.Locate(f) is not null);

    /// <summary>Everything the model needs to run here is here: its files, and the CUDA pack where it suits the machine.</summary>
    public bool IsReady(OwnedEntry model) => IsHere(model) && (!CudaRuntimePack.Applies(hardware) || CudaRuntimePack.Locate(assets) is not null);

    /// <summary>Whether any part of <paramref name="model"/> has made way on its GPU for a picture being drawn.</summary>
    public bool IsMakingWay(OwnedEntry model) => model.PartsFor(ExecutionProvider).Any(part => setup.HasMadeWay(PartId(model, part)));

    /// <summary>The host's models <paramref name="ids"/> made way for a picture, until the returned value is disposed.</summary>
    public IDisposable MakingWay(IReadOnlyList<string> ids) => setup.MakingWay(ids);

    /// <summary>Raised when models make way for a picture or are back after it.</summary>
    public event EventHandler? MadeWayChanged
    {
        add => setup.MadeWayChanged += value;
        remove => setup.MadeWayChanged -= value;
    }

    /// <summary>Whether <paramref name="asset"/> is one of the model's files on this machine.</summary>
    public bool IsFileOf(OwnedEntry model, ModelAsset asset) => CatalogOf(model).Files.Contains(asset);

    /// <summary>Fetches another build of a model that is here. The store refuses it when its drive has no room, and the
    /// activity says so.</summary>
    private async Task<bool> FetchFilesAsync(string name, IReadOnlyList<ModelAsset> files, CancellationToken ct)
    {
        if (files.All(f => assets.Locate(f) is not null)) return true;

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
            ModelUse.Loaded();
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

    /// <summary>Has the host hold the model's parts as <paramref name="placement"/> places them — fetching a CPU build of a model
    /// that is here the first time a part is placed on one. A part placed nowhere is unavailable until the plan changes.</summary>
    public async Task ApplyAsync(Placement placement, CancellationToken ct)
    {
        if (_held is not { } model) return;
        foreach (var id in Held(model).Select(p => PartId(model, p)).Distinct())
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

    /// <summary>How many replies a language model's part writes at once, as it is held; as many as it holds where nothing gives it
    /// room for more until it is placed.</summary>
    public int RepliesOf(OwnedModel model, string partName)
    {
        lock (_gate)
            return _placed.TryGetValue($"{model.Id}/{partName}", out var placed) ? placed.Sequences : ModelSpec.DefaultSequences;
    }

    /// <summary>Where a part loaded on demand — a picture's or a clip's — runs beside the models held now.</summary>
    /// <exception cref="LlmProviderException">No GPU here has room for it, and it has no CPU build.</exception>
    public async Task<ModelSpec> PlacedOnDemandAsync(OwnedEntry model, string partName, CancellationToken ct)
    {
        host.Environment = Environment;
        var part = model.PartNamed(partName, ExecutionProvider);
        var placed = await setup.PlaceOnDemandAsync(Need(model, part), ct)
                     ?? throw new LlmProviderException($"{model.Catalog.DisplayName} needs a GPU with {Gib(part.MemoryBytes)} free for its {part.Role}.");
        return await ResolvedAsync(model, placed, ct);
    }

    /// <summary>What the setup needs to place a part: how it runs on a GPU and on the CPU — the same part where it runs
    /// on either, its CPU build where it has one — what its context costs, held in 8 bits (<see cref="ContextCache.Q8"/>), and
    /// what it takes to read in longer steps or to be cut between a GPU and the CPU.</summary>
    private PlacementNeed Need(OwnedEntry model, OwnedPart part)
    {
        var onCpu = part.ExecutionProvider is null ? part
                  : model.Parts.FirstOrDefault(p => p.Name == part.Name && p.ExecutionProvider == OwnedModels.Cpu);
        bool language = part.BytesPerToken > 0;
        return new PlacementNeed(Unresolved(model, part), onCpu is null ? null : Unresolved(model, onCpu) with { MemoryBytes = FilesBytes(model, onCpu) },
                                 language ? ContextCache.Q8.BytesPerToken(part.BytesPerToken) : 0, language ? OwnedModels.ShortestContext : 0,
                                 part.Layers, language ? WeightsBytes(model, part) : 0, part.BatchBytesPerToken, ContextCache.Q8.BytesPerToken(part.BytesPerSequence));
    }

    /// <summary>What <paramref name="part"/>'s own file holds — a language model's weights, its GGUF — read whole.</summary>
    private static long WeightsBytes(OwnedEntry model, OwnedPart part)
        => model.Catalog.Files.FirstOrDefault(f => f.RelativePath == $"{part.Folder}/{part.File}")?.Size ?? 0;

    /// <summary>What <paramref name="part"/> holds in memory on the CPU, with no context: its files, read whole — the
    /// catalog's footprints are its GPU's.</summary>
    private static long FilesBytes(OwnedEntry model, OwnedPart part)
        => model.Catalog.Files.Where(f => f.RelativePath.StartsWith(part.Folder + "/", StringComparison.Ordinal)).Sum(f => f.Size);

    /// <summary>A part as the setup plans it: its folder still the catalog's, relative to the asset store.</summary>
    private static ModelSpec Unresolved(OwnedEntry model, OwnedPart part)
        => new(PartId(model, part), part.Runner, part.Folder, part.File, part.Adapter, part.MemoryBytes, part.ContextTokens, part.CudaOptions,
               part.Projector, ContextCache: part.BytesPerToken > 0 ? ContextCache.Q8 : ContextCache.F16);

    /// <summary>A placed spec with its folder where the build it was placed as is on this machine — fetched, if it is another
    /// build of a model that is here, one this machine has not needed before. A model that is not here is never fetched: its
    /// download is the user's to start, through the app's downloads.</summary>
    /// <exception cref="LlmProviderException">The model is not here, or the build's files would not come.</exception>
    private async Task<ModelSpec> ResolvedAsync(OwnedEntry model, ModelSpec placed, CancellationToken ct)
    {
        var part = model.Parts.First(p => PartId(model, p) == placed.Id && p.Folder == placed.Folder);
        var files = model.Catalog.Files.Where(f => f.RelativePath.StartsWith(part.Folder + "/", StringComparison.Ordinal)).ToList();
        if (!files.All(f => assets.Locate(f) is not null) && (!IsHere(model) || !await FetchFilesAsync(model.Catalog.DisplayName, files, ct)))
            throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");

        // Its folder is where its files are: a file's path, less its place inside the part's folder — whatever root the
        // store keeps them under.
        var file = files[0];
        var path = assets.Locate(file)!;
        var inside = file.RelativePath[(part.Folder.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        return placed with { Folder = path[..^inside.Length].TrimEnd(Path.DirectorySeparatorChar) };
    }

    /// <summary>
    /// Where <paramref name="files"/> are on this machine — files of a model that is here, fetched only once a request
    /// first asks for them, such as a LoRA's — downloading them first, shown on the activity as <paramref name="name"/>.
    /// Asking for them is the user's start to that download, as choosing a model to download is.
    /// </summary>
    /// <exception cref="LlmProviderException">The model is not here, or the files would not come.</exception>
    public async Task<IReadOnlyList<string>> FetchedAsync(OwnedEntry model, string name, IReadOnlyList<ModelAsset> files, CancellationToken ct)
    {
        if (!IsHere(model)) throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        if (!await FetchFilesAsync(name, files, ct)) throw new LlmProviderException($"{name} could not be downloaded.");
        return [.. files.Select(f => assets.Locate(f) ?? throw new LlmProviderException($"{name} is not on this machine yet."))];
    }

    /// <summary>Where one of <paramref name="part"/>'s files is on this machine.</summary>
    /// <exception cref="LlmProviderException">It is not here yet.</exception>
    public string Locate(OwnedEntry model, OwnedPart part, string file)
    {
        var asset = model.Catalog.Files.First(f => f.RelativePath == $"{part.Folder}/{file}");
        return assets.Locate(asset) ?? throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
    }

    /// <summary>The host's name for a part: the model's id, and the part's name in it — the same for each of its builds.</summary>
    private static string PartId(OwnedEntry model, OwnedPart part) => $"{model.Id}/{part.Name}";

    private static string Gib(long bytes) => $"{bytes / (double)(1L << 30):0.#} GB";
}
