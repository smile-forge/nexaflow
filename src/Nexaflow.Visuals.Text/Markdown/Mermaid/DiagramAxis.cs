using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>A mark along an axis: how far along it, from nought at its start to one at its end, and the words beside it, where it has any.</summary>
internal sealed record DiagramTick(double At, DiagramWords? Words);

/// <summary>The lines across a panel at each of an axis's ticks. A separate piece from the axis's own
/// ticks (which are drawn outward, into the margin) — these stand for nothing anybody wrote.</summary>
internal static class DiagramGrid
{
    /// <summary>The lines across <paramref name="panel"/> at each of <paramref name="ticks"/>, added to a
    /// shape of their own so several sets draw as one.</summary>
    /// <param name="upright">Whether the ticks belong to the axis running up the page, whose lines
    /// therefore run across.</param>
    public static void Lines(GeometryGroup into, Rect panel, IReadOnlyList<DiagramTick> ticks, bool upright)
    {
        foreach (var mark in ticks)
            into.Children.Add(upright
                ? new LineGeometry(new Point(panel.Left, panel.Bottom - (mark.At * panel.Height)),
                                   new Point(panel.Right, panel.Bottom - (mark.At * panel.Height)))
                : new LineGeometry(new Point(panel.Left + (mark.At * panel.Width), panel.Top),
                                   new Point(panel.Left + (mark.At * panel.Width), panel.Bottom)));
    }

    /// <summary>Draws a line across <paramref name="panel"/> at each of <paramref name="ticks"/>.</summary>
    /// <param name="upright">Whether the ticks belong to the axis running up the page, whose lines
    /// therefore run across.</param>
    public static void Draw(LayoutBuilder build, string kind, Rect panel, IReadOnlyList<DiagramTick> ticks,
                            bool upright, DiagramStroke stroke)
    {
        if (ticks.Count == 0) return;

        var lines = new GeometryGroup();
        Lines(lines, panel, ticks, upright);
        lines.Freeze();

        // Nothing wrote a gridline, so it stands for nothing and is nowhere to put a caret.
        build.Open(kind, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(lines, null, stroke.Ink, stroke.Thickness) { Dashes = stroke.Dashes });
        build.Close();
    }
}

/// <summary>An axis: a line, a tick at each mark along it, and the words beside each tick — a chart's
/// values up its side, its categories along its foot, a timeline's dates. The axis is one piece
/// standing for what it was written as (an <c>x-axis</c> line), so pressing the line means that line.</summary>
internal static class DiagramAxis
{
    /// <summary>How long a tick is, and the clear air between it and its words.</summary>
    public const double TickLength = 5;
    public const double Gap = 4;

    /// <summary>How far past the line an axis's ticks and words reach — the room to leave for them beside a chart.</summary>
    /// <param name="upright">Whether the axis runs up the page, so its words' width is the room they take.</param>
    /// <param name="tick">How long its ticks are drawn — nought for none.</param>
    public static double Room(IReadOnlyList<DiagramTick> ticks, bool upright, double tick = TickLength) =>
        tick + Gap + ticks.Select(mark => mark.Words is null ? 0 : upright ? mark.Words.Width : mark.Words.Height).DefaultIfEmpty(0).Max();

    /// <summary>Draws an axis from <paramref name="from"/> to <paramref name="to"/>: the line, a tick at
    /// each of <paramref name="ticks"/>, and each tick's words — beside the tick on the side
    /// <paramref name="after"/> says (under/right if true, over/left if false).</summary>
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
