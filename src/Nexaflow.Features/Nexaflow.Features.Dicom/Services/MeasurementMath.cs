using System.Windows;
using Nexaflow.Features.Dicom.Models;

namespace Nexaflow.Features.Dicom.Services;

/// <summary>
/// The geometry behind the measurement readouts. Length/area convert pixels to millimetres via
/// <see cref="DicomInstanceInfo.PixelSpacingX"/>/<c>Y</c> (falling back to pixels, labelled <c>px</c>, when
/// the image is uncalibrated); ROI intensity stats sample raw values through a <see cref="PixelSampler"/>
/// and report HU for CT. All points are in source-image pixel space.
/// </summary>
internal static class MeasurementMath
{
    public static string LengthLabel(Point a, Point b, DicomInstanceInfo? info)
    {
        var dxPx = b.X - a.X;
        var dyPx = b.Y - a.Y;
        if (info is { HasSpatialCalibration: true })
        {
            var dx = dxPx * info.PixelSpacingX!.Value;
            var dy = dyPx * info.PixelSpacingY!.Value;
            return $"{Math.Sqrt(dx * dx + dy * dy):0.0} mm";
        }
        return $"{Math.Sqrt(dxPx * dxPx + dyPx * dyPx):0.0} px";
    }

    public static string AngleLabel(Point a, Point vertex, Point b)
    {
        var v1 = new Vector(a.X - vertex.X, a.Y - vertex.Y);
        var v2 = new Vector(b.X - vertex.X, b.Y - vertex.Y);
        if (v1.Length == 0 || v2.Length == 0) return "0.0°";
        return $"{Math.Abs(Vector.AngleBetween(v1, v2)):0.0}°";
    }

    /// <summary>Area + (when samplable) mean/SD intensity for a rectangular or elliptical ROI defined by
    /// two opposite corners in image space.</summary>
    public static string RoiLabel(Point c0, Point c1, bool ellipse, DicomInstanceInfo? info, PixelSampler? sampler)
    {
        var x0 = (int)Math.Round(Math.Min(c0.X, c1.X));
        var y0 = (int)Math.Round(Math.Min(c0.Y, c1.Y));
        var x1 = (int)Math.Round(Math.Max(c0.X, c1.X));
        var y1 = (int)Math.Round(Math.Max(c0.Y, c1.Y));
        var wPx = Math.Max(0, x1 - x0);
        var hPx = Math.Max(0, y1 - y0);

        // Area.
        string area;
        if (info is { HasSpatialCalibration: true })
        {
            var wMm = wPx * info.PixelSpacingX!.Value;
            var hMm = hPx * info.PixelSpacingY!.Value;
            var mm2 = ellipse ? Math.PI * (wMm / 2) * (hMm / 2) : wMm * hMm;
            area = $"{mm2:0.0} mm²";
        }
        else
        {
            var px2 = ellipse ? Math.PI * (wPx / 2.0) * (hPx / 2.0) : (double)wPx * hPx;
            area = $"{px2:0} px²";
        }

        // Intensity stats.
        var stats = IntensityStats(x0, y0, x1, y1, ellipse, sampler);
        return stats is null ? area : $"{area} · {stats}";
    }

    private static string? IntensityStats(int x0, int y0, int x1, int y1, bool ellipse, PixelSampler? sampler)
    {
        if (sampler is null || !sampler.Available) return null;

        var cx = (x0 + x1) / 2.0;
        var cy = (y0 + y1) / 2.0;
        var rx = Math.Max(0.5, (x1 - x0) / 2.0);
        var ry = Math.Max(0.5, (y1 - y0) / 2.0);

        double sum = 0, sumSq = 0;
        var n = 0;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            if (ellipse)
            {
                var nx = (x - cx) / rx;
                var ny = (y - cy) / ry;
                if (nx * nx + ny * ny > 1.0) continue;
            }
            var v = sampler.Value(x, y);
            if (v is null) continue;
            sum += v.Value;
            sumSq += v.Value * v.Value;
            n++;
        }

        if (n == 0) return null;
        var mean = sum / n;
        var variance = Math.Max(0, sumSq / n - mean * mean);
        var sd = Math.Sqrt(variance);
        var unit = sampler.IsHounsfield ? " HU" : "";
        return $"mean {mean:0}{unit}, SD {sd:0}";
    }
}
