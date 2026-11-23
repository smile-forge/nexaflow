using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Web.Views;

/// <summary>
/// The Web page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// snapshot, title and address — see <c>WebContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<HtmlViewModel>))]
public partial class WebContextPreview : UserControl
{
    public WebContextPreview(PagePreview<HtmlViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
