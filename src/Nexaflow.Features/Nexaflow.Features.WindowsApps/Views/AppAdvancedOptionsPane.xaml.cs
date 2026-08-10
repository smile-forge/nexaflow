using System.Windows.Controls;

namespace Nexaflow.Features.WindowsApps.Views;

/// <summary>
/// The Store app "Advanced options" side pane. Purely declarative — its DataContext is the
/// <see cref="ViewModels.AppAdvancedOptionsViewModel"/> the list hands it, including the close command,
/// so there is nothing for the code-behind to wire.
/// </summary>
public partial class AppAdvancedOptionsPane : UserControl
{
    public AppAdvancedOptionsPane() => InitializeComponent();
}
