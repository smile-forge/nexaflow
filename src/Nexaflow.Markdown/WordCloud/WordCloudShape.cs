namespace Nexaflow.Markdown.WordCloud;

/// <summary>The outline a cloud is packed into.</summary>
public enum WordCloudShape
{
    Circle,
    Cardioid,
    Diamond,
    Square,
    Triangle,
    TriangleForward,
    Pentagon,
    Star,
}

/// <summary>
/// How far out the shape reaches in each direction — the one thing the placement needs to know about it.
///
/// <para>
/// A cloud is packed by trying places around a spiral of ever-growing rings, and the shape is how far the
/// ring reaches at each angle, as a share of its radius. A circle reaches the same distance everywhere and
/// is the ring itself; every other shape pulls the ring in where its outline is nearer the middle, so the
/// words run out at the outline rather than at a circle round it. That is the whole of it: there is no
/// polygon anywhere, and nothing is clipped.
/// </para>
/// <para>
/// The formulae are <c>wordcloud2.js</c>'s, so a cloud written for that library packs into the same outline
/// here. Each is the polar equation of the shape's edge, normalised so that its longest reach is one.
/// </para>
/// </summary>
public static class WordCloudShapes
{
    private const double Turn = System.Math.PI * 2;

    /// <summary>The shape that name stands for, or null where it names none.</summary>
    public static WordCloudShape? Of(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "circle" => WordCloudShape.Circle,
        "cardioid" => WordCloudShape.Cardioid,
        "diamond" => WordCloudShape.Diamond,
        "square" => WordCloudShape.Square,
        "triangle" or "triangle-upright" => WordCloudShape.Triangle,
        "triangle-forward" => WordCloudShape.TriangleForward,
        "pentagon" => WordCloudShape.Pentagon,
        "star" => WordCloudShape.Star,
        _ => null,
    };

    /// <summary>The names a <c>shape:</c> line may give, as a diagnostic lists them.</summary>
    public const string Names =
        "circle, cardioid, diamond, square, triangle, triangle-forward, pentagon, star";

    /// <summary>
    /// How far <paramref name="shape"/> reaches at <paramref name="theta"/> radians, as a share of the ring's
    /// radius — one at its furthest, less everywhere the outline comes in.
    /// </summary>
    public static double Reach(WordCloudShape shape, double theta)
    {
        var sin = System.Math.Sin;
        var cos = System.Math.Cos;

        switch (shape)
        {
            case WordCloudShape.Cardioid:
                return 1 - sin(theta);

            case WordCloudShape.Diamond:
            {
                var edge = Wrapped(theta, Turn / 4);
                return 1 / (cos(edge) + sin(edge));
            }

            case WordCloudShape.Square:
                // The nearer of the two pairs of sides, which is what makes the corners reach furthest.
                return System.Math.Min(1 / System.Math.Max(System.Math.Abs(cos(theta)), 1e-9),
                                       1 / System.Math.Max(System.Math.Abs(sin(theta)), 1e-9));

            case WordCloudShape.TriangleForward:
            {
                var edge = Wrapped(theta, Turn / 3);
                return 1 / (cos(edge) + System.Math.Sqrt(3) * sin(edge));
            }

            case WordCloudShape.Triangle:
            {
                var edge = Wrapped(theta + System.Math.PI * 3 / 2, Turn / 3);
                return 1 / (cos(edge) + System.Math.Sqrt(3) * sin(edge));
            }

            case WordCloudShape.Pentagon:
            {
                var edge = Wrapped(theta + 0.955, Turn / 5);
                return 1 / (cos(edge) + 0.726543 * sin(edge));
            }

            case WordCloudShape.Star:
            {
                // Ten sides rather than five, and the odd ones are the same side read backwards — which is
                // what turns the pentagon's formula into the points and notches of a star.
                var point = Wrapped(theta + 0.955, Turn / 10);
                var inward = Wrapped(theta + 0.955, Turn / 5) - Turn / 10 >= 0;
                var edge = inward ? Turn / 10 - point : point;
                return 1 / (cos(edge) + 3.07768 * sin(edge));
            }

            default:
                return 1;
        }
    }

    /// <summary>
    /// <paramref name="theta"/> brought inside one repeat of the outline, never negative — C#'s remainder
    /// keeps the sign of what it divides, and a negative angle here would reach the wrong way.
    /// </summary>
    private static double Wrapped(double theta, double repeat)
    {
        var at = theta % repeat;
        return at < 0 ? at + repeat : at;
    }
}
