using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

public partial class AiAbilityGridControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private AiAbilityGridViewModel? _viewModel;

    public AiAbilityGridControl()
    {
        InitializeComponent();
        Loaded           += OnLoaded;
        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            _viewModel?.ResetAddPanel();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) return;   // Loaded fires each time the page is shown again

        // The workspace panel hands over the grid it shares with the local-model pages; on its own, the grid is the config's.
        _viewModel = DataContext switch
        {
            AiAbilityGridViewModel shared => shared,
            AiConfig config               => new AiAbilityGridViewModel(config),
            _                             => null,
        };
        if (_viewModel is null) return;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AiAbilityGridViewModel.HasChanges))
                HasChangesChanged?.Invoke(this, EventArgs.Empty);
        };
        DataContext = _viewModel;
    }

    public void Apply() => _viewModel?.Apply();

    // IConfigChangeTracker
    public bool HasChanges => _viewModel?.HasChanges ?? false;
    public event EventHandler? HasChangesChanged;
}
