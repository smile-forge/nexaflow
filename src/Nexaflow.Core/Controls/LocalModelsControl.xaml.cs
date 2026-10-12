using System.Windows.Controls;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The page for a provider's local model files. Fetching and removing act at once; choosing a model edits the ability
/// grid this page shares with the AI page, so applying either page applies both.
/// </summary>
public partial class LocalModelsControl : UserControl, ICustomConfigApply, IConfigChangeTracker
{
    private readonly AiAbilityGridViewModel _grid;

    public LocalModelsControl(LocalModelsViewModel viewModel, AiAbilityGridViewModel grid)
    {
        InitializeComponent();
        DataContext = viewModel;
        _grid       = grid;
        _grid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiAbilityGridViewModel.HasChanges))
                HasChangesChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Apply() => _grid.Apply();

    public bool HasChanges => _grid.HasChanges;
    public event EventHandler? HasChangesChanged;
}
