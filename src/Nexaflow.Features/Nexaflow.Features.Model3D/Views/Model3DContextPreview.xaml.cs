using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Model3D.Views;

/// <summary>
/// The 3D Model page's read-only reminder for the conversation context-preview panel. Pure XAML over the page's
/// snapshot, name and format — see <c>Model3DContextPreview.xaml</c>.
/// </summary>
[ViewFor(typeof(PagePreview<Model3DViewModel>))]
public partial class Model3DContextPreview : UserControl
{
    public Model3DContextPreview(PagePreview<Model3DViewModel> preview)
    {
        InitializeComponent();
        DataContext = preview.Page;
    }
}
