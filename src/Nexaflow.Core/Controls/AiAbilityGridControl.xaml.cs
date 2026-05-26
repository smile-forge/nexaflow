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
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged   += OnVisibleChanged;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            _viewModel?.ResetAddPanel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not AiConfig config) return;
        _viewModel = new AiAbilityGridViewModel(config);
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
