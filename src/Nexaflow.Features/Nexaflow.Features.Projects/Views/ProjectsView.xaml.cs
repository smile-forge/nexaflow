using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.ViewModels;

namespace Nexaflow.Features.Projects.Views;

public partial class ProjectsView : UserControl, IPageView, IRefreshable
{
    public ProjectsViewModel ViewModel { get; }

    public ProjectsView(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext()
    {
        if (ViewModel.ProjectCount == 0) return "Projects list: no projects.";
        var selected = ViewModel.SelectedProject is { } p ? $" Selected: '{p.DisplayName}'." : string.Empty;
        return $"Projects list: {ViewModel.ProjectCount} project(s).{selected}";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ViewModel.RefreshCommand.Execute(null);
}
