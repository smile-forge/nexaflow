using Nexaflow.AiHost.Catalog;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Session.Services.LocalAi;

namespace Nexaflow.Session.Services;

/// <summary>
/// Setting up a workspace's AI: who runs it — Nexaflow's own models, offered where this machine can run them on a GPU, or a cloud
/// provider — then for a provider its key, its language model for every reasoning row, and its other kinds of model for the rows
/// that take them; and the workspace's configs that say <c>[MandatorySetup]</c>. Each choice is saved as it is made. While a
/// provider is being set up a provider set is held, to list its models; disposing the setup lets it go.
/// </summary>
public sealed class WorkspaceAiSetup : IDisposable
{
    /// <summary>The kinds asked for beside language, in the order they are asked, and the rows each fills.</summary>
    private static readonly (ModelKind Kind, AiAbility[] Rows)[] KindsBesideLanguage =
    [
        (ModelKind.SpeechToText,    [AiAbility.SpeechIn]),
        (ModelKind.TextToSpeech,    [AiAbility.SpeechOut]),
        (ModelKind.ImageGeneration, [AiAbility.ImageGeneration, AiAbility.HighQualityImageGeneration, AiAbility.ImageEditing]),
        (ModelKind.VideoGeneration, [AiAbility.VideoGeneration, AiAbility.HighQualityVideoGeneration]),
    ];

    private readonly ProviderManager _providers;
    private readonly WorkspaceManager _saved;

    /// <summary>The provider set held while a provider is set up, acquired from its saved config; null until then.</summary>
    private ProviderSet? _set;

    /// <param name="workspace">The workspace set up.</param>
    /// <param name="providers">The session's provider pool: the providers offered, and the models on this machine.</param>
    /// <param name="saved">The saved workspaces, written back as each choice is made.</param>
    /// <param name="features">The session's feature system, whose workspace-scoped configs are asked for where they say so.</param>
    public WorkspaceAiSetup(Workspace workspace, ProviderManager providers, WorkspaceManager saved, FeatureManager features)
    {
        Workspace  = workspace;
        _providers = providers;
        _saved     = saved;

        workspace.EnsureSharedServicesLoaded(providers);
        providers.DiscoverAll();
        workspace.ReloadProviderConfigs(providers);
        // Every workspace-scoped config made now, so one that says [MandatorySetup] is asked for: the workspace otherwise holds
        // only those made so far, and on a first run that is none.
        workspace.MaterializeAllScopedConfigs(features.Configs);

        // Nexaflow's own model, found among the providers by its harness in a set held just long enough.
        var offers = providers.AcquireProviderSet(workspace.ProviderConfigs, []);
        try { Owned = OwnedModelOffer.Find(offers, providers.Service<MachineCards>() ?? MachineCards.Probe(), providers.Assets); }
        finally { providers.ReleaseProviderSet(offers); }
    }

    public Workspace Workspace { get; }

    /// <summary>Nexaflow's own model, where this machine can run one; null otherwise.</summary>
    public OwnedModelOffer? Owned { get; }

    /// <summary>Nexaflow's own model is offered as the choice: it has a GPU to run on — on the CPU alone it answers too slowly.</summary>
    public bool OffersOwned => Owned is { OnGpu: not null };

    /// <summary>Who runs the workspace's AI: a provider unless Nexaflow's own model was chosen.</summary>
    public WorkspaceHarness Harness { get; private set; } = WorkspaceHarness.Bridged;

    /// <summary>The providers the workspace can be set up with: their configs as the workspace holds them.</summary>
    public IReadOnlyList<IProviderConfig> Providers => Workspace.ProviderConfigs;

    /// <summary>The provider chosen; null until one is.</summary>
    public IProviderConfig? Provider { get; private set; }

    /// <summary>Who runs the workspace's AI. Nexaflow's own model takes every row it can serve, and the workspace is saved.</summary>
    public void ChooseHarness(WorkspaceHarness harness)
    {
        Harness = harness;
        if (harness != WorkspaceHarness.Owned) return;
        if (Owned is not { } owned) throw new InvalidOperationException("This machine runs no model of Nexaflow's own.");

        WorkspaceAiDefaults.Assign(Workspace.AiConfig, bridged: null, owned.Columns());
        SaveAi();
    }

    /// <summary>The provider to set up; null for none.</summary>
    public void ChooseProvider(IProviderConfig? provider) => Provider = provider;

