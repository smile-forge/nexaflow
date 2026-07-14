using System;

namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// A note/rest duration: a power-of-two base value (<see cref="Base"/>: 1=whole, 2=half, 4=quarter,
/// 8=eighth, 16=sixteenth, …) plus augmentation <see cref="Dots"/>. Length is expressed in quarter
/// notes so both parsers can accumulate time against a <see cref="TimeSignature"/> regardless of the
/// source's default note length.
/// </summary>
public sealed class Duration
{
    /// <summary>Note-value denominator: 1=whole, 2=half, 4=quarter, 8=eighth, 16=sixteenth, 32, 64 — plus the
    /// one non-denominator value, <c>0</c>, which is the <em>breve</em> (double whole, 8 quarter-lengths). ABC
    /// reaches it whenever the unit length × multiplier lands on two whole notes (e.g. <c>A16</c> under
    /// <c>L:1/8</c>), and it engraves as its own note head, so it has to be representable.</summary>
    public int Base { get; init; } = 4;

    /// <summary>Number of augmentation dots (each adds half of the running value).</summary>
    public int Dots { get; init; }

    public static readonly Duration Breve     = new() { Base = 0 };
    public static readonly Duration Whole     = new() { Base = 1 };
    public static readonly Duration Half      = new() { Base = 2 };
    public static readonly Duration Quarter   = new() { Base = 4 };
    public static readonly Duration Eighth    = new() { Base = 8 };
    public static readonly Duration Sixteenth = new() { Base = 16 };

    /// <summary>True for the double-whole note head (no stem, no flags).</summary>
    public bool IsBreve => Base <= 0;

    /// <summary>Length measured in quarter notes (quarter = 1.0, whole = 4.0, dotted-quarter = 1.5).</summary>
    public double QuarterLength
    {
        get
        {
            double baseLen = IsBreve ? 8.0 : 4.0 / Base;
            double total = baseLen, add = baseLen;
            for (int i = 0; i < Dots; i++) { add /= 2.0; total += add; }
            return total;
        }
    }

    /// <summary>True for eighth notes and shorter — these carry flags/beams.</summary>
    public bool IsBeamable => Base >= 8;

    /// <summary>Number of flags (or beam lines): eighth=1, sixteenth=2, thirty-second=3, else 0.</summary>
    public int FlagCount => Base >= 8 ? (int)Math.Round(Math.Log2(Base)) - 2 : 0;

    /// <summary>The same note value lengthened by <paramref name="dots"/> extra dots — ABC's broken-rhythm
    /// <c>&gt;</c>/<c>&lt;</c> lengthens one side of a pair exactly this way (one <c>&gt;</c> = one dot).</summary>
    public Duration Dotted(int dots) => new() { Base = Base, Dots = Dots + dots };

    /// <summary>The same note value halved <paramref name="times"/> times — the shortened half of a broken
    /// rhythm pair (an eighth under <c>A&gt;A</c> becomes a sixteenth). Dots are dropped, as ABC intends.</summary>
    public Duration Halved(int times)
    {
        int b = IsBreve ? 1 : Base;                       // a halved breve is a whole note
        for (int i = IsBreve ? 1 : 0; i < times; i++) b = Math.Min(b * 2, 64);
        return new Duration { Base = b };
    }

    /// <summary>Builds a duration from a quarter-length, snapping to the nearest simple/dotted value.
    /// Used by parsers that compute time arithmetically (e.g. ABC's <c>L:</c> default length × factor).</summary>
    public static Duration FromQuarterLength(double ql)
    {
        if (ql <= 0) return Quarter;
        // Try plain then dotted values from the breve down to a 64th; pick the closest.
        Duration best = Quarter;
        double bestErr = double.MaxValue;
        foreach (int b in new[] { 0, 1, 2, 4, 8, 16, 32, 64 })
            for (int dots = 0; dots <= 3; dots++)
            {
                var d = new Duration { Base = b, Dots = dots };
                double err = Math.Abs(d.QuarterLength - ql);
                if (err < bestErr - 1e-9) { bestErr = err; best = d; }
            }
        return best;
    }

    public override string ToString() => (IsBreve ? "2/1" : $"1/{Base}") + new string('.', Dots);
}
