using System.Windows.Controls;

namespace Nexaflow.Features.Audio.Controls;

/// <summary>
/// Tiny pause/skip transport shown in the shell chrome (beside the activity ticker) while the audio tab is
/// backgrounded with Background play on. Its <c>DataContext</c> is the page's <c>AudioViewModel</c>, so it
/// drives the same commands as the page and stays in sync.
/// </summary>
public partial class AudioMiniTransport : UserControl
{
    public AudioMiniTransport() => InitializeComponent();
}
