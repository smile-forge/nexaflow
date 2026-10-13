using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// What every provider of a model Nexaflow runs itself shares: the runtime this machine runs owned models on, fetching a
/// model's files — with the CUDA pack, where it suits the machine — and having the host hold the model's parts.
/// </summary>
internal sealed class OwnedModelRuntime(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, ComputeHardwareReport hardware)
{
    /// <summary>How long a request may go without a word from the host — a load takes seconds, a token milliseconds.</summary>
    internal static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(2);

    public IAiHost Host => host;

    public IModelAssetStore Assets => assets;

    public IBackgroundActivityManager Activities => activities;

    /// <summary>What owned models run on here — CUDA where the pack suits this machine, the CPU otherwise — and so
    /// which of a model's builds this machine fetches.</summary>
    public string ExecutionProvider => CudaRuntimePack.Applies(hardware) ? OwnedModels.Cuda : OwnedModels.Cpu;

    /// <summary>What the host should be started with: the CUDA pack, once it is here.</summary>
    public AiHostEnvironment Environment
        => CudaRuntimePack.Applies(hardware) && CudaRuntimePack.Locate(assets) is { } pack ? new AiHostEnvironment(pack) : AiHostEnvironment.Cpu;

    public CatalogModel CatalogOf(OwnedModel model) => model.CatalogFor(ExecutionProvider);

    /// <summary>The owned models of <paramref name="kind"/> this machine can run — those with a build for its runtime.</summary>
    public IEnumerable<OwnedModel> Runnable(ModelKind kind) => OwnedModels.OfKind(kind).Where(m => m.RunsOn(ExecutionProvider));

    /// <summary>It has a build for this machine, and that build's files are here and verified.</summary>
    public bool IsHere(OwnedModel model) => model.RunsOn(ExecutionProvider) && CatalogOf(model).Files.All(f => assets.Locate(f) is not null);

    /// <summary>Whether <paramref name="asset"/> is one of the model's files on this machine.</summary>
    public bool IsFileOf(OwnedModel model, ModelAsset asset) => CatalogOf(model).Files.Contains(asset);

    /// <summary>
    /// Fetches what is missing — the model's files for this machine, and the CUDA pack where it suits it — reporting how
    /// far it has got. Returns whether everything is here; a failure is reported, never thrown.
    /// </summary>
    public async Task<bool> FetchAsync(OwnedModel model, CancellationToken ct)
    {
        if (!model.RunsOn(ExecutionProvider)) return false;   // nothing of it runs on this machine
        var files = CatalogOf(model).Files.ToList();
        if (CudaRuntimePack.Applies(hardware)) files.AddRange(CudaRuntimePack.Files);
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

    /// <summary>Fetches the model and has the host load every part and keep it. Never throws: a model that will not
    /// load says why when a request asks for it.</summary>
    public async Task WarmupAsync(OwnedModel model, CancellationToken ct)
    {
        if (!await FetchAsync(model, ct)) return;
        try
        {
            host.Environment = Environment;
            foreach (var part in model.PartsFor(ExecutionProvider))
                await foreach (var _ in host.SendAsync(id => new LoadModel(id, Spec(model, part)), StallLimit, ct)) { }
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException or LlmProviderException) { }
    }

    /// <summary>Lets the host unload the model's parts; the host itself stays for any other model.</summary>
    public async Task CooldownAsync(OwnedModel model, CancellationToken ct)
    {
        try
        {
            foreach (var part in model.PartsFor(ExecutionProvider))
                await foreach (var _ in host.SendAsync(id => new UnloadModel(id, PartId(model, part)), StallLimit, ct)) { }
        }
        catch (Exception ex) when (ex is AiHostException or OperationCanceledException) { }
    }

    public ModelSpec Spec(OwnedModel model, string role) => Spec(model, model.Part(role, ExecutionProvider));

    /// <summary>What the host needs to run a part. Its folder is where its files are: a file's path, less its place inside
    /// the part's folder — whatever root the store keeps them under.</summary>
    /// <exception cref="LlmProviderException">The part's files are not here yet.</exception>
    public ModelSpec Spec(OwnedModel model, OwnedPart part)
    {
        var file = model.Catalog.Files.First(f => f.RelativePath.StartsWith(part.Folder + "/", StringComparison.Ordinal));
        var path = assets.Locate(file) ?? throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
        var inside = file.RelativePath[(part.Folder.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        var folder = path[..^inside.Length].TrimEnd(Path.DirectorySeparatorChar);
        return new ModelSpec(PartId(model, part), part.Runner, folder, part.File, part.Adapter, part.MemoryBytes, part.ContextTokens, part.CudaOptions);
    }

    /// <summary>Where one of <paramref name="part"/>'s files is on this machine.</summary>
    /// <exception cref="LlmProviderException">It is not here yet.</exception>
    public string Locate(OwnedModel model, OwnedPart part, string file)
    {
        var asset = model.Catalog.Files.First(f => f.RelativePath == $"{part.Folder}/{file}");
        return assets.Locate(asset) ?? throw new LlmProviderException($"{model.Catalog.DisplayName} is not on this machine yet.");
    }

    /// <summary>The host's name for a part: the model's id, and the part's role in it.</summary>
    private static string PartId(OwnedModel model, OwnedPart part) => $"{model.Id}/{part.Name}";
}
