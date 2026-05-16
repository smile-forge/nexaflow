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

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext()
    {
        if (ViewModel.TotalImages == 0) return "Image viewer: no images loaded.";
        return ViewModel.TotalImages == 1
            ? $"Image viewer: '{ViewModel.CurrentFileName}'."
            : $"Image viewer: '{ViewModel.CurrentFileName}' ({ViewModel.CurrentIndex + 1} of {ViewModel.TotalImages}).";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];
}
