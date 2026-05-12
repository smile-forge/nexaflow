using System.Windows.Controls;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects.Views;

public partial class ProjectDetailView : UserControl, IRefreshable
{
    public ProjectDetailViewModel ViewModel { get; }

    public ProjectDetailView(ProjectDetailViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ViewModel.RefreshCommand.Execute(null);
}
