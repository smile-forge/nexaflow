using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The per-workspace "Configure" panel. Drives a <see cref="Profile"/> (which may have no live
/// <see cref="Workspace"/>). To avoid disrupting a running workspace mid-edit, the panel works on a
/// SEPARATE set of config instances loaded from the profile's folder — <see cref="Apply"/> writes
/// them to disk only; the live instances are untouched. If anything was applied, <see cref="Close"/>
/// reloads the profile from disk and rebuilds the live workspace once (no per-apply popup).
/// </summary>
public partial class ManageAiViewModel : ObservableObject
{
    public ObservableCollection<ConfigEditViewModel> Sections { get; } = [];

    [ObservableProperty] private ConfigEditViewModel? _selectedSection;

    /// <summary>Header text — names the workspace being configured (it may not be the active one).
    /// Observable so it follows a rename made on the identity page.</summary>
    [ObservableProperty] private string _title;

    private ConfigEditViewModel? _listeningSection;

    /// <summary>The identity (name/symbol/colour) page — persists itself, so it's special-cased in Apply.</summary>
    private ConfigEditViewModel? _identitySection;

    private readonly Profile _profile;

    // Disk-backed EDITING copies — independent of the profile's live instances.
    private readonly AiConfig                       _aiConfig;
    private readonly AiPersonaConfig                _persona;
    private IReadOnlyList<IProviderConfig>          _providerConfigs;
    private readonly IReadOnlyList<IFeatureConfig>  _workspaceConfigs;

    // Provider set the ability grid queries for model lists; re-acquired when a key changes.
    private ProviderSet? _editingProviderSet;

    // Set once any section is applied to disk — drives the single rebuild on Close.
    private bool _dirty;

    public event Action<string>? ApplyError;

    public ManageAiViewModel(Profile profile)
    {
        _profile = profile;
        _title   = $"Configure Workspace — {profile.Name}";

        // Discover provider plugins, then make sure the profile's folder + scoped-type list exist.
        ProviderManager.Instance.DiscoverAll();
        profile.EnsureSharedServicesLoaded();

        var dir = profile.Dir;

        _aiConfig = new AiConfig();
        ConfigManager.Instance.LoadFrom(dir, _aiConfig, _aiConfig.ConfigName);

        _persona = new AiPersonaConfig();
        ConfigManager.Instance.LoadFrom(dir, _persona, _persona.ConfigName);

        _providerConfigs  = ProviderManager.Instance.LoadProviderConfigs(dir);
        _workspaceConfigs = LoadEditingWorkspaceConfigs(dir);

        // Capability instances for the ability grid's model lists (model-agnostic; columns: none).
        _editingProviderSet  = ProviderManager.Instance.AcquireProviderSet(_providerConfigs, []);
        _aiConfig.Providers  = _editingProviderSet;

        BuildSections();
        SelectedSection = Sections.FirstOrDefault();
    }

    private void BuildSections()
    {
        // First page: the workspace identity (name/symbol/colour). The control edits the live profile
        // directly on Apply, so it carries no per-folder config (special-cased in Apply below).
        var identityControl = new WorkspaceIdentityControl { DataContext = _profile };
        _identitySection = ConfigEditViewModel.ForCustomControl(identityControl, "workspace-identity", "Workspace");
        Sections.Add(_identitySection);

        Sections.Add(new ConfigEditViewModel(_aiConfig, _aiConfig.ConfigName, _aiConfig.FriendlyName));
        Sections.Add(new ConfigEditViewModel(_persona, _persona.ConfigName, _persona.FriendlyName));
        foreach (var config in _providerConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName));
        foreach (var config in _workspaceConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName));
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
    /// Releases the editing provider set and, if anything was applied, reloads the profile from disk
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

        // Pull the just-saved values into the live profile, then rebuild the running workspace (if any).
        _profile.ReloadSharedConfigs();
        if (WorkspaceManager.Instance.FindLiveWorkspace(_profile) is { } ws)
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
        if (SelectedSection is null) return;
        var section = SelectedSection;

        // The identity page edits the live profile directly (name/symbol/colour) and persists itself
        // (profile list + on-disk folder move), so it skips the per-folder disk-save the config sections
        // take. A rename moved the data folder, so a live workspace must rebuild (its AIService captured
        // the old conversations path) — flag it dirty so Close() rebuilds; colour/symbol update live.
        if (ReferenceEquals(section, _identitySection))
        {
            var oldName = _profile.Name;
            section.ApplyToReal();
            section.ResetChanges();
            Title = $"Configure Workspace — {_profile.Name}";
            if (!string.Equals(oldName, _profile.Name, StringComparison.Ordinal))
                _dirty = true;
            return;
        }

        section.ApplyToReal();   // clone -> editing instance (NOT the live profile instance)

        try
        {
            ConfigManager.Instance.SaveTo(_profile.Dir, section.RealConfig, section.ConfigName);
            section.ResetChanges();
            _dirty = true;

            // A provider config (not the ability grid / persona, which are also IProviderConfig)
            // changed — rebuild the editing provider set so the grid can list models with the new key.
            if (section.RealConfig is IProviderConfig
                && !ReferenceEquals(section.RealConfig, _aiConfig)
                && !ReferenceEquals(section.RealConfig, _persona))
            {
                ReacquireEditingProviderSet();
            }
        }
        catch (Exception ex)
        {
            ApplyError?.Invoke($"Could not save {section.FriendlyName} settings: {ex.Message}");
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
