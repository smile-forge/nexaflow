using System.Windows.Controls;
using Nexaflow.Features.Images.ViewModels;

namespace Nexaflow.Features.Images.Views;

public partial class ImageView : UserControl
{
    public ImageViewModel ViewModel { get; }

    public ImageView(ImageViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }
}
