using System;
using System.Collections.Generic;

namespace Nexaflow.Core.Themes;

/// <summary>
/// The desert's weather: one long cycle of wind that every moving part of <see cref="DesertScene"/> follows.
/// The bushes lean into it, sand streaks and swirls ride its gusts, the crest plumes and the ground drift
/// thicken with it, and once a cycle it builds toward a storm that never quite arrives.
/// <para>
/// It is a pure function of time — seconds into the cycle — with no WPF in it, because the one promise the
/// theme makes about it, <b>a storm at most once an hour</b>, has to be provable without a window. Two things
/// keep that promise. The cycle is longer than <see cref="StormSpacing"/> and holds exactly one storm, at its
/// very start, so consecutive storms are a whole cycle apart. And a new desert enters the cycle at
/// <see cref="OpeningSeconds"/>, after the storm has settled and at least an hour before the next one — so a
/// scene that is torn down and made again (a theme change, the battery policy dropping and restoring it) can
/// never follow one storm with another sooner than that, whatever the scene before it was showing.
/// </para>
/// <para>
/// Strength runs from 0 for still air to 1 for a full storm. The air is never still (<see cref="Calm"/> is a
/// breeze), a gust lifts it to around half, and the storm crests short of 1: it threatens, then fades.
/// </para>
/// </summary>
internal sealed class DesertWind
{
    /// <summary>The fewest seconds between the starts of two storms.</summary>
    public const double StormSpacing = 3600;

    /// <summary>The breeze that is always blowing, as a strength.</summary>
    public const double Calm = 0.1;

    /// <summary>
    /// One gust: a front that crosses the window from the left in <see cref="Crossing"/> seconds, lifting the
    /// wind to <see cref="Peak"/> as it passes. <see cref="Swirls"/> gusts carry an eddy of sand with them.
    /// </summary>
    public readonly record struct Gust(double Start, double Rise, double Hold, double Fall,
                                       double Peak, double Crossing, bool Swirls)
    {
        /// <summary>When the gust has passed the window's left edge.</summary>
        public double End => Start + Rise + Hold + Fall;

        /// <summary>When the gust has passed the window's right edge, too.</summary>
        public double Gone => End + Crossing;

        /// <summary>The gust's strength at <paramref name="t"/>, where its front arrives at <see cref="Start"/>.</summary>
        public double At(double t)
        {
            double u = t - Start;
            if (u <= 0 || u >= Rise + Hold + Fall) return 0;
            if (u < Rise)        return Peak * SmoothStep(u / Rise);
            if (u < Rise + Hold) return Peak;
            return Peak * (1 - SmoothStep((u - Rise - Hold) / Fall));
        }
    }

    /// <summary>
    /// The storm that opens every cycle: it gathers for <see cref="Build"/> seconds, holds at its
    /// <see cref="Peak"/> for <see cref="Crest"/>, and dies away over <see cref="Fade"/>.
    /// </summary>
    public readonly record struct StormCourse(double Build, double Crest, double Fade, double Peak)
    {
        /// <summary>When the storm has died away.</summary>
        public double End => Build + Crest + Fade;

        /// <summary>The storm's strength at <paramref name="t"/>.</summary>
        public double At(double t)
        {
            if (t <= 0 || t >= End) return 0;
            if (t < Build)          return Peak * SmoothStep(t / Build);
            if (t < Build + Crest)  return Peak;
            return Peak * (1 - SmoothStep((t - Build - Crest) / Fade));
        }
    }

    /// <summary>The resolution <see cref="TravelAt"/> is tabulated at, in seconds — finer than any gust's rise.</summary>
    private const double TravelStep = 0.25;

    private readonly Gust[] _gusts;
    private readonly double[] _travel;

    public DesertWind(Random rng)
    {
        Storm = new StormCourse(Build: 150 + rng.NextDouble() * 90,
                          Crest: 40 + rng.NextDouble() * 50,
                          Fade: 210 + rng.NextDouble() * 100,
                          Peak: 0.76 + rng.NextDouble() * 0.12);

        // After the storm the air stays calm a while before the gusts come back — that settling is what makes
        // the storm read as having passed rather than as having been switched off.
        SettledSeconds = Storm.End + 120 + rng.NextDouble() * 120;
        CycleSeconds   = 80 * 60 + rng.NextDouble() * 20 * 60;
        OpeningSeconds = SettledSeconds + rng.NextDouble() * (CycleSeconds - StormSpacing - SettledSeconds);

        _gusts  = PlanGusts(rng);
        _travel = Integrate();
    }

    /// <summary>The length of the cycle, which opens with the storm and then repeats.</summary>
    public double CycleSeconds { get; }

    /// <summary>Where a new desert enters the cycle: in the calm, at least an hour before the next storm.</summary>
    public double OpeningSeconds { get; }

    /// <summary>When the air has settled after the storm and the gusts begin again.</summary>
    public double SettledSeconds { get; }

