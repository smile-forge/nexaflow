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

    public static LayoutPoint operator *(LayoutPoint point, LayoutMatrix matrix) => matrix.Transform(point);
}

/// <summary>
/// How big something is, in a layout's own units, with WPF's meaning: never less than nothing across or down — asking for that is a
/// mistake, and says so — and empty as no size at all, which is what a rectangle that is nowhere is the size of.
/// </summary>
public readonly record struct LayoutSize
{
    public LayoutSize(double width, double height)
    {
        if (width < 0 || height < 0) throw new ArgumentException("A size is never less than nothing across or down.");

        Width = width;
        Height = height;
    }

    private LayoutSize(double width, double height, bool asIs)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    /// <summary>No size at all.</summary>
    public static LayoutSize Empty { get; } = new(double.NegativeInfinity, double.NegativeInfinity, asIs: true);

    public bool IsEmpty => Width < 0;
}

/// <summary>How far one place is from another, in a layout's own units.</summary>
public readonly record struct LayoutVector(double X, double Y)
{
    public static LayoutVector operator +(LayoutVector a, LayoutVector b) => new(a.X + b.X, a.Y + b.Y);

    public static LayoutVector operator -(LayoutVector a, LayoutVector b) => new(a.X - b.X, a.Y - b.Y);

    public static LayoutVector operator -(LayoutVector vector) => new(-vector.X, -vector.Y);

    public static LayoutVector operator *(LayoutVector vector, double scalar) => new(vector.X * scalar, vector.Y * scalar);

    public static LayoutVector operator *(double scalar, LayoutVector vector) => new(vector.X * scalar, vector.Y * scalar);

    /// <summary>Divided as WPF's vector is, by multiplying by the reciprocal, so the two agree to the last bit.</summary>
    public static LayoutVector operator /(LayoutVector vector, double scalar) => vector * (1.0 / scalar);

    public static LayoutPoint operator +(LayoutVector vector, LayoutPoint point) => new(point.X + vector.X, point.Y + vector.Y);

    /// <summary>The dot product, as WPF's vector times a vector is.</summary>
    public static double operator *(LayoutVector a, LayoutVector b) => (a.X * b.X) + (a.Y * b.Y);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public double LengthSquared => (X * X) + (Y * Y);

    /// <summary>The same way, a length of one — scaled down by its larger part first, as WPF's is, so the two agree to the last bit.</summary>
    public LayoutVector Normalized()
    {
        var scaled = this / Math.Max(Math.Abs(X), Math.Abs(Y));
        return scaled / scaled.Length;
    }

    public static double CrossProduct(LayoutVector a, LayoutVector b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>How far <paramref name="a"/> turns to reach <paramref name="b"/>, in degrees, as WPF's says it.</summary>
    public static double AngleBetween(LayoutVector a, LayoutVector b)
    {
        var sin = (a.X * b.Y) - (b.X * a.Y);
        var cos = (a.X * b.X) + (a.Y * b.Y);

        return Math.Atan2(sin, cos) * (180 / Math.PI);
    }
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

    /// <summary>A rectangle of <paramref name="size"/> with its top left at <paramref name="at"/> — empty where the size is.</summary>
    public LayoutRect(LayoutPoint at, LayoutSize size)
    {
        if (size.IsEmpty)
        {
            this = Empty;
            return;
        }

        X = at.X;
        Y = at.Y;
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>A rectangle of <paramref name="size"/> at the origin — empty where the size is.</summary>
    public LayoutRect(LayoutSize size) : this(default, size)
    {
    }

    /// <summary>The rectangle from <paramref name="at"/> to where <paramref name="reach"/> takes it.</summary>
    public LayoutRect(LayoutPoint at, LayoutVector reach) : this(at, at + reach)
    {
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

    public LayoutPoint TopRight => new(Right, Top);

    public LayoutPoint BottomLeft => new(Left, Bottom);

    public LayoutPoint BottomRight => new(Right, Bottom);

    public LayoutPoint Location => new(X, Y);

    public LayoutSize Size => IsEmpty ? LayoutSize.Empty : new LayoutSize(Width, Height);

    /// <summary>The rectangle round <paramref name="rect"/> and <paramref name="point"/>.</summary>
    public static LayoutRect Union(LayoutRect rect, LayoutPoint point) => Union(rect, new LayoutRect(point, point));

    /// <summary>Where the two overlap — empty where they do not.</summary>
    public static LayoutRect Intersect(LayoutRect a, LayoutRect b)
    {
        if (!a.IntersectsWith(b)) return Empty;

        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);

        return new LayoutRect(left, top, Math.Max(Math.Min(a.Right, b.Right) - left, 0), Math.Max(Math.Min(a.Bottom, b.Bottom) - top, 0));
    }

    public static LayoutRect Offset(LayoutRect rect, double x, double y) => Offset(rect, new LayoutVector(x, y));

    public static LayoutRect Inflate(LayoutRect rect, LayoutSize size) => Inflate(rect, size.Width, size.Height);

    /// <summary>Whether all of <paramref name="rect"/> is inside this one.</summary>
    public bool Contains(LayoutRect rect) =>
        !IsEmpty && !rect.IsEmpty && X <= rect.X && Y <= rect.Y && X + Width >= rect.X + rect.Width && Y + Height >= rect.Y + rect.Height;

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

    /// <summary>
    /// The box round where the rectangle lands under <paramref name="matrix"/>. A matrix that only scales and moves takes the rectangle's
    /// own corner and size, as WPF does, and any other its four corners — so the two agree to the last bit.
    /// </summary>
    public static LayoutRect Transform(LayoutRect rect, LayoutMatrix matrix)
    {
        if (rect.IsEmpty) return rect;
        if (matrix.IsIdentity) return rect;

        if (matrix is { M12: 0, M21: 0 })
        {
            var (x, y, width, height) = (rect.X, rect.Y, rect.Width, rect.Height);

            if (matrix.M11 != 1 || matrix.M22 != 1)
            {
                (x, y, width, height) = (x * matrix.M11, y * matrix.M22, width * matrix.M11, height * matrix.M22);
                if (width < 0) (x, width) = (x + width, -width);
                if (height < 0) (y, height) = (y + height, -height);
            }

            return new LayoutRect(x + matrix.OffsetX, y + matrix.OffsetY, width, height);
        }

        var a = matrix.Transform(rect.TopLeft);
        var b = matrix.Transform(rect.TopRight);
        var c = matrix.Transform(rect.BottomRight);
        var d = matrix.Transform(rect.BottomLeft);

        var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));

        return new LayoutRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The rectangle grown by <paramref name="x"/> at each side and <paramref name="y"/> at top and bottom — empty where that shrinks
    /// it past nothing. An empty one stays empty, where WPF's throws.
    /// </summary>
    public static LayoutRect Inflate(LayoutRect rect, double x, double y)
    {
        if (rect.IsEmpty) return rect;

        // Each side added in turn, as WPF grows its own, so the two agree to the last bit.
        var width = rect.Width + x + x;
        var height = rect.Height + y + y;

        return width >= 0 && height >= 0 ? new LayoutRect(rect.X - x, rect.Y - y, width, height) : Empty;
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

    /// <summary>
    /// The transform that undoes this one. Only asked of one that <see cref="HasInverse"/>. A matrix that only scales, moves or both is
    /// undone by its own reciprocals, as WPF undoes one, and any other through its determinant — so the two agree to the last bit.
    /// </summary>
    public LayoutMatrix Inverted()
    {
        if (M12 == 0 && M21 == 0)
        {
            if (M11 == 1 && M22 == 1) return new LayoutMatrix(1, 0, 0, 1, -OffsetX, -OffsetY);

            var (across, down) = (1.0 / M11, 1.0 / M22);
            return new LayoutMatrix(across, 0, 0, down, -OffsetX * across, -OffsetY * down);
        }

        var inverse = 1.0 / Determinant;

        return new LayoutMatrix(M22 * inverse, -M12 * inverse, -M21 * inverse, M11 * inverse,
                                ((M21 * OffsetY) - (OffsetX * M22)) * inverse, ((OffsetX * M12) - (M11 * OffsetY)) * inverse);
    }
}