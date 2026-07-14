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
    /// <summary>Note-value denominator: 1=whole, 2=half, 4=quarter, 8=eighth, 16=sixteenth, 32, 64.</summary>
    public int Base { get; init; } = 4;

    /// <summary>Number of augmentation dots (each adds half of the running value).</summary>
    public int Dots { get; init; }

    public static readonly Duration Whole     = new() { Base = 1 };
    public static readonly Duration Half      = new() { Base = 2 };
    public static readonly Duration Quarter   = new() { Base = 4 };
    public static readonly Duration Eighth    = new() { Base = 8 };
    public static readonly Duration Sixteenth = new() { Base = 16 };

    /// <summary>Length measured in quarter notes (quarter = 1.0, whole = 4.0, dotted-quarter = 1.5).</summary>
    public double QuarterLength
    {
        get
        {
            double baseLen = 4.0 / Base;
            double total = baseLen, add = baseLen;
            for (int i = 0; i < Dots; i++) { add /= 2.0; total += add; }
            return total;
        }
    }

    /// <summary>True for eighth notes and shorter — these carry flags/beams.</summary>
    public bool IsBeamable => Base >= 8;

    /// <summary>Number of flags (or beam lines): eighth=1, sixteenth=2, thirty-second=3, else 0.</summary>
    public int FlagCount => Base >= 8 ? (int)Math.Round(Math.Log2(Base)) - 2 : 0;

    /// <summary>Builds a duration from a quarter-length, snapping to the nearest simple/dotted value.
    /// Used by parsers that compute time arithmetically (e.g. ABC's <c>L:</c> default length × factor).</summary>
    public static Duration FromQuarterLength(double ql)
    {
        if (ql <= 0) return Quarter;
        // Try plain then single-dotted values from whole down to 64th; pick the closest.
        Duration best = Quarter;
        double bestErr = double.MaxValue;
        foreach (int b in new[] { 1, 2, 4, 8, 16, 32, 64 })
            for (int dots = 0; dots <= 2; dots++)
            {
                var d = new Duration { Base = b, Dots = dots };
                double err = Math.Abs(d.QuarterLength - ql);
                if (err < bestErr) { bestErr = err; best = d; }
            }
        return best;
    }

    public override string ToString() => $"1/{Base}{new string('.', Dots)}";
}
