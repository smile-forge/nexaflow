using Nexaflow.Core.AI;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// One model the Local column can hold for a job: whose it is, which, the runtime it shares with others, and — when this
/// machine cannot run it — why.
/// </summary>
public sealed record LocalModelOption(string ProviderName, string AssemblyFileName, ModelKind Kind, CatalogModel Model,
                                      LocalRuntime? Runtime, UnrunnableReason? Unrunnable)
{
    public string Id => Model.Id;

    public bool Runs => Unrunnable is null;

    /// <summary>Its files and its runtime's — everything it needs here.</summary>
    public IEnumerable<ModelAsset> Needs => Runtime is null ? Model.Files : Model.Files.Concat(Runtime.Files);

    /// <summary>Whether <paramref name="column"/> holds this model.</summary>
    public bool IsIn(ProviderModelPair? column)
        => column is not null && column.Model == Id && string.Equals(column.ProviderName, ProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A column holding it.</summary>
    public ProviderModelPair Column() => new() { ProviderName = ProviderName, Model = Id, Kind = Kind, AssemblyFileName = AssemblyFileName };
}

/// <summary>
/// The models that run on this machine, as the Local column, the Local page and the downloaded-models page see them: for
/// each job, the models of every provider that is Nexaflow's own harness — those this machine runs, and those it cannot,
/// with why; whether each is here; fetching one with the runtime it shares; and removing what is here.
/// </summary>
public sealed class LocalModelShelf(ProviderSet providers, ModelAssetStore store)
{
    public ModelAssetStore Store => store;

    /// <summary>
    /// The models <paramref name="slot"/> can hold: those this machine runs first, in their catalogs' order. Speech to text
    /// holds a conversation, so it offers Nexaflow's own speech models; where this machine runs none of them it offers only
    /// <see cref="LocalColumns.Dictation"/>.
    /// </summary>
    public IReadOnlyList<LocalModelOption> OptionsFor(LocalSlot slot)
    {
        var options = Catalogued(LocalColumns.KindOf(slot));
        if (slot == LocalSlot.SpeechToText)
        {
            var converse = options.Where(o => !LocalColumns.Dictates(o.Column())).ToList();
            options = converse.Any(o => o.Runs) ? converse : [.. options.Where(o => o.IsIn(LocalColumns.Dictation))];
        }
        var tier = LocalColumns.TierOf(slot);
        return [.. options.OrderBy(o => o.Runs ? 0 : 1).ThenBy(o => o.Model.Tier == tier ? 0 : 1)];
    }

    /// <summary>Every model of <paramref name="kind"/> in the catalogs of the providers that are Nexaflow's own harness.</summary>
    private List<LocalModelOption> Catalogued(ModelKind kind)
    {
        var options = new List<LocalModelOption>();
        foreach (var provider in providers.Providers)
        {
            if (provider.Harness != ModelHarness.Owned || !provider.Serves(kind) || provider is not ILocalModelCatalog catalog) continue;
            var assembly = providers.GetAssemblyFileName(provider.Name);
            foreach (var model in catalog.Catalog)
                options.Add(new(provider.Name, assembly, kind, model, catalog.RuntimeOf(model.Id), null));
            foreach (var model in catalog.Unrunnable)
                options.Add(new(provider.Name, assembly, kind, model.Model, null, model.Why));
        }
        return options;
    }

    /// <summary>Every model in the catalogs, once each — those no job offers here too, so what is on disk can always be seen.</summary>
    public IReadOnlyList<LocalModelOption> All
        => [.. Enum.GetValues<LocalSlot>().Select(LocalColumns.KindOf).Distinct().SelectMany(Catalogued).DistinctBy(o => (o.ProviderName.ToLowerInvariant(), o.Id))];

    /// <summary>Whether this machine runs any model for <paramref name="slot"/>.</summary>
    public bool Runs(LocalSlot slot) => OptionsFor(slot).Any(o => o.Runs);

    /// <summary>Whether <paramref name="slot"/> only dictates here: the speech job where this machine runs none of Nexaflow's
    /// own speech models, with <see cref="LocalColumns.Dictation"/> in their place.</summary>
    public bool Dictates(LocalSlot slot) => OptionsFor(slot) is { Count: > 0 } options && options.All(o => LocalColumns.Dictates(o.Column()));

    /// <summary>Whether everything <paramref name="option"/> needs is here.</summary>
    public bool IsReady(LocalModelOption option) => option.Needs.All(f => store.Locate(f) is not null);

    /// <summary>Whether the model's own files are here, whatever of its runtime is.</summary>
    public bool IsHere(LocalModelOption option) => option.Model.Files.All(f => store.Locate(f) is not null);

    /// <summary>What fetching <paramref name="option"/> would download.</summary>
    public long MissingBytes(LocalModelOption option) => option.Needs.Where(f => store.Locate(f) is null).Sum(f => f.Size);

    /// <summary>Fetches what <paramref name="option"/> still needs.</summary>
    /// <exception cref="InvalidDataException">What arrived is not what the catalog describes.</exception>
    public Task FetchAsync(LocalModelOption option, IProgress<AssetProgress>? progress, CancellationToken ct)
        => store.EnsureAllAsync([.. option.Needs], progress, ct);

    /// <summary>Every runtime the models share.</summary>
    public IReadOnlyList<LocalRuntime> Runtimes => [.. All.Select(o => o.Runtime).OfType<LocalRuntime>().DistinctBy(r => r.Id)];

    public bool IsHere(LocalRuntime runtime) => runtime.Files.All(f => store.Locate(f) is not null);

    /// <summary>
    /// Removes the files of <paramref name="files"/> that are here, but those another model here lists too: two builds of
    /// one model can share every part but one, and removing one leaves the other whole. A model counts as here when a file
    /// of its own outside <paramref name="files"/> is.
    /// </summary>
    public async Task RemoveAsync(IReadOnlyList<ModelAsset> files, CancellationToken ct = default)
    {
        var removing = files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        var kept = All.Select(o => o.Model.Files)
                      .Where(own => own.Any(f => !removing.Contains(f.RelativePath) && store.Locate(f) is not null))
                      .SelectMany(own => own.Select(f => f.RelativePath))
                      .ToHashSet(StringComparer.Ordinal);
        foreach (var file in files.Where(f => !kept.Contains(f.RelativePath))) await store.RemoveAsync(file, ct);
    }
}
