namespace Nexaflow.Icons;

/// <summary>
/// An icon drawn as outlines rather than set as a glyph — a pack's icon, as its SVG says (<see cref="IconBody"/>): what is
/// filled and stroked, in the order it is drawn, inside the box from <see cref="Left"/>, <see cref="Top"/> across
/// <see cref="Width"/> and down <see cref="Height"/>. Everything is already in the icon's own coordinates — every transform the
/// SVG wrote is carried out, and every arc is drawn as the curves that make it — so whatever lays it out only scales it into
/// place. Nothing here draws.
/// </summary>
public sealed record VectorIcon(double Left, double Top, double Width, double Height, IReadOnlyList<VectorLayer> Layers);

/// <summary>
/// One outline drawn: filled with <see cref="Fill"/> and stroked with <see cref="Stroke"/> — either or both, the stroke first where
/// <see cref="StrokeFirst"/> says — and showing only inside every one of <see cref="Clips"/>.
/// </summary>
/// <param name="StrokeWidth">
/// How wide the stroke is in the icon's coordinates: as the SVG wrote it, scaled as the transforms carrying it scale an area —
/// which is how they scale a line, for every transform that keeps a circle round.
/// </param>
public sealed record VectorLayer(
    VectorShape Shape,
    VectorPaint? Fill,
    VectorPaint? Stroke,
    double StrokeWidth,
    VectorJoin Join,
    double MiterLimit,
    VectorCap Cap,
    bool StrokeFirst,
    IReadOnlyList<VectorClip> Clips);

/// <summary>An outline: its figures, and whether a place inside them an even number of times is outside — SVG's <c>evenodd</c> rather than <c>nonzero</c>.</summary>
public sealed record VectorShape(IReadOnlyList<VectorFigure> Figures, bool EvenOdd);

/// <summary>Where a layer may show: inside any of these outlines — a clip path's, each of its shapes by its own rule.</summary>
public sealed record VectorClip(IReadOnlyList<VectorShape> Shapes);

/// <summary>One run of an outline: from <see cref="Start"/> through each segment in turn, closed back to the start where <see cref="Closed"/>.</summary>
public sealed record VectorFigure(VectorPoint Start, IReadOnlyList<VectorSegment> Segments, bool Closed);

/// <summary>A step of a figure, from wherever the step before it ended to <see cref="To"/>.</summary>
public abstract record VectorSegment(VectorPoint To);

public sealed record VectorLine(VectorPoint To) : VectorSegment(To);

public sealed record VectorCubic(VectorPoint First, VectorPoint Second, VectorPoint To) : VectorSegment(To);

public sealed record VectorQuadratic(VectorPoint Control, VectorPoint To) : VectorSegment(To);

public readonly record struct VectorPoint(double X, double Y);

public readonly record struct VectorColor(byte A, byte R, byte G, byte B);

/// <summary>A gradient's colour at <see cref="At"/>, nought to one along it.</summary>
public readonly record struct VectorStop(VectorColor Color, double At);

public enum VectorJoin { Miter, Round, Bevel }

public enum VectorCap { Butt, Round, Square }

/// <summary>What an outline is filled or stroked with.</summary>
public abstract record VectorPaint;

public sealed record VectorSolid(VectorColor Color) : VectorPaint;

/// <summary>The colour whatever draws the icon draws it in — SVG's <c>currentColor</c> — at <see cref="Opacity"/>.</summary>
public sealed record VectorCurrent(double Opacity) : VectorPaint;

/// <summary>A gradient through <see cref="Stops"/>, its own geometry carried into the icon's coordinates by <see cref="Transform"/>.</summary>
public abstract record VectorGradient(IReadOnlyList<VectorStop> Stops, VectorMatrix Transform) : VectorPaint;

/// <summary>A gradient running from <see cref="From"/> to <see cref="To"/>.</summary>
public sealed record VectorLinear(IReadOnlyList<VectorStop> Stops, VectorPoint From, VectorPoint To, VectorMatrix Transform)
    : VectorGradient(Stops, Transform);

/// <summary>A gradient running out from <see cref="Focus"/> to the circle of <see cref="Radius"/> about <see cref="Centre"/>.</summary>
public sealed record VectorRadial(IReadOnlyList<VectorStop> Stops, VectorPoint Centre, double Radius, VectorPoint Focus, VectorMatrix Transform)
    : VectorGradient(Stops, Transform);

/// <summary>
/// An affine transform as SVG writes one, <c>matrix(a b c d e f)</c>: a point is carried to
/// (<see cref="A"/>x + <see cref="C"/>y + <see cref="E"/>, <see cref="B"/>x + <see cref="D"/>y + <see cref="F"/>).
/// </summary>
public readonly record struct VectorMatrix(double A, double B, double C, double D, double E, double F)
{
    public static VectorMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public static VectorMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static VectorMatrix Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>A turn of <paramref name="degrees"/> clockwise on the page, about <paramref name="x"/>, <paramref name="y"/>.</summary>
    public static VectorMatrix Rotation(double degrees, double x = 0, double y = 0)
    {
        var (sin, cos) = Math.SinCos(degrees * (Math.PI / 180));
        return Translation(x, y) * new VectorMatrix(cos, sin, -sin, cos, 0, 0) * Translation(-x, -y);
    }

    /// <summary>How much it scales an area — what a line drawn through it is widened by, where it keeps a circle round.</summary>
    public double Scale => Math.Sqrt(Math.Abs((A * D) - (B * C)));

    /// <summary><paramref name="inner"/> done first, then <paramref name="outer"/> — as SVG nests them, the outer one written first.</summary>
    public static VectorMatrix operator *(VectorMatrix outer, VectorMatrix inner) =>
        new((outer.A * inner.A) + (outer.C * inner.B), (outer.B * inner.A) + (outer.D * inner.B),
            (outer.A * inner.C) + (outer.C * inner.D), (outer.B * inner.C) + (outer.D * inner.D),
            (outer.A * inner.E) + (outer.C * inner.F) + outer.E, (outer.B * inner.E) + (outer.D * inner.F) + outer.F);

    public VectorPoint Transform(VectorPoint point) => new((A * point.X) + (C * point.Y) + E, (B * point.X) + (D * point.Y) + F);
}
