using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public event Action<string>? ApplyError;

    public ManageAiViewModel()
    {
        foreach (var config in ConfigManager.Instance.GetAll().OfType<IProviderConfig>())
        {
            var section = new ConfigEditViewModel(config, config.ConfigName, config.FriendlyName);
            Sections.Add(section);
        }

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
