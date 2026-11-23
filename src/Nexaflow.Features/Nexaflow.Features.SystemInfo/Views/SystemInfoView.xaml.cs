using System.Windows.Controls;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.SystemInfo.Views;

[ViewFor(typeof(SystemInfoViewModel))]
public partial class SystemInfoView : UserControl
{
    public SystemInfoView(SystemInfoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
