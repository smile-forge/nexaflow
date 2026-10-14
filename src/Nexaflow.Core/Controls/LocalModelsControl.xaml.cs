using System.Windows.Controls;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The Local page: a model chosen for each job the models on this machine do. A choice takes effect at once, written into the
/// ability grid this page shares with the AI page, whose other edits apply with either page.
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
