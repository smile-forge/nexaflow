namespace Nexaflow.Markdown.Layout;

/// <summary>
/// A point in a layout's own units. The geometry a layout tree is measured in, owned here so that laying content out needs no
/// desktop: the same two doubles WPF's point is, with the same arithmetic, so a renderer turns one into its own by copying.
/// </summary>
public readonly record struct LayoutPoint(double X, double Y)
{
    public static LayoutPoint operator +(LayoutPoint point, LayoutVector by) => new(point.X + by.X, point.Y + by.Y);

    public static LayoutPoint operator -(LayoutPoint point, LayoutVector by) => new(point.X - by.X, point.Y - by.Y);

    public static LayoutVector operator -(LayoutPoint to, LayoutPoint from) => new(to.X - from.X, to.Y - from.Y);

    public static explicit operator LayoutVector(LayoutPoint point) => new(point.X, point.Y);
}

/// <summary>How far one place is from another, in a layout's own units.</summary>
public readonly record struct LayoutVector(double X, double Y)
{
    public static LayoutVector operator +(LayoutVector a, LayoutVector b) => new(a.X + b.X, a.Y + b.Y);

    public static LayoutVector operator -(LayoutVector a, LayoutVector b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>
/// A rectangle in a layout's own units, with WPF's meaning of empty — no place at all, which is not a rectangle of no size
/// at a place — because a piece that never said where it was and one that is at a point are different answers.
/// </summary>
public readonly record struct LayoutRect
{
    public LayoutRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public LayoutRect(LayoutPoint a, LayoutPoint b)
    {
        X = Math.Min(a.X, b.X);
        Y = Math.Min(a.Y, b.Y);
        Width = Math.Max(Math.Max(a.X, b.X) - X, 0);
        Height = Math.Max(Math.Max(a.Y, b.Y) - Y, 0);
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>No place at all.</summary>
    public static LayoutRect Empty { get; } = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => Width < 0;

    public double Left => X;

    public double Top => Y;

    public double Right => IsEmpty ? double.NegativeInfinity : X + Width;

    public double Bottom => IsEmpty ? double.NegativeInfinity : Y + Height;

    public LayoutPoint TopLeft => new(X, Y);

    /// <summary>The rectangle round both. Either being empty gives the other.</summary>
    public static LayoutRect Union(LayoutRect a, LayoutRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;

        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);

        return new LayoutRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    /// <summary>The same rectangle moved along. An empty one stays empty, where WPF's throws.</summary>
    public static LayoutRect Offset(LayoutRect rect, LayoutVector by) =>
        rect.IsEmpty ? rect : new LayoutRect(rect.X + by.X, rect.Y + by.Y, rect.Width, rect.Height);

    /// <summary>The box round where the rectangle lands under <paramref name="matrix"/>.</summary>
    public static LayoutRect Transform(LayoutRect rect, LayoutMatrix matrix)
    {
        if (rect.IsEmpty) return rect;
        if (matrix.IsIdentity) return rect;

        var a = matrix.Transform(new LayoutPoint(rect.X, rect.Y));
        var b = matrix.Transform(new LayoutPoint(rect.Right, rect.Y));
        var c = matrix.Transform(new LayoutPoint(rect.X, rect.Bottom));
        var d = matrix.Transform(new LayoutPoint(rect.Right, rect.Bottom));

        var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));

        return new LayoutRect(left, top, right - left, bottom - top);
    }

    public bool Contains(LayoutPoint point) =>
        !IsEmpty && point.X >= X && point.X - Width <= X && point.Y >= Y && point.Y - Height <= Y;

    public bool IntersectsWith(LayoutRect other) =>
        !IsEmpty && !other.IsEmpty && other.Left <= Right && other.Right >= Left && other.Top <= Bottom && other.Bottom >= Top;
}

/// <summary>
/// An affine transform in a layout's own units, in WPF's row-vector convention, so <c>a * b</c> applies <c>a</c> first — the
/// order a painter pushes them in.
/// </summary>
public readonly record struct LayoutMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static LayoutMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

    public double Determinant => (M11 * M22) - (M12 * M21);

    public bool HasInverse => Determinant != 0;

    public static LayoutMatrix Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

    public static LayoutMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>A turn of <paramref name="degrees"/> clockwise on the page, about <paramref name="centreX"/>, <paramref name="centreY"/>.</summary>
    public static LayoutMatrix Rotation(double degrees, double centreX = 0, double centreY = 0)
    {
        var radians = degrees % 360 * (Math.PI / 180);
        var (sin, cos) = Math.SinCos(radians);

        return new LayoutMatrix(cos, sin, -sin, cos,
                                (centreX * (1 - cos)) + (centreY * sin),
                                (centreY * (1 - cos)) - (centreX * sin));
    }

    public static LayoutMatrix operator *(LayoutMatrix a, LayoutMatrix b) =>
        new((a.M11 * b.M11) + (a.M12 * b.M21), (a.M11 * b.M12) + (a.M12 * b.M22),
            (a.M21 * b.M11) + (a.M22 * b.M21), (a.M21 * b.M12) + (a.M22 * b.M22),
            (a.OffsetX * b.M11) + (a.OffsetY * b.M21) + b.OffsetX, (a.OffsetX * b.M12) + (a.OffsetY * b.M22) + b.OffsetY);

    public LayoutPoint Transform(LayoutPoint point) =>
        new((point.X * M11) + (point.Y * M21) + OffsetX, (point.X * M12) + (point.Y * M22) + OffsetY);

    public LayoutVector Transform(LayoutVector vector) =>
        new((vector.X * M11) + (vector.Y * M21), (vector.X * M12) + (vector.Y * M22));

    /// <summary>The transform that undoes this one. Only asked of one that <see cref="HasInverse"/>.</summary>
    public LayoutMatrix Inverted()
    {
        var determinant = Determinant;

        return new LayoutMatrix(M22 / determinant, -M12 / determinant, -M21 / determinant, M11 / determinant,
                                ((M21 * OffsetY) - (OffsetX * M22)) / determinant, ((OffsetX * M12) - (M11 * OffsetY)) / determinant);
    }
}
