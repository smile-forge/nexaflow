using System.Windows.Controls;

namespace Nexaflow.Features.Svg.Views;

/// <summary>
/// The SVG page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// drawing and size — see <c>SvgContextPreview.xaml</c>.
/// </summary>
public partial class SvgContextPreview : UserControl
{
    public SvgContextPreview() => InitializeComponent();
}