    /// <summary>Saves the chosen provider's config as edited, and holds a provider set acquired from it, to list its models.</summary>
    public void SaveProviderConfig()
    {
        if (Provider is null) return;
        _saved.SaveWorkspaceProviderConfig(Workspace, Provider);
        var old = _set;
        _set = _providers.AcquireProviderSet(Workspace.ProviderConfigs, []);
        if (old is not null) _providers.ReleaseProviderSet(old);
    }

    /// <summary>The models the chosen provider offers of <paramref name="kind"/>; none where it offers none of that kind, or they
    /// cannot be listed.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(ModelKind kind)
    {
        if (ProviderOf(kind) is not { } provider) return [];
        try { return await provider.GetAvailableModelsAsync(); }
        catch (Exception) { return []; }   // a provider that cannot list its models offers none
    }

    /// <summary>Puts <paramref name="model"/>, of the chosen provider, in every reasoning row of the ability grid, and saves the workspace.</summary>
    public void UseLanguageModel(string model)
    {
        if (Provider is null) return;
        // The provider's own name is the key the grid's columns are resolved against; its friendly name usually equals it.
        var name = ProviderOf(ModelKind.Language)?.Name ?? Provider.FriendlyName;
        WorkspaceAiDefaults.Assign(Workspace.AiConfig, Column(name, model, ModelKind.Language), owned: null);
        SaveAi();
    }

    /// <summary>The chosen provider's kinds of model beside language, each with the rows of the ability grid it fills.</summary>
    public IReadOnlyList<(ModelKind Kind, IReadOnlyList<AiAbility> Rows)> KindsOffered()
        => [.. KindsBesideLanguage.Where(k => ProviderOf(k.Kind) is not null).Select(k => (k.Kind, (IReadOnlyList<AiAbility>)k.Rows))];

    /// <summary>Puts each model chosen, of the chosen provider, in the rows its kind fills, and saves the workspace; none chosen
    /// saves nothing.</summary>
    public void UseModels(IReadOnlyList<(ModelKind Kind, string Model)> chosen)
    {
        if (_set is not { } set) return;
        var columns = chosen.Select(c => ProviderOf(c.Kind) is { } provider ? Column(provider.Name, c.Model, c.Kind) : null)
                            .OfType<ProviderModelPair>().ToList();
        if (columns.Count == 0) return;
        WorkspaceAiDefaults.AssignKinds(Workspace.AiConfig, set, columns);
        SaveAi();
    }

    /// <summary>The workspace's configs that say <c>[MandatorySetup]</c> — its persona, then its feature configs — each with the
    /// name it is saved under and its title.</summary>
    public IReadOnlyList<(object Config, string ConfigName, string FriendlyName)> MandatoryConfigs()
    {
        var configs = new List<(object, string, string)>();
        if (ConfigRequirements.IsMandatory(Workspace.Persona))
            configs.Add((Workspace.Persona, Workspace.Persona.ConfigName, Workspace.Persona.FriendlyName));
        foreach (var scoped in Workspace.WorkspaceConfigs.Where(ConfigRequirements.IsMandatory))
            configs.Add((scoped, scoped.ConfigName, scoped.FriendlyName));
        return configs;
    }

    /// <summary>Saves one of <see cref="MandatoryConfigs"/> as edited.</summary>
    public void Save(object config)
    {
        if (ReferenceEquals(config, Workspace.Persona)) _saved.SaveWorkspacePersona(Workspace);
        else if (config is IFeatureConfig scoped)      _saved.SaveWorkspaceScopedConfig(Workspace, scoped);
        else throw new ArgumentException("Not one of the workspace's configs.", nameof(config));
    }

    /// <summary>Lets go of the provider set held to list models.</summary>
    public void Dispose()
    {
        if (_set is null) return;
        _providers.ReleaseProviderSet(_set);
        _set = null;
    }

    /// <summary>The chosen provider's own instance for <paramref name="kind"/>, from the set held; null where there is none.</summary>
    private IAiProvider? ProviderOf(ModelKind kind)
        => Provider?.FriendlyName is { } name ? _set?.Find(name, kind) : null;

    private ProviderModelPair Column(string providerName, string model, ModelKind kind) => new()
    {
        ProviderName = providerName, Model = model, Kind = kind, AssemblyFileName = _set?.GetAssemblyFileName(providerName) ?? string.Empty,
    };

    private void SaveAi()
    {
        _saved.SaveWorkspaceAiConfig(Workspace);
        _saved.SaveWorkspaces();
    }
}
