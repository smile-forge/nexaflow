using System.Windows.Controls;

namespace Nexaflow.Features.Audio.Views;

/// <summary>
/// The Audio page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// now-playing state — see <c>AudioContextPreview.xaml</c>.
/// </summary>
public partial class AudioContextPreview : UserControl
{
    public AudioContextPreview() => InitializeComponent();
}
