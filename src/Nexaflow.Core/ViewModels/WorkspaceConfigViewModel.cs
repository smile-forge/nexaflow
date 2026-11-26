using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The per-workspace "Configure" panel: a page per section of a <see cref="WorkspaceSettingsDraft"/>, which edits copies of the
/// workspace's settings — the workspace may have no live <see cref="WorkspaceRuntime"/> — and, once anything was saved, reloads and
/// restarts the workspace when the panel closes. The identity and default-tabs pages edit the workspace itself and save themselves.
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

    /// <summary>The edit of the workspace's settings the pages show.</summary>
    private readonly WorkspaceSettingsDraft _settings;

    /// <summary>The ability grid being edited — one, shared by the AI page and every local-model page, so a model
    /// chosen on either is the same edit.</summary>
    private readonly AiAbilityGridViewModel _grid;

    /// <summary>The app's downloads of models on this machine; null where the app has none.</summary>
    private readonly LocalModelDownloads? _downloads;

    /// <summary>The pages whose apply writes the ability grid.</summary>
    private readonly List<ConfigEditViewModel> _gridSections = [];

    public event Action<string>? ApplyError;

    private readonly Nexaflow.Features.Common.IShellServices? _shell;

    /// <summary>The page each section is edited on where it has one of its own.</summary>
    private readonly ConfigPageModels _pages;

    /// <summary>The session's provider pool: the tools with models of their own, and the CUDA pack's download.</summary>
    private readonly ProviderManager _providers;

    /// <summary>The saved workspaces, which the identity and default-tabs pages save the workspace in.</summary>
    private readonly WorkspaceManager _savedWorkspaces;

    /// <summary>What rules on a setting the app does not decide alone: one it rules on shows as it rules.</summary>
    private readonly IConfigRulings _rulings;

    /// <param name="workspace">The workspace the panel configures.</param>
    /// <param name="session">What the window has of the session: the configuration the workspace's settings are read and saved
    /// through, the feature system that knows the workspace-scoped configs, the provider pool, the saved workspaces and the live
    /// runtimes.</param>
    /// <param name="pages">The page each section is edited on where it has one of its own.</param>
    /// <param name="shell">The shell of the window the panel is in: an <see cref="Nexaflow.Features.Common.IShellAware"/> page is
    /// handed it, and a path is browsed for with its pickers.</param>
    public WorkspaceConfigViewModel(Workspace workspace, WindowServices session, ConfigPageModels pages, Nexaflow.Features.Common.IShellServices? shell = null)
    {
        _providers       = session.Providers;
        _savedWorkspaces = session.SavedWorkspaces;
        _rulings         = session.Config;
        _shell   = shell;
        _pages   = pages;
        _title   = Str.Format("Shell.Workspace.Configure.TitleFormat", workspace.Name);

        _settings  = new WorkspaceSettingsDraft(workspace, session.Config, session.Features, session.Providers, session.SavedWorkspaces,
                                                session.Workspaces);
        _downloads = _providers.Service<LocalModelDownloads>();
        _grid      = new AiAbilityGridViewModel(_settings.AiDraft, _downloads);

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
        var workspace = _settings.Workspace;

        // First page: the workspace identity (name/symbol/colour). The control edits the live workspace
        // directly on Apply, so it carries no per-folder config (special-cased in Apply below).
        var identityControl = new WorkspaceIdentityControl(_savedWorkspaces, _shell) { DataContext = workspace };
        _identitySection = ConfigEditViewModel.ForCustomControl(identityControl, "workspace-identity", Str.Get("Shell.Config.WorkspaceIdentity"));
        Sections.Add(_identitySection);

        // Startup tabset editor (delete-only; captured via the workspace icon's "Use Tabset as Default").
        var defaultTabsControl = new WorkspaceDefaultTabsControl(_savedWorkspaces) { DataContext = workspace };
        _defaultTabsSection = ConfigEditViewModel.ForCustomControl(defaultTabsControl, "workspace-default-tabs", Str.Get("Shell.Config.WorkspaceDefaultTabs"));
        Sections.Add(_defaultTabsSection);

        Sections.Add(GridSection(new AiAbilityGridControl(_grid), _settings.AiConfig.ConfigName, _settings.AiConfig.FriendlyName));

        // The models on this machine: a page choosing one for each job, and one of what is on disk.
        if (_settings.Shelf is { } shelf && _downloads is not null)
        {
            var tools = _providers.Services<ILocalTool>();
            var local = new LocalModelsViewModel(shelf, _grid, workspace, _downloads, CommitLocalChoice, tools,
                                                 _providers.Service<CudaPackDownload>());
            Sections.Add(GridSection(new LocalModelsControl(local, _grid), "local-models", Str.Get("Shell.Config.Local.Title")));
            Sections.Add(ConfigEditViewModel.ForCustomControl(new DownloadedModelsControl(new DownloadedModelsViewModel(shelf, _settings.SavedGrids, tools)),
                                                              "downloaded-models", Str.Get("Shell.Config.Downloaded.Title")));
        }
        Sections.Add(new ConfigEditViewModel(_settings.Persona, _settings.Persona.ConfigName, _settings.Persona.FriendlyName, _rulings, _pages, _shell));
        foreach (var config in _settings.ProviderConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName, _rulings, _pages, _shell));

        foreach (var config in _settings.WorkspaceConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName, _rulings, _pages, _shell));
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

    /// <summary>Saves the ability grid's config; every page that edits the grid is then unchanged.</summary>
    private bool SaveGridConfig()
    {
        try
        {
            _settings.SaveGrid();
            foreach (var s in _gridSections) s.ResetChanges();
            return true;
        }
        catch (Exception ex)
        {
            ApplyError?.Invoke(Str.Format("Shell.Options.SaveFailed", _settings.AiConfig.FriendlyName, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Closes the edit: the provider set is let go and, if anything was saved, the workspace is reloaded and its runtime rebuilt
    /// once. Called by the host when the panel closes. Returns true when changes were applied (so the host knows not to bounce back
    /// to the Options popup).
    /// </summary>
    public bool Close() => _settings.Close();

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
        // The identity page edits the live workspace directly (name/symbol/colour) and persists itself (workspace list + on-disk
        // folder move); a rename moved the data folder, which the running workspace must be rebuilt to follow.
        if (ReferenceEquals(section, _identitySection))
        {
            var oldName = _settings.Workspace.Name;
            section.ApplyToReal();
            section.ResetChanges();
            Title = Str.Format("Shell.Workspace.Configure.TitleFormat", _settings.Workspace.Name);
            if (!string.Equals(oldName, _settings.Workspace.Name, StringComparison.Ordinal))
                _settings.Renamed();
            return true;
        }

        // The default-tabs page also edits the live workspace and persists itself; its change only affects future window startups.
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

        section.ApplyToReal();   // clone -> the edit's copy (NOT the live workspace's)
        try
        {
            _settings.Save(section.RealConfig, section.ConfigName);
            section.ResetChanges();
            return true;
        }
        catch (Exception ex)
        {
            ApplyError?.Invoke(Str.Format("Shell.Options.SaveFailed", section.FriendlyName, ex.Message));
            return false;
        }
    }
}
