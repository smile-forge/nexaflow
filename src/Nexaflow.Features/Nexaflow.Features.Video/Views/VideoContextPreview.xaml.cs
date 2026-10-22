using System.Windows.Controls;

namespace Nexaflow.Features.Video.Views;

/// <summary>
/// The Video page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// poster frame and duration — see <c>VideoContextPreview.xaml</c>.
/// </summary>
public partial class VideoContextPreview : UserControl
{
    public VideoContextPreview() => InitializeComponent();
}
