using System.Windows;

namespace Nexaflow.Visuals.Common.Locate;

/// <summary>
/// The shape a lasso is thrown in: a rounded box clear of the control on every side — a pill round a thin search box, a
/// rounded square round an icon — drawn once round and on past where it started, swinging wider as it goes and then
/// tucking back inside, so the tail cuts across the line it began on. A loop, not a frame.
/// <para>
/// Pure geometry, so the promise that matters — the loop never cuts into the thing it points at — is checked for every
/// shape of target rather than eyeballed for one.
/// </para>
/// </summary>
public static class LassoGeometry
{
    private const double Overshoot = 0.14;   // of a lap, past the start: the part that crosses
    private const double Drift     = 0.50;   // of the padding, outward by the end of the lap
    private const double Tuck      = 0.20;   // …and inward by the very end, so the tail cuts back over the loop
    private const double Waver     = 0.10;   // of the padding, either way: the hand
    private const double Laps      = 3;      // wavers per lap

    /// <summary>How far clear of the target the loop runs: a quarter of its short side, held between 6 and 14 px.</summary>
    public static double PaddingFor(Size target) => Math.Clamp(Math.Min(target.Width, target.Height) * 0.25, 6, 14);

    /// <summary>The loop round <paramref name="target"/>, as points joined start to end.</summary>
    public static IReadOnlyList<Point> Loop(Rect target)
    {
        var box = Box.Round(target);
        var run = box.Perimeter * (1 + Overshoot);
        var count = Math.Clamp((int)(run / 3), 72, 480);
        var lap = 1 / (1 + Overshoot);   // where the first time round ends and the crossing part begins

        var points = new Point[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var t = (double)i / count;
            var (at, outward) = box.At(box.Start + run * t);
            // Wider all the way round, and then the tail cuts back inside the line it started on. Without that last part the
            // two passes run parallel and never meet, which is a spiral — the crossing is what makes it read as a thrown loop.
            var drift = t <= lap ? Drift * (t / lap) : Drift - (Drift + Tuck) * ((t - lap) / (1 - lap));
            var offset = box.Padding * (drift + Waver * Math.Sin(2 * Math.PI * Laps * (1 + Overshoot) * t));
            points[i] = at + outward * offset;
        }
        return points;
    }

    /// <summary>The length of the line through <paramref name="points"/> — what one dash must span to draw it on.</summary>
    public static double Length(IReadOnlyList<Point> points)
    {
        var length = 0.0;
        for (var i = 1; i < points.Count; i++) length += (points[i] - points[i - 1]).Length;
        return length;
    }

    /// <summary>Where a step's number rides: the loop's top-right-most point.</summary>
    public static Point BadgeAnchor(IReadOnlyList<Point> points)
    {
        var best = points[0];
        foreach (var p in points)
            if (p.X - p.Y > best.X - best.Y) best = p;
        return best;
    }

    // The rounded box the loop follows, walked by distance along its edge: clockwise from the top edge's left end.
    private readonly record struct Box(Rect Outer, double Radius, double Padding)
    {
        public static Box Round(Rect target)
        {
            var size = new Size(Math.Max(0, target.Width), Math.Max(0, target.Height));
            var padding = PaddingFor(size);
            var outer = new Rect(target.X - padding, target.Y - padding, size.Width + 2 * padding, size.Height + 2 * padding);
            // One and a half times the padding keeps every corner of the target at least ~0.79 padding inside the curve —
            // enough that the tail can tuck back in without grazing the control. A thin box gets a pill.
            var radius = Math.Min(1.5 * padding, Math.Min(outer.Width, outer.Height) / 2);
            return new Box(outer, radius, padding);
        }

        private double Across => Outer.Width - 2 * Radius;
        private double Down   => Outer.Height - 2 * Radius;
        private double Arc    => Math.PI * Radius / 2;

        public double Perimeter => 2 * Across + 2 * Down + 4 * Arc;

        // Halfway round the bottom-left corner — where a right hand's throw begins: past the top edge, the two
        // right-hand corners, the right edge, the bottom edge, and half of that corner.
        public double Start => 2 * Across + Down + 2 * Arc + Arc / 2;

        /// <summary>The point <paramref name="distance"/> along the edge (it wraps), and the way out from there.</summary>
        public (Point At, Vector Outward) At(double distance)
        {
            var d = distance % Perimeter;
            if (d < 0) d += Perimeter;

            double l = Outer.Left, t = Outer.Top, r = Outer.Right, b = Outer.Bottom, rad = Radius;

            if (d < Across) return (new Point(l + rad + d, t), new Vector(0, -1));
            d -= Across;
            if (d < Arc) return Corner(new Point(r - rad, t + rad), -Math.PI / 2 + d / Math.Max(rad, 1e-9));
            d -= Arc;
            if (d < Down) return (new Point(r, t + rad + d), new Vector(1, 0));
            d -= Down;
            if (d < Arc) return Corner(new Point(r - rad, b - rad), d / Math.Max(rad, 1e-9));
            d -= Arc;
            if (d < Across) return (new Point(r - rad - d, b), new Vector(0, 1));
            d -= Across;
            if (d < Arc) return Corner(new Point(l + rad, b - rad), Math.PI / 2 + d / Math.Max(rad, 1e-9));
            d -= Arc;
            if (d < Down) return (new Point(l, b - rad - d), new Vector(-1, 0));
            d -= Down;
            return Corner(new Point(l + rad, t + rad), Math.PI + Math.Min(d, Arc) / Math.Max(rad, 1e-9));

            (Point, Vector) Corner(Point centre, double angle)
            {
                var outward = new Vector(Math.Cos(angle), Math.Sin(angle));
                return (centre + outward * rad, outward);
            }
        }
    }
}
