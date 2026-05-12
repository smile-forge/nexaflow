using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.ViewModels;

namespace Nexaflow.Features.Projects.Views;

public partial class ProjectsView : UserControl, IRefreshable
{
    public ProjectsViewModel ViewModel { get; }

    public ProjectsView(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ViewModel.RefreshCommand.Execute(null);
}
