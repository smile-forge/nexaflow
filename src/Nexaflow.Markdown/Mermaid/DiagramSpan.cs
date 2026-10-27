namespace Nexaflow.Markdown.Mermaid;

/// <summary>A range and how it is read: where a value stands along it, where its ticks go, and what
/// each says. Wraps <see cref="DiagramScale"/> so an axis can also be logarithmic, square-rooted or
/// reversed. A value with no place (nought or less on a log scale) returns null rather than nought,
/// since nought is itself a real place on an axis.</summary>
internal sealed record DiagramSpan
{
    private DiagramSpan(double min, double max, DiagramTransform transform)
    {
        this.Min = min;
        this.Max = max;
        this.Transform = transform;
    }

    public double Min { get; }

    public double Max { get; }

    public DiagramTransform Transform { get; }

    /// <summary>The span covering <paramref name="min"/> to <paramref name="max"/>.</summary>
    /// <param name="widen">Whether the range is opened out to round numbers so its ends are ticks. False
    /// when the block wrote the ends itself.</param>
    public static DiagramSpan Of(double min, double max, DiagramTransform transform = DiagramTransform.Linear,
                                 bool widen = true)
    {
        if (Logs(transform))
        {
            var step = Base(transform);

            // Nowhere for nought or less to go. The span starts a step below the largest value instead,
            // and the values that cannot be placed are the caller's to report — never quietly moved.
            if (!(max > 0)) (min, max) = (1, step);
            if (!(min > 0)) min = max / step;

            if (!widen) return new DiagramSpan(min, max <= min ? min * step : max, transform);

            var low = Math.Pow(step, Math.Floor(Math.Log(min, step)));
            var high = Math.Pow(step, Math.Ceiling(Math.Log(max, step)));

            return new DiagramSpan(low, high <= low ? low * step : high, transform);
        }

        // A square root has nothing to say about less than nought either, but the axis itself is fine:
        // it simply starts at nought.
        if (transform == DiagramTransform.Sqrt && min < 0) min = 0;

        if (!widen) return new DiagramSpan(min, max <= min ? min + 1 : max, transform);

        var (from, to) = DiagramScale.Nice(min, max);
        if (transform == DiagramTransform.Sqrt && from < 0) from = 0;

        return new DiagramSpan(from, to <= from ? from + 1 : to, transform);
    }

    /// <summary>
    /// Where a value stands, from nought at the start to one at the end — or null where this span has
    /// nowhere to put it.
    /// </summary>
    public double? At(double value)
    {
        if (this.Forward(value) is not { } place
            || this.Forward(this.Min) is not { } low
            || this.Forward(this.Max) is not { } high)
            return null;

        var at = high > low ? (place - low) / (high - low) : 0;

        return this.Transform == DiagramTransform.Reverse ? 1 - at : at;
    }

    /// <summary>The value as this span reads it — so a fit down a logarithmic axis is a fit through the
    /// logarithms.</summary>
    public double? Reading(double value) => this.Forward(value);

    /// <summary>The value a reading came from: <see cref="Reading"/> turned about.</summary>
    public double Value(double reading) => this.Transform switch
    {
        DiagramTransform.Log10 => Math.Pow(10, reading),
        DiagramTransform.Log2 => Math.Pow(2, reading),
        DiagramTransform.NaturalLog => Math.Exp(reading),
        DiagramTransform.Sqrt => reading * reading,
        _ => reading,
    };

    /// <summary>The ticks along the span: each value, where it stands, and what it's written as. Round
    /// numbers on a plain axis, a step of the base (1, 10, 100) on a logarithmic one.</summary>
    public IReadOnlyList<(double Value, double At, string Says)> Ticks(int count = 5)
    {
        var marks = new List<(double Value, double At, string Says)>();

        if (Logs(this.Transform))
        {
            var step = Base(this.Transform);
            var from = (int)Math.Floor(Math.Log(this.Min, step));
            var to = (int)Math.Ceiling(Math.Log(this.Max, step));

            for (var power = from; power <= to && marks.Count < 40; power++)
            {
                var value = Math.Pow(step, power);
                if (value >= this.Min * (1 - 1e-9) && value <= this.Max * (1 + 1e-9) && this.At(value) is { } at)
                    marks.Add((value, at, DiagramScale.Plain(value)));
            }

            return marks;
        }

        var apart = DiagramScale.Step(this.Min, this.Max, count);

        foreach (var value in DiagramScale.Ticks(this.Min, this.Max, count))
            if (this.At(value) is { } at)
                marks.Add((value, at, DiagramScale.Label(value, apart)));

        return marks;
    }

    /// <summary>The value as this span reads it, or null where it cannot read it at all.</summary>
    private double? Forward(double value) => this.Transform switch
    {
        DiagramTransform.Log10 => value > 0 ? Math.Log10(value) : null,
        DiagramTransform.Log2 => value > 0 ? Math.Log2(value) : null,
        DiagramTransform.NaturalLog => value > 0 ? Math.Log(value) : null,
        DiagramTransform.Sqrt => value >= 0 ? Math.Sqrt(value) : null,
        _ => value,
    };

    private static bool Logs(DiagramTransform transform) =>
        transform is DiagramTransform.Log10 or DiagramTransform.Log2 or DiagramTransform.NaturalLog;

    private static double Base(DiagramTransform transform) => transform switch
    {
        DiagramTransform.Log2 => 2,
        DiagramTransform.NaturalLog => Math.E,
        _ => 10,
    };
}
