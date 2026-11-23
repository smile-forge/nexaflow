using System.Windows.Controls;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Audio.Views;

/// <summary>
/// The Audio page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// now-playing state — see <c>AudioContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<AudioViewModel>))]
public partial class AudioContextPreview : UserControl
{
    public AudioContextPreview(PagePreview<AudioViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
