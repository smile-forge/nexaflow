using System;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The judgement calls of engraving, separated from the drawing so they can be reasoned about (and asserted)
/// on their own: which way a stem points, and how steeply a beam sits over its group. Everything else in the
/// painter is mechanical.
/// </summary>
internal static class Engraving
{
    /// <summary>The staff positions an event occupies, in half-spaces above the bottom line — one value twice
    /// over for a note, the full spread for a chord. Rests nominally sit on the middle line.</summary>
    public static (int Lo, int Hi) Span(MusicalEvent ev, StaffGeometry g)
    {
        switch (ev)
        {
            case Note n:
                int h = g.HalfSpacesAbove(n.Pitch);
                return (h, h);
            case Chord c when c.Notes.Count > 0:
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (var cn in c.Notes)
                {
                    int x = g.HalfSpacesAbove(cn.Pitch);
                    lo = Math.Min(lo, x);
                    hi = Math.Max(hi, x);
                }
                return (lo, hi);
            default:
                return (MiddleLine, MiddleLine);
        }
    }

    /// <summary>The middle staff line, in half-spaces above the bottom line.</summary>
    public const int MiddleLine = 4;

    /// <summary>
    /// Stems point away from the middle line, and the note that reaches furthest from it decides for the whole
    /// beam group. A note sitting <em>on</em> the middle line takes an up stem — the tie breaks upward, which
    /// is what ABC engravers do and what the reference tunes show.
    /// </summary>
    public static bool StemDown(IReadOnlyList<MusicalEvent> group, StaffGeometry g)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (var ev in group)
        {
            var (a, b) = Span(ev, g);
            lo = Math.Min(lo, a);
            hi = Math.Max(hi, b);
        }
        return hi - MiddleLine > MiddleLine - lo;
    }

    public static bool StemDown(MusicalEvent ev, StaffGeometry g) => StemDown([ev], g);

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
