using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;

/// <summary>
/// Where a Venn diagram's circles go, and where inside each region its words go.
///
/// <para>
/// <strong>Areas mean sizes.</strong> Each circle's area is its set's size, and each pair of circles overlaps by as much as
/// the diagram says those two sets share — which is what Mermaid's layout (venn.js) does, and what makes <c>:20</c> on a
/// set and <c>:3</c> on a union say something. The overlap two circles need comes to a distance between their centres,
/// found by halving; the circles are then put down greedily, most-shared first, each where it best keeps its distances to
/// those already down, and moved together until the distances stop improving. Sets that share nothing are kept apart
/// rather than at an exact distance, and one inside another is kept inside rather than centred. Where three sets or more
/// have a size written for what they share, the circles are then moved together once more until every overlap — pairs
/// and those regions alike — covers as near its size as the rest allow (<see cref="Shared"/>, venn.js's loss).
/// </para>
/// <para>
/// <strong>Words go where there is most room.</strong> A region is inside its sets' circles and outside every other, and
/// what is written in it is centred on the point of it furthest from any edge — so a set's label sits in the part of its
/// circle nothing else covers, and an overlap's in the lens between its circles.
/// </para>
/// <para>
/// All of it is in the layout's own units, a set of size one being a circle of area one. Drawing it at a size is the
/// builder's.
/// </para>
/// </summary>
internal static class VennLayout
{
    /// <summary>A circle, placed.</summary>
    internal readonly record struct Circle(Point Centre, double Radius)
    {
        public Rect Bounds => new(Centre.X - Radius, Centre.Y - Radius, Radius * 2, Radius * 2);
    }

    /// <summary>How far apart two circles have to be: exactly, no nearer, or no further.</summary>
    private enum Rule
    {
        Exactly,
        AtLeast,
        AtMost,
    }

    /// <summary>How many angles round a circle already down a new one is tried at.</summary>
    private const int Angles = 36;

    /// <summary>How many times the whole layout is moved towards its distances before it is taken as it is.</summary>
    private const int Steps = 500;

    /// <summary>How finely a region is searched for where its words go, a side.</summary>
    private const int Samples = 40;

    /// <summary>
    /// Places circles of <paramref name="areas"/> so each pair overlaps by <paramref name="overlap"/>, and each region of three
    /// sets or more in <paramref name="regions"/> covers its size. The first set written ends up on the left of two and at the
    /// top of more, with the second to the left of the third, so the same diagram always comes out the same way round.
    /// </summary>
    public static Circle[] Place(IReadOnlyList<double> areas, Func<int, int, double> overlap,
                                 IReadOnlyList<(IReadOnlyList<int> Sets, double Size)>? regions = null)
    {
        var count = areas.Count;
        var radii = areas.Select(area => Math.Sqrt(Math.Max(area, 1e-9) / Math.PI)).ToArray();
        var sizes = new double[count, count];
        var apart = new (double Distance, Rule Rule)[count, count];
        var shared = new double[count];

        for (var one = 0; one < count; one++)
        {
            for (var other = one + 1; other < count; other++)
            {
                var area = Math.Min(Math.Max(0, overlap(one, other)), Math.Min(areas[one], areas[other]));
                sizes[one, other] = sizes[other, one] = area;
                apart[one, other] = apart[other, one] = Distance(radii[one], radii[other], area);

                shared[one] += area;
                shared[other] += area;
            }
        }

        var centres = new Point[count];
        var down = new List<int>(count);

        foreach (var next in Enumerable.Range(0, count).OrderByDescending(at => shared[at]).ThenBy(at => at))
        {
            if (down.Count > 0) centres[next] = Best(next);
            down.Add(next);
        }

        Settle(centres, apart);

        // Pairs first, because their distances can be found exactly; then what three sets or more share, which only moving
        // them all together can reach.
        var written = regions?
            .Where(region => region.Sets.Count >= 3 && region.Sets.All(set => set >= 0 && set < count))
            .Select(region => (region.Sets, Size: Math.Min(Math.Max(0, region.Size), region.Sets.Min(set => areas[set]))))
            .ToList();
        if (written is { Count: > 0 }) Fit(centres, radii, sizes, written);

        if (shared.Any(area => area > 0)) Orient(centres);

        return [.. centres.Select((centre, at) => new Circle(centre, radii[at]))];

        // Where a circle best keeps its distances to those already down: at the right distance from each it overlaps, all
        // the way round — or, overlapping none of them, beside them all.
        Point Best(int next)
        {
            var tried = new List<Point>();

            foreach (var placed in down.Where(placed => apart[placed, next].Rule != Rule.AtLeast))
                for (var step = 0; step < Angles; step++)
                {
                    var angle = step * 2 * Math.PI / Angles;
                    tried.Add(centres[placed] + (apart[placed, next].Distance * new Vector(Math.Cos(angle), Math.Sin(angle))));
                }

            if (tried.Count == 0)
            {
                var right = down.Max(placed => centres[placed].X + radii[placed]);
                tried.Add(new Point(right + Gap(radii[next], radii[next]) + radii[next], centres[down[0]].Y));
            }

            return tried.MinBy(point => down.Sum(placed => Penalty((point - centres[placed]).Length, apart[placed, next])));
        }
    }

