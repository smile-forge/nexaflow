using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Painting;

/// <summary>
/// The wavy line a diagnostic wears — the shape a spelling mistake has worn in every editor for thirty
/// years, so it needs no explaining to anyone.
/// </summary>
public static class Squiggle
{
    /// <summary>
    /// A wave along the bottom of <paramref name="bounds"/>.
    /// </summary>
    /// <param name="wavelength">How long one full wave is. Shorter reads as busier, and as more wrong.</param>
    /// <param name="amplitude">How far it strays from the line.</param>
    public static Geometry Under(Rect bounds, double wavelength = 4, double amplitude = 1.1)
    {
        var geometry = new StreamGeometry();
        var baseline = bounds.Bottom + amplitude;

        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(bounds.X, baseline), isFilled: false, isClosed: false);

            // Quadratic arcs alternating up and down, one per half wave, so the ends always sit on the
            // line whatever the width — a wave cut off mid-rise reads as a rendering fault.
            var up = true;
            for (var x = bounds.X; x < bounds.Right; x += wavelength / 2, up = !up)
            {
                var next = Math.Min(x + wavelength / 2, bounds.Right);
                draw.QuadraticBezierTo(
                    new Point((x + next) / 2, baseline + (up ? -amplitude : amplitude)),
                    new Point(next, baseline),
                    isStroked: true,
                    isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Every wave for a set of runs, as one geometry — one draw call rather than a dozen.</summary>
    public static Geometry Under(IEnumerable<Rect> runs)
    {
        var group = new GeometryGroup();
        foreach (var run in runs.Where(r => r.Width > 0)) group.Children.Add(Under(run));
        group.Freeze();
        return group;
    }
}
