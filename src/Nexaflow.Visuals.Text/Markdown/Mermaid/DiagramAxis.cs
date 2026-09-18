using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>A mark along an axis: how far along it, from nought at its start to one at its end, and the words beside it, where it has any.</summary>
internal sealed record DiagramTick(double At, DiagramWords? Words);

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

    /// <summary>
    /// A number with as many decimals as it needs and no more — how a log tick, a break a block wrote, or
    /// a number beside a colour bar is written, where <see cref="Label"/>'s decimals-from-the-step does not
    /// apply because there is no even step to take them from.
    /// </summary>
    public static string Plain(double value) => value.ToString("0.############", CultureInfo.CurrentCulture);
}

/// <summary>How a value becomes a distance along an axis.</summary>
internal enum DiagramTransform
{
    Linear,

    Log10,
    Log2,
    NaturalLog,

    Sqrt,

    /// <summary>Linear, with the axis running the other way.</summary>
    Reverse,
}

/// <summary>
/// A range and how it is read: where a value stands along it, where its ticks go, and what each says.
///
/// <para>
/// <see cref="DiagramScale"/> answers those for a plain range and is what every chart drawn so far
/// needs. This wraps it so an axis can also be logarithmic, square-rooted or reversed — which is what
/// a scatter plot of anything spanning orders of magnitude needs, and what ggplot2 spells
/// <c>scale_x_log10</c>.
/// </para>
/// <para>
/// <strong>A value with no place returns null rather than nought.</strong> Nought is a real place on an
/// axis, and a log scale has nothing whatever to say about a value of nought or less — so the two have
/// to be different answers, and the caller decides whether that is a mark left out or a block that will
/// not draw.
/// </para>
/// </summary>
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

    /// <summary>
    /// The span covering <paramref name="min"/> to <paramref name="max"/>.
    /// </summary>
    /// <param name="widen">
    /// Whether the range is opened out to round numbers so its ends are ticks. False where the block
    /// wrote the ends itself, which is a reader asking for exactly those.
    /// </param>
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

    /// <summary>
    /// The value as this span reads it — the number anything worked out <em>over</em> the axis is worked
    /// out from, so a fit down a logarithmic axis is a fit through the logarithms.
    /// </summary>
    public double? Reading(double value) => this.Forward(value);

    /// <summary>
    /// The value a reading came from: <see cref="Reading"/> turned about, so what was worked out in the
    /// axis's own terms can be said in the reader's again.
    /// </summary>
    public double Value(double reading) => this.Transform switch
    {
        DiagramTransform.Log10 => Math.Pow(10, reading),
        DiagramTransform.Log2 => Math.Pow(2, reading),
        DiagramTransform.NaturalLog => Math.Exp(reading),
        DiagramTransform.Sqrt => reading * reading,
        _ => reading,
    };

    /// <summary>
    /// The ticks along the span: each value, where it stands, and what it is written as. Round numbers
    /// on a plain axis, and a step of the base on a logarithmic one — 1, 10, 100, which is the only
    /// numbering of a log axis anybody reads.
    /// </summary>
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

/// <summary>
/// The lines across a panel at each of an axis's ticks.
///
/// <para>
/// Its own piece rather than a long tick on the axis: a tick is drawn <em>outward</em>, away from what
/// the axis is measuring, so lengthening one reaches out into the margin rather than back across the
/// panel. They are also drawn before everything else and stand for nothing anybody wrote, which a tick
/// does not.
/// </para>
/// </summary>
internal static class DiagramGrid
{
    /// <summary>
    /// Draws a line across <paramref name="panel"/> at each of <paramref name="ticks"/>.
    /// </summary>
    /// <param name="upright">
    /// Whether the ticks belong to the axis running up the page, whose lines therefore run across.
    /// </param>
    public static void Draw(LayoutBuilder build, string kind, Rect panel, IReadOnlyList<DiagramTick> ticks,
                            bool upright, DiagramStroke stroke)
    {
        if (ticks.Count == 0) return;

        var lines = new GeometryGroup();

        foreach (var mark in ticks)
            lines.Children.Add(upright
                ? new LineGeometry(new Point(panel.Left, panel.Bottom - (mark.At * panel.Height)),
                                   new Point(panel.Right, panel.Bottom - (mark.At * panel.Height)))
                : new LineGeometry(new Point(panel.Left + (mark.At * panel.Width), panel.Top),
                                   new Point(panel.Left + (mark.At * panel.Width), panel.Bottom)));

        lines.Freeze();

        // Nothing wrote a gridline, so it stands for nothing and is nowhere to put a caret.
        build.Open(kind, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(lines, null, stroke.Ink, stroke.Thickness) { Dashes = stroke.Dashes });
        build.Close();
    }
}

