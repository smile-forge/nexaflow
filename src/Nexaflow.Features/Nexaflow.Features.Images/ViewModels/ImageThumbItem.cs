using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// One cell in the album / explore / collage views. Starts with a null <see cref="Thumbnail"/> (the view
/// shows a placeholder) which the background populate loop replaces once the shell yields the image.
/// </summary>
public partial class ImageThumbItem : ObservableObject
{
    /// <summary>Position in the viewer's list. Re-numbered when an image is deleted, so it's observable.</summary>
    [ObservableProperty] private int _index;

    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;

    [ObservableProperty] private BitmapSource? _thumbnail;

    /// <summary>True for the item matching the viewer's current index (highlighted in the Explore strip).</summary>
    [ObservableProperty] private bool _isSelected;

    // ── Collage layout (scattered canvas placement; set once by the view-model) ──
    [ObservableProperty] private double _collageX;
    [ObservableProperty] private double _collageY;
    [ObservableProperty] private double _collageRotation;
}
