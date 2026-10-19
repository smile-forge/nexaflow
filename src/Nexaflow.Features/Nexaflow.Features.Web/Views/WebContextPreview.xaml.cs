using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

/// <summary>
/// The Web page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// snapshot, title and address — see <c>WebContextPreview.xaml</c>.
/// </summary>
public partial class WebContextPreview : UserControl
{
    public WebContextPreview() => InitializeComponent();
}
