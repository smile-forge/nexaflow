using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

public partial class AiAbilityGridControl : UserControl, ICustomConfigApply
{
    private AiAbilityGridViewModel? _viewModel;

    public AiAbilityGridControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiConfig config) return;
        _viewModel  = new AiAbilityGridViewModel(config);
        DataContext = _viewModel;
    }

    public void Apply() => _viewModel?.Apply();
}