    /// <summary>
    /// How much every one of <paramref name="circles"/> covers — venn.js's intersection area. Where their edges cross inside
    /// them all, it is the polygon through those crossings and, past each side, the slice of the circle that side runs along;
    /// where they do not, it is the smallest circle whole if it lies inside all the rest, and nothing if it does not.
    /// </summary>
    public static double Shared(IReadOnlyList<Circle> circles)
    {
        if (circles.Count == 0) return 0;
        if (circles.Count == 1) return Math.PI * circles[0].Radius * circles[0].Radius;

        var inside = new List<(Point At, int One, int Other)>();
        for (var one = 0; one < circles.Count; one++)
            for (var other = one + 1; other < circles.Count; other++)
                foreach (var crossing in Crossings(circles[one], circles[other]))
                    if (circles.All(circle => (crossing - circle.Centre).Length <= circle.Radius + 1e-10))
                        inside.Add((crossing, one, other));

        if (inside.Count <= 1)
        {
            var smallest = circles.MinBy(circle => circle.Radius);
            return circles.All(circle => (circle.Centre - smallest.Centre).Length <= Math.Abs(smallest.Radius - circle.Radius))
                ? Math.PI * smallest.Radius * smallest.Radius
                : 0;
        }

        var middle = new Point(inside.Average(crossing => crossing.At.X), inside.Average(crossing => crossing.At.Y));
        var around = inside.OrderByDescending(crossing => Math.Atan2(crossing.At.X - middle.X, crossing.At.Y - middle.Y)).ToList();

        double polygon = 0, slices = 0;
        var previous = around[^1];

        foreach (var current in around)
        {
            polygon += (previous.At.X + current.At.X) * (current.At.Y - previous.At.Y);

            // The side runs along whichever circle both its ends lie on — the narrower slice, where they lie on two.
            var halfway = new Point((current.At.X + previous.At.X) / 2, (current.At.Y + previous.At.Y) / 2);
            (double Radius, double Width)? slice = null;

            foreach (var edge in new[] { current.One, current.Other })
            {
                if (edge != previous.One && edge != previous.Other) continue;

                var circle = circles[edge];
                var from = Math.Atan2(current.At.X - circle.Centre.X, current.At.Y - circle.Centre.Y);
                var to = Math.Atan2(previous.At.X - circle.Centre.X, previous.At.Y - circle.Centre.Y);
                var turn = to - from;
                if (turn < 0) turn += 2 * Math.PI;

                var angle = to - (turn / 2);
                var rim = new Point(circle.Centre.X + (circle.Radius * Math.Sin(angle)), circle.Centre.Y + (circle.Radius * Math.Cos(angle)));
                var width = Math.Min(circle.Radius * 2, (halfway - rim).Length);

                if (slice is null || width < slice.Value.Width) slice = (circle.Radius, width);
            }

            if (slice is not { } found) continue;

            slices += Segment(found.Radius, found.Width);
            previous = current;
        }

        return slices + (polygon / 2);
    }

    /// <summary>Where the edges of two circles cross — nowhere, where they do not.</summary>
    private static IEnumerable<Point> Crossings(Circle one, Circle other)
    {
        var distance = (other.Centre - one.Centre).Length;
        if (distance >= one.Radius + other.Radius || distance <= Math.Abs(one.Radius - other.Radius)) yield break;

        var along = ((one.Radius * one.Radius) - (other.Radius * other.Radius) + (distance * distance)) / (2 * distance);
        var height = Math.Sqrt(Math.Max(0, (one.Radius * one.Radius) - (along * along)));

        var foot = one.Centre + ((other.Centre - one.Centre) * (along / distance));
        var across = new Vector(-(other.Centre.Y - one.Centre.Y) * (height / distance), -(other.Centre.X - one.Centre.X) * (height / distance));

        yield return new Point(foot.X + across.X, foot.Y - across.Y);
        yield return new Point(foot.X - across.X, foot.Y + across.Y);
    }

