using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Session.AI;
using Nexaflow.Session.Services.LocalAi;

namespace Nexaflow.Session.Services;

/// <summary>
/// An edit of a workspace's settings — its ability grid, persona, providers' configs and feature configs — on copies of its own, read
/// from the workspace's folder, so a running workspace is not disturbed mid-edit: a save writes a copy to the folder only. Once
/// anything has been saved, <see cref="Close"/> reloads the workspace from its folder and restarts it, once. While it is open it
/// holds a provider set built from the copies, which lists the providers' models and the models on this machine.
/// </summary>
public sealed class WorkspaceSettingsDraft
{
    private readonly ConfigManager _config;
    private readonly FeatureManager _features;
    private readonly ProviderManager _providers;
    private readonly WorkspaceManager _savedWorkspaces;
    private readonly WorkspaceRuntimes _runtimes;

    /// <summary>The provider set built from the copies; null once closed.</summary>
    private ProviderSet? _set;

    /// <summary>Something was saved: closing reloads and restarts the workspace.</summary>
    private bool _anySaved;

    /// <param name="workspace">The workspace whose settings are edited; it need not be running.</param>
    /// <param name="config">The session's configuration, which the copies are read and saved through.</param>
    /// <param name="features">The session's feature system, which knows the workspace-scoped configs.</param>
    /// <param name="providers">The session's provider pool: the providers and their configs, and the models on this machine.</param>
    /// <param name="saved">The saved workspaces: every grid, for which workspaces use a model.</param>
    /// <param name="runtimes">The live workspaces, whose runtime of this one is restarted once a saved edit is closed.</param>
    public WorkspaceSettingsDraft(Workspace workspace, ConfigManager config, FeatureManager features, ProviderManager providers,
                                  WorkspaceManager saved, WorkspaceRuntimes runtimes)
    {
        Workspace  = workspace;
        _config    = config;
        _features  = features;
        _providers = providers;
        _savedWorkspaces = saved;
        _runtimes  = runtimes;

        providers.DiscoverAll();
        workspace.EnsureSharedServicesLoaded(providers);

        var dir = workspace.Dir;
        AiConfig = new AiConfig();
        config.LoadFrom(dir, AiConfig, AiConfig.ConfigName);
        Persona = new AiPersonaConfig();
        config.LoadFrom(dir, Persona, Persona.ConfigName);
        ProviderConfigs  = providers.LoadProviderConfigs(dir);
        WorkspaceConfigs = [.. features.Configs.WorkspaceScopedTypes.Select(type =>
        {
            var scoped = (IFeatureConfig)Activator.CreateInstance(type)!;
            config.LoadFrom(dir, scoped, scoped.ConfigName);
            return scoped;
        })];

        // The providers as the copies configure them — model-agnostic: no columns. Nexaflow's own providers are pooled instances
        // a set acquired again shares, so the models on this machine are read through the first set for as long as the edit is open.
        _set    = providers.AcquireProviderSet(ProviderConfigs, []);
        Shelf   = providers.Assets is { } assets ? new LocalModelShelf(_set, assets) : null;
        AiDraft = new AiConfigDraft(AiConfig, _set, Shelf);
    }

    public Workspace Workspace { get; }

    /// <summary>The copy of the workspace's ability grid edited, which <see cref="AiDraft"/> writes into.</summary>
    public AiConfig AiConfig { get; }

    /// <summary>The edit of the ability grid, shared by every page that edits it.</summary>
    public AiConfigDraft AiDraft { get; }

    /// <summary>The copy of the workspace's persona.</summary>
    public AiPersonaConfig Persona { get; }

    /// <summary>The copies of the workspace's providers' configs.</summary>
    public IReadOnlyList<IProviderConfig> ProviderConfigs { get; }

    /// <summary>The copies of the workspace's feature configs.</summary>
    public IReadOnlyList<IFeatureConfig> WorkspaceConfigs { get; }

    /// <summary>The models on this machine; null without an asset store.</summary>
    public LocalModelShelf? Shelf { get; }

    /// <summary>Every saved workspace's ability grid by name — this one's as the edit holds it — for which workspaces use a model.</summary>
    public IReadOnlyList<(string Workspace, AiConfig Config)> SavedGrids()
        => [.. _savedWorkspaces.Workspaces.Select(w => (w.Name, ReferenceEquals(w, Workspace) ? AiConfig : SavedGrid(w)))];

    /// <summary>Saves the copy of the ability grid to the workspace's folder.</summary>
    /// <exception cref="Exception">It could not be written; the message says why.</exception>
    public void SaveGrid()
    {
        _config.SaveTo(Workspace.Dir, AiConfig, AiConfig.ConfigName);
        _anySaved = true;
    }

    /// <summary>
    /// Saves one of the copies — the persona, a provider's config or a feature config — to the workspace's folder, under
    /// <paramref name="configName"/>. A provider's config builds the provider set again, so the providers' models are listed as
    /// it now configures them; the persona, which is a provider config too, does not.
    /// </summary>
    /// <exception cref="Exception">It could not be written; the message says why.</exception>
    public void Save(object config, string configName)
    {
        _config.SaveTo(Workspace.Dir, config, configName);
        _anySaved = true;
        if (config is IProviderConfig && !ReferenceEquals(config, Persona)) AcquireAgain();
    }

    /// <summary>The workspace was renamed, which moved its data folder: closing restarts its runtime, whose assistant holds the old
    /// path to its conversations.</summary>
    public void Renamed() => _anySaved = true;

    /// <summary>
    /// Lets go of the provider set and, where anything was saved, reloads the workspace from its folder and restarts its runtime, if
    /// one is running. True when something was saved.
    /// </summary>
    public bool Close()
    {
        if (_set is not null)
        {
            _providers.ReleaseProviderSet(_set);
            _set = null;
        }

        if (!_anySaved) return false;
        _anySaved = false;
        Workspace.ReloadSharedConfigs(_features.Configs, _providers);
        _runtimes.Restart(Workspace);
        return true;
    }

    private void AcquireAgain()
    {
        var fresh = _providers.AcquireProviderSet(ProviderConfigs, []);
        var old   = _set;
        _set = fresh;
        AiDraft.UseProviders(fresh, Shelf);
        if (old is not null) _providers.ReleaseProviderSet(old);
    }

    private AiConfig SavedGrid(Workspace workspace)
    {
        var config = new AiConfig();
        _config.LoadFrom(workspace.Dir, config, config.ConfigName);
        return config;
    }
}
