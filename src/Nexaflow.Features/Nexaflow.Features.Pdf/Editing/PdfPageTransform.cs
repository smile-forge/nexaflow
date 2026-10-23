using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// How a page, as it is shown, is put on the sheet it is printed on: moved, made larger or smaller, turned by quarter
/// turns — from the page's own space to the sheet's, both in points from their top left, y down. Only a transform that
/// keeps the page's edges square to the sheet's is a transform of a page, so a link's or a note's box stays a box, and
/// none mirrors it.
/// <para>
/// Its values are rounded to a millionth as it is made, so two transforms that place a page the same by different
/// edits are equal — the one sheet, laid once.
/// </para>
/// </summary>
internal readonly struct PdfPageTransform : IEquatable<PdfPageTransform>
{
    private const int Digits = 6;

    private PdfPageTransform(Matrix matrix)
    {
        Matrix = matrix;
    }

    /// <summary>The page left as it is. The default transform is this one.</summary>
    public static PdfPageTransform Identity => default;

    /// <summary>From the page's space to the sheet's, as a point is carried: <c>point × Matrix</c>.</summary>
    public Matrix Matrix { get; }

    public bool IsIdentity => Matrix.IsIdentity;

    /// <summary><paramref name="matrix"/> as a transform of a page.</summary>
    /// <exception cref="ArgumentException">It skews the page, turns it other than by quarter turns, or mirrors it.</exception>
    public static PdfPageTransform Of(Matrix matrix)
    {
        var rounded = new Matrix(
            Round(matrix.M11), Round(matrix.M12), Round(matrix.M21), Round(matrix.M22),
            Round(matrix.OffsetX), Round(matrix.OffsetY));
        var square = (rounded.M12 == 0 && rounded.M21 == 0) || (rounded.M11 == 0 && rounded.M22 == 0);
        if (!square || rounded.Determinant <= 0)
            throw new ArgumentException("A page is moved, scaled and turned by quarter turns on its sheet, never skewed or mirrored.", nameof(matrix));
        return new PdfPageTransform(rounded);
    }

    public static PdfPageTransform Translate(double x, double y) => Of(new Matrix(1, 0, 0, 1, x, y));

    /// <exception cref="ArgumentOutOfRangeException">A scale is not above nothing.</exception>
    public static PdfPageTransform Scaled(double across, double down) =>
        across > 0 && down > 0
            ? Of(new Matrix(across, 0, 0, down, 0, 0))
            : throw new ArgumentOutOfRangeException(nameof(across), "A page is scaled by more than nothing.");

    /// <summary>
    /// A page shown <paramref name="shown"/> large turned <paramref name="quarters"/> quarter turns clockwise, its top
    /// left where the turned page's top left is.
    /// </summary>
    public static PdfPageTransform Turned(int quarters, Size shown) => (((quarters % 4) + 4) % 4) switch
    {
        1 => Of(new Matrix(0, 1, -1, 0, shown.Height, 0)),
        2 => Of(new Matrix(-1, 0, 0, -1, shown.Width, shown.Height)),
        3 => Of(new Matrix(0, -1, 1, 0, 0, shown.Width)),
        _ => Identity,
    };

    /// <summary>This transform, then <paramref name="next"/>.</summary>
    public PdfPageTransform Then(PdfPageTransform next) => Of(Matrix * next.Matrix);

    /// <summary>From the sheet's space back to the page's.</summary>
    public PdfPageTransform Inverse
    {
        get
        {
            var inverse = Matrix;
            inverse.Invert();
            return Of(inverse);
        }
    }

    public Point Apply(Point point) => Matrix.Transform(point);

    /// <summary>A box of the page on the sheet — still a box, since a transform of a page keeps its edges square.</summary>
    public Rect Apply(Rect rect) => Rect.Transform(rect, Matrix);

    /// <summary>Whether this transform and <paramref name="other"/> place a page the same, to <paramref name="points"/>.</summary>
    public bool IsCloseTo(PdfPageTransform other, double points)
    {
        const double scale = 1e-6;
        return Math.Abs(Matrix.M11 - other.Matrix.M11) <= scale && Math.Abs(Matrix.M12 - other.Matrix.M12) <= scale
               && Math.Abs(Matrix.M21 - other.Matrix.M21) <= scale && Math.Abs(Matrix.M22 - other.Matrix.M22) <= scale
               && Math.Abs(Matrix.OffsetX - other.Matrix.OffsetX) <= points
               && Math.Abs(Matrix.OffsetY - other.Matrix.OffsetY) <= points;
    }

    public bool Equals(PdfPageTransform other) => Matrix.Equals(other.Matrix);

    public override bool Equals(object? obj) => obj is PdfPageTransform other && Equals(other);

    public override int GetHashCode() => Matrix.GetHashCode();

    public static bool operator ==(PdfPageTransform left, PdfPageTransform right) => left.Equals(right);

    public static bool operator !=(PdfPageTransform left, PdfPageTransform right) => !left.Equals(right);

    // Adding nothing turns a negative zero into zero, which hashes as zero does.
    private static double Round(double value) => Math.Round(value, Digits) + 0.0;
}
