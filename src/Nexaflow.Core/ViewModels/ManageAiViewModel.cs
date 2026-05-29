using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Core.ViewModels;

public partial class ManageAiViewModel : ObservableObject
{
    public ObservableCollection<ConfigEditViewModel> Sections { get; } = [];

    [ObservableProperty] private ConfigEditViewModel? _selectedSection;

    private ConfigEditViewModel? _listeningSection;
    private readonly WorkContext _workContext;

    public event Action<string>? ApplyError;

    public ManageAiViewModel(WorkContext workContext)
    {
        _workContext = workContext;

        // First section: the WorkContext-specific AI ability grid
        Sections.Add(new ConfigEditViewModel(
            workContext.AiConfig,
            workContext.AiConfig.ConfigName,
            workContext.AiConfig.FriendlyName));

        // Remaining sections: global provider configs (API keys etc — still in ConfigManager)
        foreach (var config in ConfigManager.Instance.GetAll().OfType<IProviderConfig>())
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

        if (ReferenceEquals(SelectedSection.RealConfig, _workContext.AiConfig))
        {
            // WorkContext-specific AiConfig: hot-reload the AIService and persist to the context's
            // own folder; also persist the context list metadata so the context is known.
            _workContext.AiService?.LoadAbilityConfig(_workContext.AiConfig);
            try
            {
                WorkContextManager.Instance.SaveContextAiConfig(_workContext);
                WorkContextManager.Instance.SaveConfig();
                SelectedSection.ResetChanges();
            }
            catch (Exception ex)
            {
                ApplyError?.Invoke($"Could not save AI settings: {ex.Message}");
            }
        }
        else
        {
            // Global provider config — standard save
            try
            {
                ConfigManager.Instance.Save(SelectedSection.RealConfig, SelectedSection.ConfigName);
                SelectedSection.ResetChanges();
            }
            catch (Exception ex)
            {
                ApplyError?.Invoke($"Could not save {SelectedSection.FriendlyName} settings: {ex.Message}");
            }
        }
    }
}
