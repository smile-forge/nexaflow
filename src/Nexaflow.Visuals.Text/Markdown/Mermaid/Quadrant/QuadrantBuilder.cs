using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;

/// <summary>The pieces a quadrant chart's layout is made of — its layers, and what is in them.</summary>
public static class QuadrantPiece
{
    /// <summary>The four quadrants, and one — standing for the line writing its caption, where one is written.</summary>
    public const string Quadrants = "Quadrants";
    public const string Quadrant = "Quadrant";

    /// <summary>The line round the chart and the lines between its quadrants.</summary>
    public const string Borders = "Borders";

    /// <summary>The points, and one — standing for the point as written.</summary>
    public const string Points = "Points";
    public const string Point = "Point";

    /// <summary>What is written on the chart: captions, the axes' ends, and the points' names.</summary>
    public const string Words = "Words";
    public const string Caption = "Caption";
    public const string AxisLabel = "AxisLabel";
    public const string Name = "Name";
}

/// <summary>
/// Draws a <c>quadrantChart</c> block: four quadrants each tinted and captioned, the ends of its axes out at their ends — the
/// y-axis's reading up the chart's side — and a dot for each point where it stands, its name beside it.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A quadrant stands for its caption's line, a dot for its
/// point, and the words are the characters written — a caption, an axis's end, a point's name typed into where it is drawn.
/// The first quadrant is top right and the rest go anticlockwise, as Mermaid numbers them; the x-axis's words go over the
/// chart where there are no points, and under it where there are.
/// </para>
/// <para>
/// <strong>Nothing written covers anything else.</strong> Each point's name goes under its dot where that is clear, or
/// wherever round it covers least of the other dots, names and the axes' words; then each caption, written in the axes'
/// colour, goes in the middle of its quadrant where that is clear, or to whichever edge or corner of it is clearest.
/// </para>
/// </summary>
internal sealed class QuadrantBuilder : MermaidBuilder
{
    /// <summary>How big the chart is drawn before anything asks for another size.</summary>
    private const double Side = 380;

    private const double Smallest = 120;
    private const double Gap = 6;
    private const double AxisSize = 12;
    private const double CaptionSize = 13;
    private const double NameSize = 11;
    private const double Radius = 5;

    /// <summary>How solid a quadrant is tinted where no front matter colours it.</summary>
    private const double Tint = 0.16;

    internal QuadrantBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>The chart as its stages left it on the block.</summary>
    private QuadrantBlockNode? Chart => Reading.Root.Node as QuadrantBlockNode;

    /// <summary>A point drawn: the part of the reading it is, what its stages said of it, where it comes among the points, and where it stands.</summary>
    private readonly record struct Dot(ContentPart Part, QuadrantPointNode Said, int Order, Point Centre, double Radius);

    /// <summary>The front matter's <c>quadrantTitleFill</c>, where it writes one.</summary>
    protected override string? TitleColour => Chart?.Config.TitleFill;

    /// <summary>The front matter's <c>titleFontSize</c>, where it writes one.</summary>
    protected override double? TitleTextSize => Chart?.Config.TitleFontSize;

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        if (Chart is not { } chart) return AsWritten(build);
        var config = chart.Config;

