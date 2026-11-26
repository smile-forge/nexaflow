using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Session.Services.LocalAi;

/// <summary>The model Nexaflow runs itself that a new workspace is offered: what its column is, and what it needs downloaded.</summary>
/// <param name="Placement">Where its models would run on this machine, as its GPUs are expected before the host has run.</param>
/// <param name="VoiceOnGpu">Every voice model has room on a GPU beside it.</param>
/// <param name="Larger">The other language models of Nexaflow's own this machine fits, by name.</param>
public sealed record OwnedModelOffer(string ProviderName, string ModelId, string DisplayName, string AssemblyFileName,
                                     string? SpeechInModelId, string? SpeechOutModelId, Placement Placement, bool VoiceOnGpu,
                                     IReadOnlyList<string> Larger)
{
    /// <summary>What choosing it downloads: its language model and voice, each with the runtime it needs. Warming a model never
    /// downloads it; the setup's downloads offer these once the choice is made.</summary>
    public IReadOnlyList<LocalModelOption> Downloads { get; init; } = [];

    /// <summary>The columns a workspace that uses Nexaflow's own models starts with.</summary>
    public OwnedColumns Columns() => new(
        Column(ModelId, ModelKind.Language),
        SpeechInModelId is null ? null : Column(SpeechInModelId, ModelKind.SpeechToText),
        SpeechOutModelId is null ? null : Column(SpeechOutModelId, ModelKind.TextToSpeech));

    private ProviderModelPair Column(string model, ModelKind kind)
        => new() { ProviderName = ProviderName, Model = model, Kind = kind, AssemblyFileName = AssemblyFileName };

    /// <summary>Its language model as the setup would place it on a GPU; null where none has room for it.</summary>
    public ModelSpec? OnGpu => Placement.Placed.FirstOrDefault(s => s.Id.StartsWith(ModelId + "/", StringComparison.Ordinal) && s.Device is not null);

    /// <summary>The owned language provider's first model, with its speech models, their files and — where the machine
    /// suits it — the CUDA pack to fetch, and where they would run here; null when no provider is Nexaflow's own harness.</summary>
    public static OwnedModelOffer? Find(ProviderSet providers, MachineCards hardware, IModelAssetStore? assets = null)
    {
        var provider = providers.Providers.FirstOrDefault(p => p.Harness == ModelHarness.Owned && p.Serves(ModelKind.Language));
        if (provider is not ILocalModelCatalog { Catalog: [var model, ..] } catalog) return null;

        IAiProvider? Serving(ModelKind kind)
            => providers.Providers.FirstOrDefault(p => p.Harness == ModelHarness.Owned && p.Name == provider.Name && p.Serves(kind));
        CatalogModel? First(ModelKind kind) => Serving(kind) is ILocalModelCatalog { Catalog: [var first, ..] } ? first : null;
        var speechIn  = First(ModelKind.SpeechToText);
        var speechOut = First(ModelKind.TextToSpeech);

        static IReadOnlyList<PlacementNeed> NeedsOf(IAiProvider? served, CatalogModel? of)
            => served is IHostModelCatalog host && of is not null ? host.NeedsOf(of.Id) : [];
        var voice = NeedsOf(Serving(ModelKind.SpeechToText), speechIn).Concat(NeedsOf(Serving(ModelKind.TextToSpeech), speechOut)).ToList();
        var placement = ModelPlacement.Plan(ModelPlacement.Expected(hardware), [.. NeedsOf(provider, model), .. voice]);

        var shelf = assets is null ? null : new LocalModelShelf(providers, assets);
        LocalModelOption? Option(LocalSlot slot, CatalogModel? of)
            => of is null ? null : shelf?.OptionsFor(slot).FirstOrDefault(o => o.ProviderName == provider.Name && o.Id == of.Id);
        return new OwnedModelOffer(provider.Name, model.Id, model.DisplayName, providers.GetAssemblyFileName(provider.Name),
                                   speechIn?.Id, speechOut?.Id, placement,
                                   VoiceOnGpu: voice.All(n => n.CpuOnly || placement.Of(n.Id)?.Device is not null),
                                   Larger: [.. catalog.Catalog.Skip(1).Select(m => m.DisplayName)])
        {
            Downloads = [.. new[] { Option(LocalSlot.Language, model), Option(LocalSlot.SpeechToText, speechIn), Option(LocalSlot.TextToSpeech, speechOut) }
                            .OfType<LocalModelOption>()],
        };
    }
}
