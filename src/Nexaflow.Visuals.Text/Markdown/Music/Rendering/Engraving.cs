using System;
using System.Collections.Generic;
using System.Linq;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The judgement calls of engraving, separated from the drawing so they can be reasoned about (and asserted)
/// on their own: which way a stem points, and how steeply a beam sits over its group. Everything else in the
/// builder is mechanical.
/// </summary>
internal static class Engraving
{
    /// <summary>The middle staff line, in half-spaces above the bottom line.</summary>
    public const int MiddleLine = 4;

    /// <summary>
    /// Which way the stems of a note, a chord or a beamed group point, given the half-spaces above the bottom
    /// line its heads sit at: away from the middle line, with the head reaching furthest from it deciding for
    /// all of them.
    /// <para>
    /// <strong>The tie goes down</strong> — a note sitting <em>on</em> the middle line, or a group reaching
    /// equally far both ways. There is no rule to appeal to here: engravers take either way and choose by
    /// how the line looks, which is not a judgement this can make. So it is settled by matching the corpus,
    /// whose own engraver stems the middle line down across all ten thousand of its tunes. A convention
    /// borrowed is worth more than a coin flip, and this is the only case the rule leaves open.
    /// </para>
    /// </summary>
    public static bool StemDown(IReadOnlyList<int> halves) =>
        halves.Max() - MiddleLine >= MiddleLine - halves.Min();

    /// <summary>
    /// The slope of a beam, from the y of the head each stem must clear and the x of each stem.
    ///
    /// A beam only leans when the group's contour genuinely leans: a run that climbs, falls and climbs again
    /// (<c>ABcdABcd</c>) beams flat, because a first-to-last slope drawn through a zig-zag asserts a direction
    /// the music doesn't have. When the contour <em>is</em> monotonic the beam takes half the interval,
    /// capped both in absolute rise and in steepness — a beam that tracked the pitch one-for-one would be a
    /// staircase, not a beam.
    /// </summary>
    public static double BeamSlope(IReadOnlyList<double> x, IReadOnlyList<double> outerY)
    {
        int n = outerY.Count;
        if (n < 2) return 0;

        bool rising = true, falling = true;
        for (int i = 1; i < n; i++)
        {
            if (outerY[i] > outerY[i - 1]) rising = false;   // y grows downward, so "rising" is y shrinking
            if (outerY[i] < outerY[i - 1]) falling = false;
        }
        if (!rising && !falling) return 0;

        double span = x[n - 1] - x[0];
        if (span <= 0.5) return 0;

        double dy = Math.Clamp((outerY[n - 1] - outerY[0]) * 0.5, -MaxBeamRise, MaxBeamRise);
        return Math.Clamp(dy / span, -MaxBeamSlope, MaxBeamSlope);
    }
}
