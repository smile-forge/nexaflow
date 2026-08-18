using System.Windows.Controls;

namespace Nexaflow.Features.Network.Views;

/// <summary>The side panel. Its DataContext is the page's view-model, so it reads the selection rather than
/// being handed a copy of it.</summary>
public partial class DevicePanel : UserControl
{
    public DevicePanel() => InitializeComponent();
}