    /// <summary>The area of the slice cut off a circle by a chord, <paramref name="width"/> deep.</summary>
    private static double Segment(double radius, double width) =>
        (radius * radius * Math.Acos(Math.Clamp(1 - (width / radius), -1, 1)))
        - ((radius - width) * Math.Sqrt(Math.Max(0, width * ((2 * radius) - width))));

    /// <summary>
    /// Moves every circle together until each overlap written for them — the pairs, and the regions three sets or more share —
    /// covers as near its size as the others let it: the loss venn.js minimises, here by stepping down its slope, further while
    /// that helps and less far when it does not.
    /// </summary>
    private static void Fit(Point[] centres, double[] radii, double[,] pairs, IReadOnlyList<(IReadOnlyList<int> Sets, double Size)> regions)
    {
        var count = centres.Length;
        var reach = radii.Average();
        var nudge = reach * 1e-5;
        var step = reach * 0.1;

        var slope = new Vector[count];
        var trial = new Point[count];
        var loss = Loss(centres);

        for (var round = 0; round < Steps && step > reach * 1e-6; round++)
        {
            var steepest = 0.0;

            for (var at = 0; at < count; at++)
            {
                var centre = centres[at];

                centres[at] = centre + new Vector(nudge, 0);
                var right = Loss(centres);
                centres[at] = centre - new Vector(nudge, 0);
                var left = Loss(centres);
                centres[at] = centre + new Vector(0, nudge);
                var below = Loss(centres);
                centres[at] = centre - new Vector(0, nudge);
                var above = Loss(centres);
                centres[at] = centre;

                slope[at] = new Vector(right - left, below - above) / (2 * nudge);
                steepest = Math.Max(steepest, slope[at].Length);
            }

            if (steepest < 1e-12) break;

            for (var at = 0; at < count; at++) trial[at] = centres[at] - (step / steepest * slope[at]);

            var tried = Loss(trial);
            if (tried < loss)
            {
                Array.Copy(trial, centres, count);
                loss = tried;
                step *= 1.5;
            }
            else
            {
                step /= 2;
            }
        }

        double Loss(Point[] at)
        {
            var sum = 0.0;

            for (var one = 0; one < count; one++)
                for (var other = one + 1; other < count; other++)
                {
                    var off = Lens(radii[one], radii[other], (at[one] - at[other]).Length) - pairs[one, other];
                    sum += off * off;
                }

            foreach (var (sets, size) in regions)
            {
                var off = Shared([.. sets.Select(set => new Circle(at[set], radii[set]))]) - size;
                sum += off * off;
            }

            return sum;
        }
    }

    /// <summary>
    /// The point of a region furthest from any edge of it, and how far that is — or null where the circles leave the region
    /// no room at all.
    /// </summary>
    /// <param name="members">The circles the region is inside. It is outside every other.</param>
    public static (Point Centre, double Room)? Inside(IReadOnlyList<Circle> circles, IReadOnlyCollection<int> members)
    {
        if (members.Count == 0) return null;

        // Every point of the region is inside its smallest circle, so that is all that has to be searched.
        var within = circles[members.MinBy(member => circles[member].Radius)];
        var best = Search(within.Bounds, null);
        if (best is null) return null;

        // Then again, finer, round the best point found.
        var cell = within.Radius * 2 / Samples;
        return Search(new Rect(best.Value.Centre.X - cell, best.Value.Centre.Y - cell, cell * 2, cell * 2), best);

        (Point Centre, double Room)? Search(Rect area, (Point Centre, double Room)? found)
        {
            for (var across = 0; across <= Samples; across++)
            {
                for (var down = 0; down <= Samples; down++)
                {
                    var point = new Point(area.X + (area.Width * across / Samples), area.Y + (area.Height * down / Samples));
                    var room = double.PositiveInfinity;

                    for (var at = 0; at < circles.Count; at++)
                    {
                        var from = (point - circles[at].Centre).Length;
                        room = Math.Min(room, members.Contains(at) ? circles[at].Radius - from : from - circles[at].Radius);
                    }

                    if (room > 0 && (found is null || room > found.Value.Room)) found = (point, room);
                }
            }

            return found;
        }
    }

