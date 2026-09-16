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
