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

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext()
    {
        if (ViewModel.ProjectCount == 0) return "Projects list: no projects.";
        var selected = ViewModel.SelectedProject is { } p ? $" Selected: '{p.DisplayName}'." : string.Empty;
        return $"Projects list: {ViewModel.ProjectCount} project(s).{selected}";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.Count == 0)
            ViewModel.RefreshCommand.Execute(null);
    }
}