        // Each line goes where what it writes is drawn — a line written twice is the last one written — and each point its
        // stages made one of its own.
        ContentPart? across = null, upwards = null;
        var regions = new ContentPart?[4];
        var points = new List<(ContentPart Part, QuadrantPointNode Said)>();

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Node)
            {
                case QuadrantPointNode point:
                    points.Add((part, point));
                    break;

                case { Kind: QuadrantKinds.Axis } when Word(part) is { } word:
                    if (word.Equals(QuadrantGrammar.XAxis, StringComparison.OrdinalIgnoreCase)) across = part;
                    else upwards = part;
                    break;

                case { Kind: QuadrantKinds.Region } when Region(part) is >= 0 and var index:
                    regions[index] = part;
                    break;
            }
        }

        var ends = new[] { Text(across, QuadrantRoles.Low), Text(across, QuadrantRoles.High), Text(upwards, QuadrantRoles.Low), Text(upwards, QuadrantRoles.High) };
        var captions = regions.Select(line => Text(line, QuadrantRoles.Caption)).ToArray();

        // A chart with nothing written in it is the source.
        if (points.Count == 0 && captions.All(caption => caption is null) && ends.All(end => end is null))
            return AsWritten(build);

        var along = ends[..2].Select(end => Words(end, config.XAxisLabelFontSize ?? AxisSize, config.XAxisTextFill, Palette.TextMuted)).ToList();
        var up = ends[2..].Select(end => Words(end, config.YAxisLabelFontSize ?? AxisSize, config.YAxisTextFill, Palette.TextMuted)).ToList();

        // As big as it is asked to be, unless the room less the y-axis's words says smaller — kept its shape either way. Those
        // words read up the chart's side, so they take only their height beside it.
        var beside = up.OfType<DiagramWords>().Select(words => words.Height + Gap).DefaultIfEmpty(0).Max();
        var (wide, tall) = (config.ChartWidth ?? Side, config.ChartHeight ?? Side);
        if (!double.IsInfinity(Space) && wide + beside > Space)
        {
            var scale = Math.Max(Smallest, Space - beside) / wide;
            (wide, tall) = (wide * scale, tall * scale);
        }

        var plot = new Rect(0, 0, wide, tall);
        var words = new List<Placing>();

        var dots = new List<Dot>();
        var names = new List<(DiagramWords Words, Point Centre, double Radius)>();
        for (var order = 0; order < points.Count; order++)
        {
            var (part, point) = points[order];
            if (!point.Placed) continue;

            var centre = new Point(Math.Clamp(point.X!.Value, 0, 1) * wide, (1 - Math.Clamp(point.Y!.Value, 0, 1)) * tall);
            var radius = point.Style.Radius ?? config.PointRadius ?? Radius;
            dots.Add(new Dot(part, point, order, centre, radius));

            if (Words(Text(part, QuadrantRoles.Name), config.PointLabelFontSize ?? NameSize, config.PointTextFill, Palette.Text) is { } name)
                names.Add((name, centre, radius));
        }

        // A dot on the chart's edge hangs over it: the axes' words stand clear of the furthest one does.
        var reached = dots.Select(dot => new Rect(dot.Centre.X - dot.Radius, dot.Centre.Y - dot.Radius, dot.Radius * 2, dot.Radius * 2)).Aggregate(plot, Rect.Union);

        // The x-axis's ends out at its ends, over the chart or under it; the y-axis's reading up its side, left of it or right.
        // An axis with one end written has it along the middle.
        var both = along.All(end => end is not null);
        for (var end = 0; end < 2; end++)
        {
            if (along[end] is not { } x) continue;
            var left = !both ? (wide - x.Width) / 2 : end == 0 ? 0 : wide - x.Width;
            words.Add(new Placing(x, new Point(left, chart.XAxisOnTop ? reached.Top - Gap - x.Height : reached.Bottom + Gap), QuadrantPiece.AxisLabel));
        }

        var upright = up.All(end => end is not null);
        for (var end = 0; end < 2; end++)
        {
            if (up[end] is not { } y) continue;
            var foot = !upright ? (tall + y.Width) / 2 : end == 0 ? tall : y.Width;
            words.Add(new Placing(y, new Point(config.YAxisOnRight ? reached.Right + Gap : reached.Left - Gap - y.Height, foot), QuadrantPiece.AxisLabel, Upright));
        }

        // The points' names settle first, clear of the dots, the axes' words and each other; then each caption takes the place
        // in its quadrant that is clearest of all of them.
        var circles = dots.Select(dot => (dot.Centre, dot.Radius)).ToList();
        words.AddRange(Named(names, circles, words.Select(placing => placing.Box).ToList(), plot));

        var cells = Enumerable.Range(0, 4).Select(index => Cell(plot, index)).ToList();
        for (var index = 0; index < 4; index++)
        {
            if (Words(captions[index], config.QuadrantLabelFontSize ?? CaptionSize, config.QuadrantTextFills[index], Palette.TextMuted) is not { } caption) continue;
            words.Add(new Placing(caption, Captioned(caption, cells[index], circles, words.Select(placing => placing.Box).ToList()), QuadrantPiece.Caption));
        }

        // Words reach past the chart's edges: everything moves over so they are not cut off.
        var room = new DiagramRoom();
        room.Reach(plot);
        foreach (var placing in words) room.Reach(placing.Box);
        foreach (var dot in dots) room.Reach(new Rect(dot.Centre.X - dot.Radius, dot.Centre.Y - dot.Radius, dot.Radius * 2, dot.Radius * 2));
        var shift = room.Shift;

        // What is drawn over the quadrants, which a press there means rather than the quadrant under it.
        var over = new GeometryGroup();
        foreach (var placing in words) over.Children.Add(new RectangleGeometry(Rect.Offset(placing.Box, shift)));
        foreach (var dot in dots) over.Children.Add(new EllipseGeometry(dot.Centre + shift, dot.Radius, dot.Radius));
        over.Freeze();

        Quadrants(build, config, regions, captions, cells, shift, over);
        Borders(build, config, plot, shift);
        Points(build, config, dots, shift);

        build.Open(QuadrantPiece.Words, part: null, stops: Stops.None);
        foreach (var placing in words) placing.Words.Set(build, placing.At + shift, placing.Kind, placing.Degrees);
        build.Close();

        return room.Size;
    }

    /// <summary>How far the y-axis's words are turned: a quarter turn, to read up the chart's side.</summary>
    private const double Upright = -90;

    /// <summary>Words set down at a place, and the box they then take — a quarter turn stands them on their foot.</summary>
    private readonly record struct Placing(DiagramWords Words, Point At, string Kind, double Degrees = 0)
    {
        public Rect Box => Degrees == 0 ? new Rect(At, new Size(Words.Width, Words.Height)) : new Rect(At.X, At.Y - Words.Width, Words.Height, Words.Width);
    }

    /// <summary>
    /// Where each point's name goes: under its dot where that is clear, or else whichever of the places round the dot — over it,
    /// beside it, off a corner — crowds least what is already there. Every name is placed, then each looks again with all the
    /// others down, so a name placed early moves aside for one that had nowhere else to go.
    /// </summary>
    private static IEnumerable<Placing> Named(IReadOnlyList<(DiagramWords Words, Point Centre, double Radius)> names, IReadOnlyList<(Point Centre, double Radius)> dots, IReadOnlyList<Rect> taken, Rect plot)
    {
        var chosen = new Rect?[names.Count];
        for (var pass = 0; pass < Passes; pass++)
        {
            var moved = false;
            for (var index = 0; index < names.Count; index++)
            {
                var (words, centre, radius) = names[index];
                var others = taken.Concat(chosen.Where((box, at) => at != index && box is not null).Select(box => box!.Value)).ToList();
                var best = Around(centre, radius, words.Width, words.Height)
                    .Select((box, rank) => (Box: box, Cost: Crowding(box, dots, others) + Outside(box, plot) + (rank * Preferred)))
                    .MinBy(candidate => candidate.Cost).Box;

                moved |= chosen[index] != best;
                chosen[index] = best;
            }

            if (!moved) break;
        }

        return names.Select((name, index) => new Placing(name.Words, chosen[index]!.Value.TopLeft, QuadrantPiece.Name));
    }

    /// <summary>The places round a dot its name can go, the likeliest first: under, over, beside, then off each corner.</summary>
    private static IEnumerable<Rect> Around(Point centre, double radius, double width, double height)
    {
        var reach = radius + NameGap;
        var corner = (radius * Math.Sqrt(0.5)) + NameGap;
        yield return new Rect(centre.X - (width / 2), centre.Y + reach, width, height);
        yield return new Rect(centre.X - (width / 2), centre.Y - reach - height, width, height);
        yield return new Rect(centre.X + reach, centre.Y - (height / 2), width, height);
        yield return new Rect(centre.X - reach - width, centre.Y - (height / 2), width, height);
        yield return new Rect(centre.X + corner, centre.Y + corner, width, height);
        yield return new Rect(centre.X - corner - width, centre.Y + corner, width, height);
        yield return new Rect(centre.X + corner, centre.Y - corner - height, width, height);
        yield return new Rect(centre.X - corner - width, centre.Y - corner - height, width, height);
    }

    /// <summary>
    /// Where a quadrant's caption goes: in the middle of its quadrant where that is clear, or else whichever of its top, its
    /// foot, its corners and its sides crowds the points and their names least.
    /// </summary>
    private static Point Captioned(DiagramWords caption, Rect cell, IReadOnlyList<(Point Centre, double Radius)> dots, IReadOnlyList<Rect> taken)
    {
        var inside = new Rect(cell.Left + Gap, cell.Top + Gap, Math.Max(0, cell.Width - (Gap * 2)), Math.Max(0, cell.Height - (Gap * 2)));
        var (left, middle, right) = (inside.Left, inside.Left + ((inside.Width - caption.Width) / 2), inside.Right - caption.Width);
        var (top, centre, foot) = (inside.Top, inside.Top + ((inside.Height - caption.Height) / 2), inside.Bottom - caption.Height);

        Point[] places =
        [
            new(middle, centre), new(middle, top), new(middle, foot),
            new(left, top), new(right, top), new(left, foot), new(right, foot),
            new(left, centre), new(right, centre),
        ];

        return places
            .Select((at, rank) => (At: at, Cost: Crowding(new Rect(at, new Size(caption.Width, caption.Height)), dots, taken) + (rank * Preferred)))
            .MinBy(candidate => candidate.Cost).At;
    }

    /// <summary>How much a box overlaps the dots and the boxes already taken — the area it covers of each.</summary>
    private static double Crowding(Rect box, IReadOnlyList<(Point Centre, double Radius)> dots, IReadOnlyList<Rect> taken)
    {
        var crowding = 0.0;
        foreach (var other in taken)
        {
            var over = Rect.Intersect(box, other);
            if (!over.IsEmpty) crowding += over.Width * over.Height;
        }

        foreach (var (centre, radius) in dots)
        {
            // How far into the dot, and its clearance, the box's nearest point comes, over the width of the dot.
            var near = new Point(Math.Clamp(centre.X, box.Left, box.Right), Math.Clamp(centre.Y, box.Top, box.Bottom));
            var into = radius + DotClearance - (near - centre).Length;
            if (into > 0) crowding += into * (radius + DotClearance) * 2;
        }

        return crowding;
    }

    /// <summary>How much of a box stands off the chart — half as bad as covering something, since nothing may be there.</summary>
    private static double Outside(Rect box, Rect plot)
    {
        var within = Rect.Intersect(box, plot);
        return ((box.Width * box.Height) - (within.IsEmpty ? 0 : within.Width * within.Height)) / 2;
    }

    /// <summary>How many times the names look again for a clearer place.</summary>
    private const int Passes = 4;

    /// <summary>How far a name stands off its dot.</summary>
    private const double NameGap = 2;

    /// <summary>How far anything written keeps clear of a dot.</summary>
    private const double DotClearance = 2;

    /// <summary>What each place further down the list of likely ones costs, so the likelier wins where all are as clear.</summary>
    private const double Preferred = 0.01;

    /// <summary>A quadrant's cell: the first top right, then anticlockwise.</summary>
    private static Rect Cell(Rect plot, int index)
    {
        var right = index is 0 or 3;
        var top = index is 0 or 1;
        return new Rect(plot.Left + (right ? plot.Width / 2 : 0), plot.Top + (top ? 0 : plot.Height / 2), plot.Width / 2, plot.Height / 2);
    }

    private DiagramWords? Words(ContentPart? text, double size, string? fill, Brush ink) =>
        text is null ? null : Written(text.Words()!, text.Hole(), size, Ink.Written(fill) ?? ink);

    /// <summary>The words a line writes in one of its places — an axis's end, a caption, a point's name — where it writes any.</summary>
    private static ContentPart? Text(ContentPart? line, string role) =>
        line?.Children.FirstOrDefault(child => child.Kind == QuadrantKinds.Text && child.Role == role) is { } text && text.Words() is not null ? text : null;

    /// <summary>The word a line starts with: <c>x-axis</c>, <c>quadrant-2</c>.</summary>
    private static string? Word(ContentPart line) => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text;

    /// <summary>Which quadrant a caption's line is for — the first top right, then anticlockwise — or -1 for none.</summary>
    private static int Region(ContentPart line) =>
        Word(line) is { } word ? QuadrantGrammar.Regions.ToList().FindIndex(region => region.Equals(word, StringComparison.OrdinalIgnoreCase)) : -1;

    private void Quadrants(LayoutBuilder build, QuadrantConfig config, IReadOnlyList<ContentPart?> regions, IReadOnlyList<ContentPart?> captions,
                           IReadOnlyList<Rect> cells, Vector shift, Geometry over)
    {
        build.Open(QuadrantPiece.Quadrants, part: null, stops: Stops.None);

        for (var index = 0; index < 4; index++)
        {
            var shape = new RectangleGeometry(Rect.Offset(cells[index], shift));
            shape.Freeze();

            var fill = Ink.Written(config.QuadrantFills[index]) ?? DiagramInk.Faded(Ink.Series(index), Tint);
            var captioned = captions[index] is not null;

            build.Open(QuadrantPiece.Quadrant, captioned ? regions[index] : null, stops: Stops.None);
            build.Draw(new GeometryMark(shape, fill, null, 0));

            // A quadrant stands where nothing drawn over it does: a press on its caption or a point means that.
            if (captioned)
            {
                var stands = new CombinedGeometry(GeometryCombineMode.Exclude, shape, over);
                stands.Freeze();
                build.Occupies(stands);
            }

            build.Close();
        }

        build.Close();
    }

    private void Borders(LayoutBuilder build, QuadrantConfig config, Rect plot, Vector shift)
    {
        plot.Offset(shift);

        var outside = new RectangleGeometry(plot);
        var inside = new GeometryGroup
        {
            Children =
            {
                new LineGeometry(new Point(plot.Left + (plot.Width / 2), plot.Top), new Point(plot.Left + (plot.Width / 2), plot.Bottom)),
                new LineGeometry(new Point(plot.Left, plot.Top + (plot.Height / 2)), new Point(plot.Right, plot.Top + (plot.Height / 2))),
            },
        };
        outside.Freeze();
        inside.Freeze();

        build.Open(QuadrantPiece.Borders, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(inside, null, Ink.Written(config.InternalBorderFill) ?? Palette.CodeBorder, config.InternalBorderWidth ?? 1));
        build.Draw(new GeometryMark(outside, null, Ink.Written(config.ExternalBorderFill) ?? Palette.CodeBorder, config.ExternalBorderWidth ?? 1));
        build.Close();
    }

    private void Points(LayoutBuilder build, QuadrantConfig config, IReadOnlyList<Dot> dots, Vector shift)
    {
        build.Open(QuadrantPiece.Points, part: null, stops: Stops.None);

        foreach (var dot in dots)
        {
            var shape = new EllipseGeometry(dot.Centre + shift, dot.Radius, dot.Radius);
            shape.Freeze();

            var style = dot.Said.Style;
            var fill = Ink.Written(style.Colour) ?? Ink.Written(config.PointFill) ?? Ink.Series(dot.Order);
            var stroke = Ink.Written(style.StrokeColour);

            build.Open(QuadrantPiece.Point, dot.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, fill, stroke, stroke is null ? 0 : style.StrokeWidth ?? 1));
            build.Occupies(shape);
            build.Close();
        }

        build.Close();
    }
}
