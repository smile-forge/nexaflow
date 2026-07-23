using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Render;
using Nexaflow.Features.Dicom.Models;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// Reads <b>raw stored pixel values</b> for one grayscale frame so the pixel probe and ROI stats report
/// real numbers (and Hounsfield units for CT) rather than the windowed display value. Built lazily per
/// selected frame; degrades to "unavailable" for colour images or any decode failure so measurement never
/// throws.
/// </summary>
internal sealed class PixelSampler
{
    private readonly IPixelData? _pixels;
    private readonly DicomInstanceInfo _info;

    public PixelSampler(DicomInstanceInfo info, int frame)
    {
        _info = info;
        try
        {
            DicomBootstrap.EnsureInitialized();
            var dcm = DicomFile.Open(info.FilePath);
            var header = DicomPixelData.Create(dcm.Dataset);
            _pixels = PixelDataFactory.Create(header, frame);
        }
        catch
        {
            _pixels = null;
        }
    }

    public bool Available => _pixels is not null;
    public bool IsHounsfield => _info.IsHounsfield;

    /// <summary>Raw stored value at a source-pixel coordinate, or null if out of range/unavailable.</summary>
    public double? Raw(int x, int y)
    {
        if (_pixels is null || x < 0 || y < 0 || x >= _pixels.Width || y >= _pixels.Height) return null;
        try { return _pixels.GetPixel(x, y); }
        catch { return null; }
    }

    /// <summary>Modality-rescaled value: Hounsfield units for CT, otherwise the raw value.</summary>
    public double? Value(int x, int y)
    {
        var raw = Raw(x, y);
        if (raw is null) return null;
        return _info.IsHounsfield ? raw.Value * _info.RescaleSlope + _info.RescaleIntercept : raw;
    }
}
