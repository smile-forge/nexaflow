using System.Windows.Controls;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Audio.Controls;

/// <summary>
/// Tiny pause/skip transport shown in the shell chrome (beside the activity ticker) while the audio tab is
/// backgrounded with Background play on. Bound to the page's <c>AudioViewModel</c> through its <see cref="AudioRemote"/>,
/// so it drives the same commands as the page and stays in sync.
/// </summary>
[ViewFor(typeof(AudioRemote))]
public partial class AudioMiniTransport : UserControl
{
    public AudioMiniTransport(AudioRemote remote)
    {
        InitializeComponent();
        DataContext = remote.Player;
    }
}
