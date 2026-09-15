namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>
/// A point or a direction on the page a structure is drawn on, in bond lengths, y up. The drawing's own geometry
/// primitive, because this assembly has none and a depiction has to be worked out — and tested — without a desktop.
/// </summary>
public readonly record struct Vec(double X, double Y)
{
    public static readonly Vec Zero = new(0, 0);

    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public static Vec operator *(double k, Vec a) => new(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double Angle => Math.Atan2(Y, X);

    /// <summary>The same direction, one long — or nothing, for nothing.</summary>
    public Vec Unit => Length is var l and > 1e-12 ? this / l : Zero;

    /// <summary>A quarter turn anticlockwise.</summary>
    public Vec Perpendicular => new(-Y, X);

    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y;

    /// <summary>The z of the cross product: positive when <paramref name="b"/> is anticlockwise of <paramref name="a"/>.</summary>
    public static double Cross(Vec a, Vec b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vec a, Vec b) => (a - b).Length;

    public static Vec FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    /// <summary>This point turned by <paramref name="radians"/> about the origin.</summary>
    public Vec Rotated(double radians)
    {
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        return new Vec(X * cos - Y * sin, X * sin + Y * cos);
    }

    /// <summary>This point mirrored in the line through <paramref name="through"/> along <paramref name="along"/>.</summary>
    public Vec Reflected(Vec through, Vec along)
    {
        var axis = along.Unit;
        var offset = this - through;
        var onto = axis * Dot(offset, axis);
        return through + onto * 2 - offset;
    }

    /// <summary>Whether two segments cross, touching at an end not counting.</summary>
    public static bool Crosses(Vec p, Vec q, Vec r, Vec s)
    {
        const double eps = 1e-9;
        var d1 = Cross(s - r, p - r);
        var d2 = Cross(s - r, q - r);
        var d3 = Cross(q - p, r - p);
        var d4 = Cross(q - p, s - p);
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps))
            && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }
}
