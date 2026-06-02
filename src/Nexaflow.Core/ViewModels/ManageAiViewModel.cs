using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Core.ViewModels;

public partial class ManageAiViewModel : ObservableObject
{
    public ObservableCollection<ConfigEditViewModel> Sections { get; } = [];

    [ObservableProperty] private ConfigEditViewModel? _selectedSection;

    private ConfigEditViewModel? _listeningSection;
    private readonly Workspace _workspace;

    public event Action<string>? ApplyError;

    public ManageAiViewModel(Workspace workspace)
    {
        _workspace = workspace;

        // Ensure every provider plugin is loaded, then (re)build this workspace's provider set so
        // newly discovered providers and on-disk config changes are reflected in the editor.
        ProviderManager.Instance.DiscoverAll();
        WorkspaceManager.Instance.RefreshProviders(workspace);

        // First section: the profile's AI ability grid (shared across its Workspaces).
        Sections.Add(new ConfigEditViewModel(
            workspace.AiConfig,
            workspace.AiConfig.ConfigName,
            workspace.AiConfig.FriendlyName));

        // AI Customisation: the profile's assistant persona (name + system prompt). Per-profile, like
        // the ability grid — not part of the provider set, so it's added explicitly.
        var persona = workspace.Profile.Persona;
        Sections.Add(new ConfigEditViewModel(persona, persona.ConfigName, persona.FriendlyName));

        // Remaining sections: the profile's provider configs (API keys etc.).
        foreach (var config in workspace.Profile.ProviderConfigs)
            Sections.Add(new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName));

        SelectedSection = Sections.FirstOrDefault();
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
                           or nameof(ConfigEditViewModel.IsRequiredSatisfied))
            ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() =>
        (SelectedSection?.HasChanges ?? false) &&
        (SelectedSection?.IsRequiredSatisfied ?? true);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (SelectedSection is null) return;

        SelectedSection.ApplyToReal();
        var profile = _workspace.Profile;

        if (ReferenceEquals(SelectedSection.RealConfig, _workspace.AiConfig))
        {
            // Ability grid: persist to the profile's folder and hot-reload the AIService. The shared
            // AiConfig is already mutated, so every Workspace on this profile sees the new assignments.
            _workspace.AiService?.LoadAbilityConfig(_workspace.AiConfig);
            try
            {
                WorkspaceManager.Instance.SaveProfileAiConfig(profile);
                WorkspaceManager.Instance.SaveProfiles();
                SelectedSection.ResetChanges();
            }
            catch (Exception ex)
            {
                ApplyError?.Invoke($"Could not save AI settings: {ex.Message}");
            }
        }
        else if (SelectedSection.RealConfig is AiPersonaConfig)
        {
            // Persona is a per-profile config. ApplyToReal has already written the edits into the
            // profile's live Persona instance (which AIService reads live), so just persist it.
            try
            {
                WorkspaceManager.Instance.SaveProfilePersona(profile);
                SelectedSection.ResetChanges();
            }
            catch (Exception ex)
            {
                ApplyError?.Invoke($"Could not save AI customisation: {ex.Message}");
            }
        }
        else
        {
            // Provider config (API keys etc.): save to the profile's folder, then reconfigure THIS
            // workspace so its AIService is rebuilt — the pool loads the now-needed provider and
            // unloads any provider no longer referenced.
            try
            {
                ConfigManager.Instance.SaveTo(
                    profile.Dir, SelectedSection.RealConfig, SelectedSection.ConfigName);
                WorkspaceManager.Instance.ReconfigureWorkspace(_workspace);
                SelectedSection.ResetChanges();
            }
            catch (Exception ex)
            {
                ApplyError?.Invoke($"Could not save {SelectedSection.FriendlyName} settings: {ex.Message}");
            }
        }
    }
}
