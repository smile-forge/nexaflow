using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Video.Views;

/// <summary>
/// The Video page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// poster frame and duration — see <c>VideoContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<VideoViewModel>))]
public partial class VideoContextPreview : UserControl
{
    public VideoContextPreview(PagePreview<VideoViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
