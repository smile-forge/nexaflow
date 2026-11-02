using System.Windows.Controls;

namespace Nexaflow.Features.Images.Views;

/// <summary>The selection drawn by hand, in the Cut out and Retouch panels alike: bound to an
/// <see cref="ViewModels.ImageEditorViewModel"/>, whose <see cref="ViewModels.RegionMarking"/> it sets.</summary>
public partial class RegionMarkingPanel : UserControl
{
    public RegionMarkingPanel() => InitializeComponent();
}
