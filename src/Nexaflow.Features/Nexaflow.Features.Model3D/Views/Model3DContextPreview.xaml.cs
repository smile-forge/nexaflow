using System.Windows.Controls;

namespace Nexaflow.Features.Model3D.Views;

/// <summary>
/// The 3D Model page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// snapshot, name and format — see <c>Model3DContextPreview.xaml</c>.
/// </summary>
public partial class Model3DContextPreview : UserControl
{
    public Model3DContextPreview() => InitializeComponent();
}
