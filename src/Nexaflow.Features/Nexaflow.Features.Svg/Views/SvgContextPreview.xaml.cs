using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Svg.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Svg.Views;

/// <summary>
/// The SVG page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// drawing and size — see <c>SvgContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<SvgViewModel>))]
public partial class SvgContextPreview : UserControl
{
    public SvgContextPreview(PagePreview<SvgViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
