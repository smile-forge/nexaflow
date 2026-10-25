using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FellowOakDicom;
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

    private DicomRenderer(DicomDataset dataset)
    {
        Dataset = dataset;
        _image  = new DicomImage(dataset);
    }

    /// <summary>Opens the instance at <paramref name="filePath"/> for drawing. A file on this disk is read lazily — fo-dicom
    /// loads frames as they are drawn; one in an archive or on a location is read whole.</summary>
    public static async Task<DicomRenderer> OpenAsync(DicomIo io, string filePath, CancellationToken ct = default)
    {
        DicomBootstrap.EnsureInitialized();
        var option = io.IsOnDisk(filePath) ? FileReadOption.ReadLargeOnDemand : FileReadOption.ReadAll;
        return new DicomRenderer((await io.OpenAsync(filePath, option, ct)).Dataset);
    }

    /// <summary>The instance as read — its header for the overlays, its pixels for the measurements.</summary>
    public DicomDataset Dataset { get; }

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

    /// <summary>Photographic-negative of the windowed output. We invert the rendered BGRA ourselves rather
    /// than toggle <see cref="DicomImage.Invert"/> — toggling that flag on a reused image corrupts its
    /// pipeline (it washes the frame to solid white instead of inverting).</summary>
    public bool Invert { get; set; }

    /// <summary>Renders <paramref name="frame"/> at the current window/level as a frozen BGRA32 bitmap
    /// (frozen so it can cross threads to the UI safely).</summary>
    public BitmapSource Render(int frame)
    {
        using var img = _image.RenderImage(frame);
        var bytes = img.As<byte[]>();
        var w = img.Width;
        var h = img.Height;

        if (Invert)
            for (var i = 0; i + 3 < bytes.Length; i += 4)   // BGRA — invert colour channels, keep alpha
            {
                bytes[i]     = (byte)(255 - bytes[i]);
                bytes[i + 1] = (byte)(255 - bytes[i + 1]);
                bytes[i + 2] = (byte)(255 - bytes[i + 2]);
            }

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), bytes, w * 4, 0);
        bmp.Freeze();
        return bmp;
    }
}
