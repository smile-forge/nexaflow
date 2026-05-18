using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using System.Windows.Controls;

namespace Nexaflow.Features.Images.Views;

public partial class ImageView : UserControl, IPageView
{
    public ImageViewModel ViewModel { get; }

    public ImageView(ImageViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
