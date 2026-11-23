using System.Windows.Controls;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Projects.Views;

[ViewFor(typeof(ProjectsViewModel))]
public partial class ProjectsView : UserControl
{
    public ProjectsView(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
