using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The per-workspace "Configure" panel. Drives a <see cref="Workspace"/> (which may have no live
/// <see cref="WorkspaceRuntime"/>). To avoid disrupting a running workspace mid-edit, the panel works on a
/// SEPARATE set of config instances loaded from the workspace's folder — <see cref="Apply"/> writes
/// them to disk only; the live instances are untouched. If anything was applied, <see cref="Close"/>
/// reloads the workspace from disk and rebuilds the live workspace once (no per-apply popup).
/// </summary>
public partial class WorkspaceConfigViewModel : ObservableObject
{
    public ObservableCollection<ConfigEditViewModel> Sections { get; } = [];

    [ObservableProperty] private ConfigEditViewModel? _selectedSection;

    /// <summary>Header text — names the workspace being configured (it may not be the active one).
    /// Observable so it follows a rename made on the identity page.</summary>
    [ObservableProperty] private string _title;

    private ConfigEditViewModel? _listeningSection;

    /// <summary>The identity (name/symbol/colour) page — persists itself, so it's special-cased in Apply.</summary>
    private ConfigEditViewModel? _identitySection;

    /// <summary>The default-tabs page — edits the live workspace's tabset and persists itself (SaveWorkspaces),
    /// so like the identity page it's special-cased in Apply (no per-folder config file).</summary>
    private ConfigEditViewModel? _defaultTabsSection;

    private readonly Workspace _workspace;

    // Disk-backed EDITING copies — independent of the workspace's live instances.
    private readonly AiConfig                       _aiConfig;
    private readonly AiPersonaConfig                _persona;
    private IReadOnlyList<IProviderConfig>          _providerConfigs;
    private readonly IReadOnlyList<IFeatureConfig>  _workspaceConfigs;

    // Provider set the ability grid queries for model lists; re-acquired when a key changes.
    private ProviderSet? _editingProviderSet;

    /// <summary>The ability grid being edited — one, shared by the AI page and every local-model page, so a model
    /// chosen on either is the same edit.</summary>
    private readonly AiAbilityGridViewModel _grid;

    /// <summary>The models on this machine, read from the editing provider set; null without an asset store.</summary>
    private readonly LocalModelShelf? _shelf;

    /// <summary>The app's downloads of models on this machine; null where the app has none.</summary>
    private readonly LocalModelDownloads? _downloads;

    /// <summary>The pages whose apply writes the ability grid, saved as <see cref="_aiConfig"/>.</summary>
    private readonly List<ConfigEditViewModel> _gridSections = [];

    // Set once any section is applied to disk — drives the single rebuild on Close.
    private bool _dirty;

    public event Action<string>? ApplyError;

    private readonly Nexaflow.Features.Common.IShellServices? _shell;

    public WorkspaceConfigViewModel(Workspace workspace, Nexaflow.Features.Common.IShellServices? shell = null)
    {
        _workspace = workspace;
        _shell   = shell;
        _title   = Str.Format("Shell.Workspace.Configure.TitleFormat", workspace.Name);

        // Discover provider plugins, then make sure the workspace's folder + scoped-type list exist.
        ProviderManager.Instance.DiscoverAll();
        workspace.EnsureSharedServicesLoaded();

        var dir = workspace.Dir;

        _aiConfig = new AiConfig();
        ConfigManager.Instance.LoadFrom(dir, _aiConfig, _aiConfig.ConfigName);

        _persona = new AiPersonaConfig();
        ConfigManager.Instance.LoadFrom(dir, _persona, _persona.ConfigName);

        _providerConfigs  = ProviderManager.Instance.LoadProviderConfigs(dir);
        _workspaceConfigs = LoadEditingWorkspaceConfigs(dir);

        // Capability instances for the ability grid's model lists (model-agnostic; columns: none).
        _editingProviderSet  = ProviderManager.Instance.AcquireProviderSet(_providerConfigs, []);
        _aiConfig.Providers  = _editingProviderSet;
        _shelf               = ProviderManager.Instance.Assets is { } assets ? new LocalModelShelf(_editingProviderSet, assets) : null;
        _downloads           = ProviderManager.Instance.Service<LocalModelDownloads>();
        _grid                = new AiAbilityGridViewModel(_aiConfig, _shelf, _downloads);

        BuildSections();
        SelectedSection = Sections.FirstOrDefault();
    }

    /// <summary>Lands on the section whose config name matches (deep link from a feature's Configure
    /// button), falling back to the current selection when there's no such section.</summary>
    public void SelectSection(string configName)
    {
        var match = Sections.FirstOrDefault(s =>
            string.Equals(s.ConfigName, configName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) SelectedSection = match;
    }

    private void BuildSections()
    {
        // First page: the workspace identity (name/symbol/colour). The control edits the live workspace
        // directly on Apply, so it carries no per-folder config (special-cased in Apply below).
        var identityControl = new WorkspaceIdentityControl { DataContext = _workspace };
        _identitySection = ConfigEditViewModel.ForCustomControl(identityControl, "workspace-identity", Str.Get("Shell.Config.WorkspaceIdentity"));
        Sections.Add(_identitySection);

        // Startup tabset editor (delete-only; captured via the workspace icon's "Use Tabset as Default").
        var defaultTabsControl = new WorkspaceDefaultTabsControl { DataContext = _workspace };
        _defaultTabsSection = ConfigEditViewModel.ForCustomControl(defaultTabsControl, "workspace-default-tabs", Str.Get("Shell.Config.WorkspaceDefaultTabs"));
        Sections.Add(_defaultTabsSection);

        Sections.Add(GridSection(new AiAbilityGridControl { DataContext = _grid }, _aiConfig.ConfigName, _aiConfig.FriendlyName));

        // The models on this machine: a page choosing one for each job, and one of what is on disk.
        if (_shelf is not null && _downloads is not null)
        {
            var tools = ProviderManager.Instance.Services<ILocalTool>();
            var local = new LocalModelsViewModel(_shelf, _grid, _workspace, _downloads, CommitLocalChoice, tools);
            Sections.Add(GridSection(new LocalModelsControl(local, _grid), "local-models", Str.Get("Shell.Config.Local.Title")));
            Sections.Add(ConfigEditViewModel.ForCustomControl(new DownloadedModelsControl(new DownloadedModelsViewModel(_shelf, SavedGrids, tools)),
                                                              "downloaded-models", Str.Get("Shell.Config.Downloaded.Title")));
        }
        Sections.Add(new ConfigEditViewModel(_persona, _persona.ConfigName, _persona.FriendlyName));
        foreach (var config in _providerConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName));

        foreach (var config in _workspaceConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName, _shell));
    }

    private ConfigEditViewModel GridSection(System.Windows.FrameworkElement control, string configName, string title)
    {
        var section = ConfigEditViewModel.ForCustomControl(control, configName, title);
        _gridSections.Add(section);
        return section;
    }

    /// <summary>Saves a Local page choice of a model that is here, written into the grid's config at once. A choice that waits on
    /// a download is saved by the app's downloads when it lands, whether or not this panel is still open.</summary>
    private void CommitLocalChoice() => SaveGridConfig();

    /// <summary>Every workspace's grid as saved, by name — this one's as this panel holds it — for who uses which model.</summary>
    private IReadOnlyList<(string Workspace, AiConfig Config)> SavedGrids()
        => [.. WorkspaceManager.Instance.Workspaces.Select(w => (w.Name, ReferenceEquals(w, _workspace) ? _aiConfig : SavedGrid(w)))];

    private static AiConfig SavedGrid(Workspace workspace)
    {
        var config = new AiConfig();
        ConfigManager.Instance.LoadFrom(workspace.Dir, config, config.ConfigName);
        return config;
    }

    /// <summary>Saves the ability grid's config; every page that edits the grid is then unchanged.</summary>
    private bool SaveGridConfig()
    {
        try
        {
            ConfigManager.Instance.SaveTo(_workspace.Dir, _aiConfig, _aiConfig.ConfigName);
            _dirty = true;
            foreach (var s in _gridSections) s.ResetChanges();
            return true;
        }
        catch (Exception ex)
        {
            ApplyError?.Invoke(Str.Format("Shell.Options.SaveFailed", _aiConfig.FriendlyName, ex.Message));
            return false;
        }
    }

    private static List<IFeatureConfig> LoadEditingWorkspaceConfigs(string dir)
    {
        var list = new List<IFeatureConfig>();
        foreach (var t in FeatureManager.Instance.WorkspaceScopedConfigTypes)
        {
            var cfg = (IFeatureConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.LoadFrom(dir, cfg, cfg.ConfigName);
            list.Add(cfg);
        }
        return list;
    }

    /// <summary>
    /// Releases the editing provider set and, if anything was applied, reloads the workspace from disk
    /// and rebuilds the live workspace once. Called by the host when the panel closes. Returns true
    /// when changes were applied (so the host knows not to bounce back to the Options popup).
    /// </summary>
    public bool Close()
    {
        if (_editingProviderSet is not null)
        {
            ProviderManager.Instance.ReleaseProviderSet(_editingProviderSet);
            _editingProviderSet = null;
            _aiConfig.Providers = null;
        }

        if (!_dirty) return false;
        _dirty = false;

        // Pull the just-saved values into the live workspace, then rebuild the running workspace (if any).
        _workspace.ReloadSharedConfigs();
        if (WorkspaceManager.Instance.FindLiveWorkspace(_workspace) is { } ws)
            WorkspaceManager.Instance.RestartWorkspace(ws);
        return true;
    }

    partial void OnSelectedSectionChanged(ConfigEditViewModel? value)
    {
        if (_listeningSection is not null)
            _listeningSection.PropertyChanged -= OnSectionPropertyChanged;

        _listeningSection = value;

        if (_listeningSection is not null)
            _listeningSection.PropertyChanged += OnSectionPropertyChanged;

        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigEditViewModel.HasChanges)
                           or nameof(ConfigEditViewModel.IsValid)
                           or nameof(ConfigEditViewModel.IsRequiredSatisfied))
            ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() =>
        (SelectedSection?.HasChanges ?? false) &&
        (SelectedSection?.IsValid ?? true) &&
        (SelectedSection?.IsRequiredSatisfied ?? true);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (SelectedSection is { } section) ApplySection(section);
    }

    /// <summary>The pages holding edits not yet applied — what closing the panel now would lose.</summary>
    public IReadOnlyList<string> UnappliedSections => [.. Sections.Where(s => s.HasChanges).Select(s => s.FriendlyName)];

    /// <summary>Applies every page holding edits, as Apply does one; false when one could not be saved, which is
    /// reported and leaves the rest unapplied.</summary>
    public bool ApplyAllChanges()
    {
        foreach (var section in Sections.ToList())
            if (section.HasChanges && !ApplySection(section)) return false;
        return true;
    }

    private bool ApplySection(ConfigEditViewModel section)
    {
        // The identity page edits the live workspace directly (name/symbol/colour) and persists itself
        // (workspace list + on-disk folder move), so it skips the per-folder disk-save the config sections
        // take. A rename moved the data folder, so a live workspace must rebuild (its AIService captured
        // the old conversations path) — flag it dirty so Close() rebuilds; colour/symbol update live.
        if (ReferenceEquals(section, _identitySection))
        {
            var oldName = _workspace.Name;
            section.ApplyToReal();
            section.ResetChanges();
            Title = Str.Format("Shell.Workspace.Configure.TitleFormat", _workspace.Name);
            if (!string.Equals(oldName, _workspace.Name, StringComparison.Ordinal))
                _dirty = true;
            return true;
        }

        // The default-tabs page also edits the live workspace and persists itself (SaveWorkspaces). Its change
        // only affects future window startups, so it needs neither a per-folder disk save nor a workspace
        // rebuild — apply and return without touching _dirty.
        if (ReferenceEquals(section, _defaultTabsSection))
        {
            section.ApplyToReal();
            section.ResetChanges();
            return true;
        }

        // The AI page and the local-model pages edit one ability grid: applying any of them saves it, and every one of
        // them is then unchanged.
        if (_gridSections.Contains(section))
        {
            section.ApplyToReal();
            return SaveGridConfig();
        }

        section.ApplyToReal();   // clone -> editing instance (NOT the live workspace instance)

        try
        {
            ConfigManager.Instance.SaveTo(_workspace.Dir, section.RealConfig, section.ConfigName);
            section.ResetChanges();
            _dirty = true;

            // A provider config (not the persona, which is also an IProviderConfig) changed — rebuild the
            // editing provider set so the grid can list models with the new key.
            if (section.RealConfig is IProviderConfig && !ReferenceEquals(section.RealConfig, _persona))
                ReacquireEditingProviderSet();
            return true;
        }
        catch (Exception ex)
        {
            ApplyError?.Invoke(Str.Format("Shell.Options.SaveFailed", section.FriendlyName, ex.Message));
            return false;
        }
    }

    private void ReacquireEditingProviderSet()
    {
        var fresh = ProviderManager.Instance.AcquireProviderSet(_providerConfigs, []);
        var old   = _editingProviderSet;
        _editingProviderSet = fresh;
        _aiConfig.Providers = fresh;            // same AiConfig instance the grid holds — picks it up
        if (old is not null) ProviderManager.Instance.ReleaseProviderSet(old);
    }
}
