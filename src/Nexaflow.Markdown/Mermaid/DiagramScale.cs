using System.Globalization;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Numbers along a range, as a chart marks them: round numbers — one, two or five times a power of ten apart — as many as
/// read easily, covering the range, and each written with as few decimals as its step needs.
/// </summary>
internal static class DiagramScale
{
    /// <summary>The step between round numbers that marks a range in about <paramref name="count"/> steps.</summary>
    public static double Step(double min, double max, int count = 5)
    {
        var raw = (max - min) / Math.Max(1, count);
        if (!(raw > 0) || double.IsInfinity(raw)) return 1;

        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / power;
        return (norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10) * power;
    }

    /// <summary>The range widened out to the round numbers either side of it, so its ends are ticks.</summary>
    public static (double Min, double Max) Nice(double min, double max, int count = 5)
    {
        if (max <= min) return (min, min + 1);

        var step = Step(min, max, count);
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step);
    }

    /// <summary>The round numbers from <paramref name="min"/> to <paramref name="max"/>, a step apart.</summary>
    public static IReadOnlyList<double> Ticks(double min, double max, int count = 5)
    {
        if (max <= min) return [min];

        var step = Step(min, max, count);
        var ticks = new List<double>();
        for (var tick = Math.Ceiling(min / step) * step; tick <= max + (step * 1e-6) && ticks.Count < 100; tick += step)
            ticks.Add(Math.Abs(tick) < step * 1e-6 ? 0 : tick);

        return ticks;
    }

    /// <summary>How far along the range a value is, from nought at <paramref name="min"/> to one at <paramref name="max"/>.</summary>
    public static double At(double value, double min, double max) => max > min ? (value - min) / (max - min) : 0;

    /// <summary>A tick's number as words: as many decimals as a step of <paramref name="step"/> needs, and no more.</summary>
    public static string Label(double value, double step)
    {
        var decimals = step >= 1 ? 0 : Math.Min(10, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
    }

    /// <summary>A number with as many decimals as it needs and no more — used where <see cref="Label"/>'s
    /// decimals-from-the-step doesn't apply because there's no even step to take them from.</summary>
    public static string Plain(double value) => value.ToString("0.############", CultureInfo.CurrentCulture);
}
