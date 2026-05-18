using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.ViewModels;

namespace Nexaflow.Features.Projects.Views;

public partial class ProjectsView : UserControl, IPageView
{
    public ProjectsViewModel ViewModel { get; }

    public ProjectsView(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.Count == 0)
            ViewModel.RefreshCommand.Execute(null);
    }
}
