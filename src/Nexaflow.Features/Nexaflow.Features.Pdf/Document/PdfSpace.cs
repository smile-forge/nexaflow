using System;
using System.Windows;
using System.Windows.Media;
using UglyToad.PdfPig.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// The arithmetic of page space — points, origin top left, y down — and the way into it from a PDF's own y-up
/// spaces.
/// </summary>
internal static class PdfSpace
{
    /// <summary>The bounds of something that paints all it is allowed to: a shading with no box of its own.</summary>
    public static Rect Everywhere { get; } = new(-1e7, -1e7, 2e7, 2e7);

    /// <summary>The square an image is drawn into, before its placement.</summary>
    public static PdfShape UnitSquare { get; } = Square();

    /// <summary>
    /// From a PDF's y-up space whose visible area is <paramref name="height"/> points tall, to page space: the
    /// same points with the y axis turned over.
    /// </summary>
    public static Matrix Flip(double height) => new(1, 0, 0, -1, 0, height);

    /// <summary>A PdfPig matrix as a WPF one. Both are row-vector affine matrices, element for element.</summary>
    public static Matrix ToMatrix(in TransformationMatrix m) => new(m.A, m.B, m.C, m.D, m.E, m.F);

    /// <summary>
    /// The most <paramref name="m"/> lengthens anything — the larger of its two axis scales. A stroke drawn through
    /// it is at most this much wider.
    /// </summary>
    public static double LargestScale(Matrix m)
    {
        var x = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        var y = Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22);
        return Math.Max(x, y);
    }

    /// <summary>The average of <paramref name="m"/>'s two axis scales — what a uniform length becomes through it.</summary>
    public static double MeanScale(Matrix m) => Math.Sqrt(Math.Abs(m.Determinant));

    /// <summary>Whether <paramref name="m"/> scales every direction alike, so a stroke keeps its shape through it.</summary>
    public static bool IsUniform(Matrix m)
    {
        var x = m.M11 * m.M11 + m.M12 * m.M12;
        var y = m.M21 * m.M21 + m.M22 * m.M22;
        var skew = m.M11 * m.M21 + m.M12 * m.M22;
        var size = Math.Max(x, y);
        return size == 0 || (Math.Abs(x - y) <= size * 1e-6 && Math.Abs(skew) <= size * 1e-6);
    }

    /// <summary>The bounds of <paramref name="box"/> once <paramref name="through"/> has moved its corners.</summary>
    public static Rect Transform(Rect box, Matrix through)
    {
        if (box.IsEmpty) return box;
        var corners = new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight };
        through.Transform(corners);
        var bounds = new Rect(corners[0], corners[1]);
        bounds.Union(corners[2]);
        bounds.Union(corners[3]);
        return bounds;
    }

    private static PdfShape Square()
    {
        var writer = new PdfShape.Writer();
        writer.Rectangle(0, 0, 1, 1);
        return writer.Take();
    }
}
