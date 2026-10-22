using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// A mesh shading's triangles painted into pixels, each pixel's colour interpolated between its triangle's corners — what
/// WPF cannot draw, having no triangle shaded by its corners, and what flat-coloured triangles side by side would leave
/// hairline seams between. Pixels no triangle covers stay clear, so what lies beneath shows through.
/// </summary>
internal static class PdfMeshPictures
{
    /// <summary>
    /// <paramref name="mesh"/> placed through <paramref name="toPixels"/> — from the shading's space to pixels of a
    /// picture <paramref name="width"/> × <paramref name="height"/> — at <paramref name="opacity"/>; null for a picture
    /// of no pixels.
    /// </summary>
    public static BitmapSource? Paint(PdfMesh mesh, Matrix toPixels, int width, int height, double opacity)
    {
        if (width <= 0 || height <= 0) return null;

        var pixels = new uint[width * height];
        var alpha = (uint)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        var points = mesh.Points;
        var values = mesh.Values;
        var channels = mesh.Channels;
        Span<Point> at = stackalloc Point[3];

        // The picture brought back into the shading's space, so a triangle wholly outside it is passed over unplaced.
        if (!toPixels.HasInverse) return null;
        var fromPixels = toPixels;
        fromPixels.Invert();
        var shown = PdfSpace.Transform(new Rect(0, 0, width, height), fromPixels);

        for (var t = 0; t < mesh.Triangles; t++)
        {
            var p = t * 6;
            if (Math.Max(points[p], Math.Max(points[p + 2], points[p + 4])) < shown.Left ||
                Math.Min(points[p], Math.Min(points[p + 2], points[p + 4])) > shown.Right ||
                Math.Max(points[p + 1], Math.Max(points[p + 3], points[p + 5])) < shown.Top ||
                Math.Min(points[p + 1], Math.Min(points[p + 3], points[p + 5])) > shown.Bottom)
                continue;
            for (var v = 0; v < 3; v++)
                at[v] = toPixels.Transform(new Point(points[t * 6 + v * 2], points[t * 6 + v * 2 + 1]));

            var area = Edge(at[0], at[1], at[2]);
            if (Math.Abs(area) < 1e-9) continue;

            var left = Math.Max(0, (int)Math.Floor(Math.Min(at[0].X, Math.Min(at[1].X, at[2].X))));
            var right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(at[0].X, Math.Max(at[1].X, at[2].X))));
            var top = Math.Max(0, (int)Math.Floor(Math.Min(at[0].Y, Math.Min(at[1].Y, at[2].Y))));
            var bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(at[0].Y, Math.Max(at[1].Y, at[2].Y))));
            if (left > right || top > bottom) continue;

            var first = t * 3 * channels;
            for (var y = top; y <= bottom; y++)
            {
                var centre = new Point(0, y + 0.5);
                for (var x = left; x <= right; x++)
                {
                    centre.X = x + 0.5;
                    // Each corner's weight is the share of the triangle the pixel's centre makes with the other two; a
                    // centre on a shared edge belongs to both triangles, so neither leaves a gap.
                    var w0 = Edge(at[1], at[2], centre) / area;
                    var w1 = Edge(at[2], at[0], centre) / area;
                    var w2 = 1 - w0 - w1;
                    const double edge = -1e-6;
                    if (w0 < edge || w1 < edge || w2 < edge) continue;

                    pixels[y * width + x] = Colour(mesh, values, first, channels, w0, w1, w2, alpha);
                }
            }
        }

        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        picture.Freeze();
        return picture;
    }

    private static double Edge(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>The colour at weights <paramref name="w0"/>, <paramref name="w1"/>, <paramref name="w2"/> of the triangle
    /// whose values start at <paramref name="first"/>, as a premultiplied BGRA pixel.</summary>
    private static uint Colour(PdfMesh mesh, float[] values, int first, int channels, double w0, double w1, double w2, uint alpha)
    {
        double Value(int channel) =>
            w0 * values[first + channel] + w1 * values[first + channels + channel] + w2 * values[first + 2 * channels + channel];

        double red, green, blue;
        if (mesh.Lookup is { } lookup)
        {
            var colour = lookup[Math.Clamp((int)Math.Round(Value(0) * (lookup.Length - 1)), 0, lookup.Length - 1)];
            (red, green, blue) = (colour.R / 255.0, colour.G / 255.0, colour.B / 255.0);
        }
        else
        {
            (red, green, blue) = (Value(0), Value(1), Value(2));
        }

        uint Premultiplied(double level) => (uint)Math.Clamp(Math.Round(level * alpha), 0, alpha);
        return (alpha << 24) | (Premultiplied(red) << 16) | (Premultiplied(green) << 8) | Premultiplied(blue);
    }
}
