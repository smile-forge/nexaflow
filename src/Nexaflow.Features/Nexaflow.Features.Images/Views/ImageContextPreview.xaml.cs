using Nexaflow.Features.Images.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// The Image page's read-only view for the conversation context-preview panel: one thumbnail per image in
/// the tab, the current one highlighted. Pure XAML over the page's thumbnails — see
/// <c>ImageContextPreview.xaml</c>; the code here only opens the list on the current image.
/// </summary>
public partial class ImageContextPreview : UserControl
{
    public ImageContextPreview() => InitializeComponent();

    // Open on the image the AI is being shown rather than the top of the set, where in a long folder it is
    // off-screen. Scrolled by index through the panel, since that card may not have been realised yet.
    private void ItemsHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is VirtualizingPanel panel
            && DataContext is ImageViewModel vm
            && vm.CurrentIndex >= 0 && vm.CurrentIndex < vm.Thumbnails.Count)
            panel.BringIndexIntoViewPublic(vm.CurrentIndex);
    }
}
