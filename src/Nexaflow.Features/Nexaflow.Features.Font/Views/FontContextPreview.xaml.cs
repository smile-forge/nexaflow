using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Font.Views;

/// <summary>
/// The Font page's read-only view for the conversation context-preview panel. Pure XAML over the page's
/// font list — see <c>FontContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<FontViewModel>))]
public partial class FontContextPreview : UserControl
{
    public FontContextPreview(PagePreview<FontViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
