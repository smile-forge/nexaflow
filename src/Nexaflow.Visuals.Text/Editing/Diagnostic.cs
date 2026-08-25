using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>How much a diagnostic matters.</summary>
public enum DiagnosticSeverity
{
    /// <summary>The content could not be read here. It is shown as written, and means nothing.</summary>
    Error,

    /// <summary>It was read, but something about it is suspect.</summary>
    Warning,
}

/// <summary>
/// A stretch of source the content could not be made sense of, and why.
/// <para>
/// Rendered content that can be edited is wrong most of the time — every command is invalid until its
/// last letter is typed — so "it does not parse" cannot mean "show nothing". It means: draw what you can,
/// show the rest as the characters that were actually typed, and mark them. A node covered by an error is
/// <em>low confidence</em>: it was shown, not understood, so selection, promotion and copy should treat
/// it as opaque text rather than as structure.
/// </para>
/// </summary>
/// <param name="Start">Offset of the first character it covers.</param>
/// <param name="Length">How many characters.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Message">What went wrong, for the reader.</param>
public sealed record Diagnostic(int Start, int Length, DiagnosticSeverity Severity, string Message)
{
    /// <summary>One past the last character it covers.</summary>
    public int End => Start + Length;

    /// <summary>Whether this node's source falls inside the trouble.</summary>
    public bool Covers(ILayoutNode node) =>
        node.SourceLength > 0 && node.SourceStart >= Start && node.SourceEnd() <= End;
}

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