/// <summary>
/// An axis: a line, a tick at each mark along it, and the words beside each tick — a chart's values up its side, its
/// categories along its foot, a timeline's dates.
///
/// <para>
/// A tick's words are whatever they are (<see cref="DiagramWords"/>): a category somebody wrote is typed into where it
/// stands under its tick, and a number the axis worked out is only pressed. The axis is one piece standing for what it
/// was written as — an <c>x-axis</c> line — so pressing the line means that line.
/// </para>
/// </summary>
internal static class DiagramAxis
{
    /// <summary>How long a tick is, and the clear air between it and its words.</summary>
    public const double TickLength = 5;
    public const double Gap = 4;

    /// <summary>How far past the line an axis's ticks and words reach — the room to leave for them beside a chart.</summary>
    /// <param name="upright">Whether the axis runs up the page, which is what makes its words' width the room they take.</param>
    /// <param name="tick">How long its ticks are drawn — nought for none.</param>
    public static double Room(IReadOnlyList<DiagramTick> ticks, bool upright, double tick = TickLength) =>
        tick + Gap + ticks.Select(mark => mark.Words is null ? 0 : upright ? mark.Words.Width : mark.Words.Height).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Draws an axis from <paramref name="from"/> to <paramref name="to"/> as a piece of <paramref name="kind"/> standing for
    /// <paramref name="part"/>: the line, a tick at each of <paramref name="ticks"/>, and each tick's words as a piece of
    /// <paramref name="tickKind"/> — beside the tick on the side <paramref name="after"/> says: under or right of the line
    /// where it is true, over or left of it where it is false.
    /// </summary>
    /// <param name="line">Whether the line itself is drawn, or only its ticks and words.</param>
    /// <param name="tick">How long the ticks are drawn — nought for none, the words then sitting against the line.</param>
    public static void Draw(LayoutBuilder build, string kind, ISourcePart? part, Point from, Point to, IReadOnlyList<DiagramTick> ticks,
                            DiagramStroke stroke, string tickKind, bool after = true, bool line = true, double tick = TickLength)
    {
        var along = to - from;
        var upright = Math.Abs(along.Y) > Math.Abs(along.X);

        // The side the ticks and words go, square to the line.
        var outward = upright ? new Vector(after ? 1 : -1, 0) : new Vector(0, after ? 1 : -1);

        var lines = new GeometryGroup();
        if (line) lines.Children.Add(new LineGeometry(from, to));

        if (tick > 0)
            foreach (var mark in ticks)
            {
                var at = from + (along * mark.At);
                lines.Children.Add(new LineGeometry(at, at + (outward * tick)));
            }

        lines.Freeze();

        build.Open(kind, part, stops: Stops.None);

        // The line and its ticks are a leaf of their own, which is what a press near them lands on — only what draws is pressed.
        if (lines.Children.Count > 0)
        {
            build.Open(MermaidPiece.Line, part, stops: Stops.None);
            build.Draw(new GeometryMark(lines, null, stroke.Ink, stroke.Thickness) { Dashes = stroke.Dashes });
            build.Occupies(lines.GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(DiagramConnector.Reach, stroke.Thickness))));
            build.Close();
        }

        foreach (var mark in ticks)
        {
            if (mark.Words is not { } words) continue;

            var at = from + (along * mark.At) + (outward * (tick + Gap));
            var place = upright
                ? new Point(after ? at.X : at.X - words.Width, at.Y - (words.Height / 2))
                : new Point(at.X - (words.Width / 2), after ? at.Y : at.Y - words.Height);

            words.Set(build, place, tickKind);
        }

        build.Close();
    }
}
