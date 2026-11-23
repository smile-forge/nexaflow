using System.Windows.Controls;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Projects.Views;

[ViewFor(typeof(ProjectDetailViewModel))]
public partial class ProjectDetailView : UserControl
{
    public ProjectDetailView(ProjectDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