    /// <summary>How much two circles <paramref name="distance"/> apart overlap.</summary>
    public static double Lens(double one, double other, double distance)
    {
        if (distance >= one + other) return 0;
        if (distance <= Math.Abs(one - other)) return Math.PI * Math.Pow(Math.Min(one, other), 2);

        var near = one * one * Math.Acos(Math.Clamp(((distance * distance) + (one * one) - (other * other)) / (2 * distance * one), -1, 1));
        var far = other * other * Math.Acos(Math.Clamp(((distance * distance) + (other * other) - (one * one)) / (2 * distance * other), -1, 1));
        var chord = 0.5 * Math.Sqrt(Math.Max(0, (-distance + one + other) * (distance + one - other)
                                                * (distance - one + other) * (distance + one + other)));

        return near + far - chord;
    }

    /// <summary>How far apart two circles go for them to overlap by <paramref name="area"/>.</summary>
    private static (double Distance, Rule Rule) Distance(double one, double other, double area)
    {
        if (area <= 0) return (one + other + Gap(one, other), Rule.AtLeast);

        var smaller = Math.PI * Math.Pow(Math.Min(one, other), 2);
        if (area >= smaller * (1 - 1e-9)) return (Math.Abs(one - other), Rule.AtMost);

        // The overlap only shrinks as the circles move apart, so halving finds the distance.
        double near = Math.Abs(one - other), far = one + other;
        for (var halving = 0; halving < 60; halving++)
        {
            var middle = (near + far) / 2;
            if (Lens(one, other, middle) > area) near = middle;
            else far = middle;
        }

        return ((near + far) / 2, Rule.Exactly);
    }

    /// <summary>The clear air kept between two circles that share nothing.</summary>
    private static double Gap(double one, double other) => Math.Min(one, other) * 0.15;

    /// <summary>How badly two centres <paramref name="distance"/> apart keep to what is asked of them.</summary>
    private static double Penalty(double distance, (double Distance, Rule Rule) asked)
    {
        var off = distance - asked.Distance;
        return asked.Rule switch
        {
            Rule.AtLeast when off >= 0 => 0,
            Rule.AtMost when off <= 0 => 0,
            _ => off * off,
        };
    }

    /// <summary>
    /// Moves every circle a little at a time towards where it keeps its distances, taking a step only where it helps and
    /// stepping further while it does.
    /// </summary>
    private static void Settle(Point[] centres, (double Distance, Rule Rule)[,] apart)
    {
        var count = centres.Length;
        if (count < 2) return;

        var step = 0.05;
        var loss = Loss(centres);
        var trial = new Point[count];

        for (var round = 0; round < Steps && step > 1e-7; round++)
        {
            var pull = new Vector[count];

            for (var one = 0; one < count; one++)
            {
                for (var other = one + 1; other < count; other++)
                {
                    var between = centres[one] - centres[other];
                    var distance = between.Length;
                    if (distance < 1e-9) (between, distance) = (new Vector(1e-3, 0), 1e-3);

                    if (Penalty(distance, apart[one, other]) == 0) continue;

                    var push = 2 * (distance - apart[one, other].Distance) / distance * between;
                    pull[one] -= push;
                    pull[other] += push;
                }
            }

            for (var at = 0; at < count; at++) trial[at] = centres[at] + (step * pull[at]);

            var tried = Loss(trial);
            if (tried < loss)
            {
                Array.Copy(trial, centres, count);
                loss = tried;
                step *= 1.2;
            }
            else
            {
                step /= 2;
            }
        }

        double Loss(Point[] at)
        {
            var sum = 0.0;
            for (var one = 0; one < count; one++)
                for (var other = one + 1; other < count; other++)
                    sum += Penalty((at[one] - at[other]).Length, apart[one, other]);
            return sum;
        }
    }

    /// <summary>Turns the layout so the first set is on the left of two and at the top of more, and the second left of the third.</summary>
    private static void Orient(Point[] centres)
    {
        var count = centres.Length;
        if (count < 2) return;

        var middle = new Point(centres.Average(centre => centre.X), centres.Average(centre => centre.Y));
        var first = centres[0] - middle;

        if (first.Length > 1e-9)
        {
            var turn = (count == 2 ? Math.PI : -Math.PI / 2) - Math.Atan2(first.Y, first.X);
            var (cos, sin) = (Math.Cos(turn), Math.Sin(turn));

            for (var at = 0; at < count; at++)
            {
                var from = centres[at] - middle;
                centres[at] = middle + new Vector((from.X * cos) - (from.Y * sin), (from.X * sin) + (from.Y * cos));
            }
        }

        if (count >= 3 && centres[1].X > centres[2].X)
            for (var at = 0; at < count; at++)
                centres[at] = new Point((2 * middle.X) - centres[at].X, centres[at].Y);
    }
}
