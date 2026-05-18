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

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext()
    {
        if (string.IsNullOrEmpty(ViewModel.ProjectName)) return "Project detail: no project loaded.";
        var count = ViewModel.Backlog.Count;
        return count == 0
            ? $"Project: '{ViewModel.ProjectName}'. No backlog items."
            : $"Project: '{ViewModel.ProjectName}'. {count} backlog item(s).";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.Count == 0)
            ViewModel.RefreshCommand.Execute(null);
    }
}
