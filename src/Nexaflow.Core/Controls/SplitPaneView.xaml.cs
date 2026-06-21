using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Renders a <see cref="ViewModels.SplitPaneNode"/>: two <see cref="PaneView"/>s side by side, separated by a
/// draggable splitter. Keeps each column's minimum at 30% of the split width so neither pane can be dragged
/// smaller than that — recomputed as the window resizes.
/// </summary>
public partial class SplitPaneView : UserControl
{
    public SplitPaneView() => InitializeComponent();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var min = 0.3 * e.NewSize.Width;
        LeftColumn.MinWidth  = min;
        RightColumn.MinWidth = min;
    }
}
