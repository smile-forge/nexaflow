using System.Runtime.CompilerServices;
using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// An icon a pack draws as outlines (<see cref="VectorIcon"/>), made at a size: as tall as the size, as wide as the icon's box is
/// for that, each layer an outline filled and stroked as the pack paints it — in the colour the diagram draws its icons in,
/// wherever the pack says <c>currentColor</c>. It is set as one piece standing for what names the icon, which a press means as
/// that and which holds no caret (<see cref="DiagramWords"/>, made by <see cref="DiagramText.Icon"/>).
/// <para>
/// One icon at one size in one colour is made once on each thread and kept for as long as its pack keeps the icon, so a diagram
/// laid out again on every keystroke draws the outlines it made the first time.
/// </para>
/// </summary>
internal sealed class DrawnIcon
{
    [ThreadStatic]
    private static ConditionalWeakTable<VectorIcon, Dictionary<(ILayoutMedium Medium, double Size, LayoutInk Current), DrawnIcon>>? made;

    private readonly IReadOnlyList<GeometryMark> _marks;

    private DrawnIcon(IReadOnlyList<GeometryMark> marks, double width, double height) => (_marks, Width, Height) = (marks, width, height);

    public double Width { get; }

    public double Height { get; }

    /// <summary><paramref name="icon"/>, <paramref name="size"/> tall, its <c>currentColor</c> in <paramref name="current"/>.</summary>
    public static DrawnIcon Of(VectorIcon icon, double size, ILayoutMedium medium, LayoutInk current)
    {
        var sizes = (made ??= []).GetValue(icon, static _ => []);
        if (sizes.TryGetValue((medium, size, current), out var known)) return known;

        return sizes[(medium, size, current)] = Made(icon, size, medium, current);
    }

    /// <summary>Sets it with its top left at <paramref name="at"/>, as a piece of <paramref name="kind"/> standing for <paramref name="part"/>.</summary>
    public void Set(LayoutBuilder build, LayoutPoint at, ISourcePart? part, string kind)
    {
        build.Open(kind, part, at, LayoutStops.None);
        foreach (var mark in _marks) build.Draw(mark);

        // The whole of its box is the icon, where it draws and where it does not, so a press anywhere on it means it.
        build.Covers(new LayoutRect(0, 0, Width, Height));
        build.Close();
    }

    private static DrawnIcon Made(VectorIcon icon, double size, ILayoutMedium medium, LayoutInk current)
    {
        var scale = icon.Height > 0 ? size / icon.Height : 0;
        var place = LayoutMatrix.Translation(-icon.Left, -icon.Top) * LayoutMatrix.Scaling(scale, scale);
        var marks = new List<GeometryMark>();

        foreach (var layer in icon.Layers)
        {
            // A clip path with nothing in it lets nothing show.
            if (layer.Clips.Any(clip => clip.Shapes.Count == 0)) continue;

            var shape = Outline(layer.Shape, place, medium);
            var clip = Clip(layer.Clips, place, medium);
            var width = layer.StrokeWidth * scale;

            var filled = layer.Fill is { } fill
                ? new GeometryMark(clip is null ? shape : medium.Combine(shape, clip, ShapeCombine.Intersect), Ink(fill, place, medium, current), null)
                : null;

            // A stroke clipped is the outline its pen inks, clipped and filled; one not clipped is drawn as a stroke, its joins and ends as written.
            var stroked = layer.Stroke is not { } pen ? null
                : clip is null ? new GeometryMark(shape, null, medium.Stroke(Ink(pen, place, medium, current), width, ends: Ends(layer)))
                : new GeometryMark(medium.Combine(medium.Widen(shape, width), clip, ShapeCombine.Intersect), Ink(pen, place, medium, current), null);

            foreach (var mark in layer.StrokeFirst ? new[] { stroked, filled } : [filled, stroked])
                if (mark is not null) marks.Add(mark);
        }

        return new DrawnIcon(marks, icon.Width * scale, size);
    }

    private static LayoutShape Outline(VectorShape shape, LayoutMatrix place, ILayoutMedium medium)
    {
        var outline = medium.Outline(shape.EvenOdd);

        foreach (var figure in shape.Figures)
        {
            outline.Begin(Placed(figure.Start), filled: true, closed: figure.Closed);

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case VectorCubic cubic:
                        outline.BezierTo(Placed(cubic.First), Placed(cubic.Second), Placed(cubic.To));
                        break;
                    case VectorQuadratic quadratic:
                        outline.QuadraticTo(Placed(quadratic.Control), Placed(quadratic.To));
                        break;
                    default:
                        outline.LineTo(Placed(segment.To));
                        break;
                }
            }
        }

        return outline.Close();

        LayoutPoint Placed(VectorPoint point) => place.Transform(new LayoutPoint(point.X, point.Y));
    }

    /// <summary>Where a layer may show: inside every one of its clips, each the outlines of all its shapes together — null where it has none.</summary>
    private static LayoutShape? Clip(IReadOnlyList<VectorClip> clips, LayoutMatrix place, ILayoutMedium medium)
    {
        LayoutShape? inside = null;

        foreach (var clip in clips)
        {
            var any = clip.Shapes.Select(shape => Outline(shape, place, medium)).Aggregate((a, b) => medium.Combine(a, b, ShapeCombine.Union));
            inside = inside is null ? any : medium.Combine(inside, any, ShapeCombine.Intersect);
        }

        return inside is null ? null : medium.Resolved(inside);
    }

    private static LayoutInk Ink(VectorPaint paint, LayoutMatrix place, ILayoutMedium medium, LayoutInk current) => paint switch
    {
        VectorSolid solid => medium.Ink(Colour(solid.Color)),
        VectorLinear linear => medium.LinearGradient(Stops(linear), Point(linear.From), Point(linear.To), Matrix(linear.Transform) * place),
        VectorRadial radial => medium.RadialGradient(Stops(radial), Point(radial.Centre), radial.Radius, radial.Radius, Point(radial.Focus),
                                                     Matrix(radial.Transform) * place),
        VectorCurrent painted => medium.Faded(current, painted.Opacity),
        _ => current,
    };

    private static List<(LayoutColor Color, double At)> Stops(VectorGradient gradient) => [.. gradient.Stops.Select(stop => (Colour(stop.Color), stop.At))];

    private static LayoutColor Colour(VectorColor colour) => new(colour.A, colour.R, colour.G, colour.B);

    private static LayoutPoint Point(VectorPoint point) => new(point.X, point.Y);

    private static LayoutMatrix Matrix(VectorMatrix matrix) => new(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);

    private static StrokeEnds Ends(VectorLayer layer)
    {
        var cap = layer.Cap switch { VectorCap.Round => StrokeCap.Round, VectorCap.Square => StrokeCap.Square, _ => StrokeCap.Flat };
        var join = layer.Join switch { VectorJoin.Round => StrokeJoin.Round, VectorJoin.Bevel => StrokeJoin.Bevel, _ => StrokeJoin.Miter };

        return new StrokeEnds(cap, cap, StrokeCap.Square, join, layer.MiterLimit);
    }
}
