using System.Windows.Controls;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Projects.Views;

public partial class ProjectDetailView : UserControl, IPageView
{
    public ProjectDetailViewModel ViewModel { get; }

    public ProjectDetailView(ProjectDetailViewModel viewModel)
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
