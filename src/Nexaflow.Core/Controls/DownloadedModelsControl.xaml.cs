using System.Windows.Controls;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Controls;

/// <summary>The page of models on this machine's disk: deleting acts at once, so there is never anything to apply.</summary>
public partial class DownloadedModelsControl : UserControl, IConfigChangeTracker
{
    public DownloadedModelsControl(DownloadedModelsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) viewModel.Refresh(); };
    }

    public bool HasChanges => false;

    public event EventHandler? HasChangesChanged { add { } remove { } }
}
