using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A laid page written down as text, exactly enough that two layouts that would paint differently read differently:
/// every piece with its place, the part of the page's text it stands for and how it is painted (clip, fade, mask,
/// turn), and every mark with what it draws — its kind, where, in what paint, with what pen — and a hash of its shape or
/// pixels. Outlines and pictures are hashed rather than written out, so a page of text is a few hundred bytes a run, not
/// every glyph's curves.
/// <para>
/// This is what a page's drawing is kept as once it has been held against a reference and accepted: a later layout that
/// writes the same text paints the same pixels, and one that writes different text says which piece moved.
/// </para>
/// </summary>
internal static class PdfLayoutSnapshot
{
    /// <summary>Lengths are written to a hundredth of a device-independent pixel: finer than any change a reader sees.</summary>
    private const string Length = "0.##";

    public static string Of(PortableTree tree)
    {
        var text = new StringBuilder();
        foreach (var piece in tree.Root.SelfAndDescendants())
        {
            text.Append(' ', piece.Depth).Append(piece.Kind).Append(' ').Append(Box(piece.Bounds));
            if (piece.Part is { } part) text.Append(" part ").Append(part.Start).Append('+').Append(part.Length);
            if (piece.Painting is { } paint) Painting(paint, text);
            text.Append('\n');

            foreach (var mark in piece.Marks)
            {
                text.Append(' ', piece.Depth + 1);
                Mark(mark, text);
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    private static void Painting(PortablePaint paint, StringBuilder text)
    {
        if (paint.Clip is { } clip) text.Append(" clip ").Append(Hash(clip));
        if (paint.Opacity < 1) text.Append(" opacity ").Append(Number(paint.Opacity));
        if (paint.Mask is { } mask) text.Append(" mask ").Append(Brush(mask.Brush()));
        if (paint.Turn is { Count: > 0 } turns)
            foreach (var turn in turns) text.Append(" turn ").Append(MatrixText(turn.Matrix.ToWpf()));
    }

    private static void Mark(PortableMark mark, StringBuilder text)
    {
        switch (mark)
        {
            case PortableGeometryMark geometry:
                var stroke = geometry.Stroke as WpfStroke;
                text.Append("geometry ").Append(Box(geometry.Covers))
                    .Append(" fill ").Append(Brush(geometry.Fill?.Brush()))
                    .Append(" stroke ").Append(Brush(stroke?.Ink?.Brush()))
                    .Append(" thickness ").Append(Number(stroke?.Thickness ?? 0));
                if (stroke?.Dashes is { Count: > 0 } dashes)
                    text.Append(" dashes ").Append(string.Join(',', System.Linq.Enumerable.Select(dashes, Number)));
                if (stroke?.Ends is { } pen)
                    text.Append(" pen ").Append(pen.StartCap).Append('/').Append(pen.EndCap).Append('/').Append(pen.DashCap)
                        .Append('/').Append(pen.Join).Append('/').Append(Number(pen.MiterLimit)).Append('/').Append(Number(pen.DashOffset));
                text.Append(" shape ").Append(Hash(geometry.Shape));
                break;
            case PortablePictureMark picture:
                text.Append("picture ").Append(Box(picture.Bounds)).Append(' ').Append(Image(((WpfPicture)picture.Picture).Source));
                break;
            case PortableWashMark wash:
                text.Append("wash ").Append(Box(wash.Bounds)).Append(' ').Append(Brush(wash.Fill.Brush()));
                break;
            default:
                text.Append(mark.GetType().Name).Append(' ').Append(Box(mark.Covers));
                break;
        }
    }

    private static string Brush(Brush? brush) => brush switch
    {
        null                       => "none",
        SolidColorBrush solid      => $"{solid.Color}@{Number(solid.Opacity)}",
        LinearGradientBrush linear => $"linear {Point(linear.StartPoint)}-{Point(linear.EndPoint)} {Stops(linear.GradientStops)} {Transform(linear)}",
        RadialGradientBrush radial => $"radial {Point(radial.Center)} r{Number(radial.RadiusX)} o{Point(radial.GradientOrigin)} {Stops(radial.GradientStops)} {Transform(radial)}",
        ImageBrush image           => $"image {Image(image.ImageSource)} {Box(image.Viewport)} {Transform(image)}",
        DrawingBrush drawing       => $"drawing {Box(drawing.Viewbox)} {drawing.TileMode} {Transform(drawing)} {Hash(drawing.Drawing)}",
        _                          => brush.GetType().Name,
    };

    private static string Stops(GradientStopCollection stops)
    {
        var text = new StringBuilder("[");
        foreach (var stop in stops) text.Append(stop.Color).Append('@').Append(Number(stop.Offset)).Append(' ');
        return text.Append(']').ToString();
    }

    private static string Transform(Brush brush) => brush.Transform is { } t && !t.Value.IsIdentity ? MatrixText(t.Value) : "-";

    private static string Image(ImageSource? source)
    {
        if (source is not BitmapSource bitmap) return source?.GetType().Name ?? "none";

        var stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return $"{bitmap.PixelWidth}x{bitmap.PixelHeight} {bitmap.Format} {Convert.ToHexString(SHA256.HashData(pixels))[..16]}";
    }

    /// <summary>A geometry by its path, through its transform, written at <see cref="Length"/> and hashed.</summary>
    private static string Hash(Geometry geometry)
    {
        var path = PathGeometry.CreateFromGeometry(geometry);
        var text = new StringBuilder(path.FillRule.ToString());
        if (geometry.Transform is { Value.IsIdentity: false } turned) text.Append(MatrixText(turned.Value));
        foreach (var figure in path.Figures)
        {
            text.Append('M').Append(Point(figure.StartPoint));
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line: text.Append('L').Append(Point(line.Point)); break;
                    case PolyLineSegment lines: foreach (var p in lines.Points) text.Append('L').Append(Point(p)); break;
                    case BezierSegment bezier: text.Append('C').Append(Point(bezier.Point1)).Append(Point(bezier.Point2)).Append(Point(bezier.Point3)); break;
                    case PolyBezierSegment beziers: foreach (var p in beziers.Points) text.Append('C').Append(Point(p)); break;
                    case QuadraticBezierSegment quad: text.Append('Q').Append(Point(quad.Point1)).Append(Point(quad.Point2)); break;
                    case PolyQuadraticBezierSegment quads: foreach (var p in quads.Points) text.Append('Q').Append(Point(p)); break;
                    case ArcSegment arc: text.Append('A').Append(Point(arc.Point)).Append(Number(arc.Size.Width)).Append(Number(arc.Size.Height)); break;
                }
            }

            if (figure.IsClosed) text.Append('Z');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>An outline the medium made, by the geometry WPF holds it as.</summary>
    private static string Hash(LayoutShape shape) => Hash(((WpfShape)shape).Geometry);

    private static string Hash(Drawing drawing) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Box(drawing.Bounds))))[..16];

    private static string Box(LayoutRect box) => Box(box.ToWpf());

    private static string Box(Rect box) =>
        box.IsEmpty ? "empty" : $"{Number(box.X)},{Number(box.Y)} {Number(box.Width)}x{Number(box.Height)}";

    private static string Point(System.Windows.Point point) => $"{Number(point.X)},{Number(point.Y)} ";

    private static string MatrixText(Matrix m) =>
        $"[{Number(m.M11)} {Number(m.M12)} {Number(m.M21)} {Number(m.M22)} {Number(m.OffsetX)} {Number(m.OffsetY)}]";

    private static string Number(double value) =>
        double.IsFinite(value) ? Math.Round(value, 2).ToString(Length, CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