    /// <summary>The cycle's storm, which starts it.</summary>
    public StormCourse Storm { get; }

    /// <summary>The cycle's gusts, in order; none overlaps another, even at the far edge of the window.</summary>
    public IReadOnlyList<Gust> Gusts => _gusts;

    /// <summary>The storm's part of the wind at <paramref name="t"/>.</summary>
    public double StormAt(double t) => Storm.At(t);

    /// <summary>
    /// The gusts' part of the wind at <paramref name="t"/>, felt at <paramref name="across"/> (0 at the window's
    /// left edge, 1 at its right) — a gust reaches the right edge a <see cref="Gust.Crossing"/> after the left.
    /// </summary>
    public double GustAt(double t, double across)
    {
        // Gusts never overlap, even counting the time a front takes to cross, so the only gust that can be
        // blowing at t is the last one to have started.
        int i = LastStartedBy(t);
        if (i < 0) return 0;
        var g = _gusts[i];
        return g.At(t - across * g.Crossing);
    }

    /// <summary>The whole wind at <paramref name="t"/>, at <paramref name="across"/>: the breeze, any gust, and the storm.</summary>
    public double StrengthAt(double t, double across)
    {
        // Gust and storm combine as independent chances of being blown: neither can push the other past 1,
        // and a gust inside the storm still lifts it, a little.
        double moving = 1 - (1 - StormAt(t)) * (1 - GustAt(t, across));
        return Calm + (1 - Calm) * moving;
    }

    /// <summary>
    /// How far the air has moved by <paramref name="t"/>: the wind's strength mid-window, summed over the cycle so
    /// far. Sand carried at a speed proportional to the wind is at a distance proportional to this, so a drift
    /// that follows it races in a gust and crawls in the calm without its speed ever jumping.
    /// </summary>
    public double TravelAt(double t)
    {
        double f = Math.Clamp(t, 0, CycleSeconds) / TravelStep;
        int    i = Math.Min((int)f, _travel.Length - 2);
        return _travel[i] + (_travel[i + 1] - _travel[i]) * (f - i);
    }

    /// <summary>How far the air moves in one whole cycle — <see cref="TravelAt"/> at the cycle's end.</summary>
    public double TravelPerCycle => TravelAt(CycleSeconds);

    // Tabulated once, by the trapezoid rule, so a frame's lookup is an index and a lerp rather than an integral.
    private double[] Integrate()
    {
        int steps = (int)Math.Ceiling(CycleSeconds / TravelStep);
        var travel = new double[steps + 2];
        double previous = StrengthAt(0, 0.5);
        for (int i = 1; i < travel.Length; i++)
        {
            double next = StrengthAt(Math.Min(i * TravelStep, CycleSeconds), 0.5);
            travel[i] = travel[i - 1] + (previous + next) * 0.5 * TravelStep;
            previous  = next;
        }
        return travel;
    }

    private int LastStartedBy(double t)
    {
        int lo = 0, hi = _gusts.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_gusts[mid].Start <= t) { found = mid; lo = mid + 1; }
            else                         hi = mid - 1;
        }
        return found;
    }

    private Gust[] PlanGusts(Random rng)
    {
        // The last few minutes before the cycle wraps into the storm are restless: gusts come closer together
        // and blow harder, so the storm has a lead-in rather than arriving out of a clear sky.
        double restlessFrom = CycleSeconds - (200 + rng.NextDouble() * 120);

        // Every gust is gone from the window before the cycle wraps, so the wrap lands on the breeze alone and
        // the storm opens the next cycle from a seamless calm.
        double lastGone = CycleSeconds - 20;

        var gusts = new List<Gust>();
        double t = SettledSeconds + GapAfterGust(rng, restless: false);
        while (true)
        {
            bool restless = t >= restlessFrom;
            var g = new Gust(
                Start:    t,
                Rise:     1.2 + rng.NextDouble() * 1.4,
                Hold:     1.5 + rng.NextDouble() * 3.5,
                Fall:     2.0 + rng.NextDouble() * 2.5,
                Peak:     restless ? 0.45 + rng.NextDouble() * 0.25 : 0.26 + rng.NextDouble() * 0.32,
                Crossing: 2.6 + rng.NextDouble() * 2.0,
                Swirls:   rng.NextDouble() < 0.35);
            if (g.Gone > lastGone) break;
            gusts.Add(g);
            t = g.Gone + GapAfterGust(rng, restless);
        }
        return gusts.ToArray();
    }

    // Occasional, not periodic: mostly half a minute to a minute and a half of breeze between gusts, skewed so
    // a long lull is commoner than a short one — a steady beat would read as machinery.
    private static double GapAfterGust(Random rng, bool restless) =>
        restless ? 8 + rng.NextDouble() * 18
                 : 20 + 75 * Math.Sqrt(rng.NextDouble());

    private static double SmoothStep(double u)
    {
        u = Math.Clamp(u, 0, 1);
        return u * u * (3 - 2 * u);
    }
}
