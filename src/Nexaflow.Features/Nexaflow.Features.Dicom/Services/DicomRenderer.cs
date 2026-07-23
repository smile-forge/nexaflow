using System.Windows.Media;
using System.Windows.Media.Imaging;
using FellowOakDicom.Imaging;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Wraps a fo-dicom <see cref="DicomImage"/> for one image SOP instance and renders frames to a WPF
/// <see cref="WriteableBitmap"/>. fo-dicom's core <see cref="RawImageManager"/> yields a BGRA32 buffer
/// (via <c>IImage.As&lt;byte[]&gt;()</c>), which maps directly to <see cref="PixelFormats.Bgra32"/> — so we
/// never touch System.Drawing. Window/level and invert are set on the underlying image, then re-rendered.
/// </summary>
internal sealed class DicomRenderer
{
    private readonly DicomImage _image;

    public DicomRenderer(string filePath)
    {
        DicomBootstrap.EnsureInitialized();
        _image = new DicomImage(filePath);
    }

    public int Frames => _image.NumberOfFrames;
    public int Width => _image.Width;
    public int Height => _image.Height;
    public bool IsGrayscale => _image.IsGrayscale;

    /// <summary>Current window width. fo-dicom seeds it from the dataset's VOI or an auto value.</summary>
    public double WindowWidth
    {
        get => _image.WindowWidth;
        set => _image.WindowWidth = value;
    }

    public double WindowCenter
    {
        get => _image.WindowCenter;
        set => _image.WindowCenter = value;
    }

    public bool Invert
    {
        get => _image.Invert;
        set => _image.Invert = value;
    }

    /// <summary>Renders <paramref name="frame"/> at the current window/level as a frozen BGRA32 bitmap
    /// (frozen so it can cross threads to the UI safely).</summary>
    public BitmapSource Render(int frame)
    {
        using var img = _image.RenderImage(frame);
        var bytes = img.As<byte[]>();
        var w = img.Width;
        var h = img.Height;

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), bytes, w * 4, 0);
        bmp.Freeze();
        return bmp;
    }
}
