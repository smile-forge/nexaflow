using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt;

/// <summary>The pieces a gantt chart's layout is made of — its layers, and what is in them.</summary>
public static class GanttPiece
{
    /// <summary>The bands behind the days <c>excludes</c> leaves out.</summary>
    public const string Excluded = "Excluded";

    /// <summary>The band behind each row, tinted by its section.</summary>
    public const string Rows = "Rows";

    /// <summary>The lines up the chart at each date, and the dates under it — and over it, with <c>topAxis</c>.</summary>
    public const string Grid = "Grid";
    public const string Axis = "Axis";
    public const string Date = "Date";

    /// <summary>What is written beside the bars: each section's name, a task's name that does not fit its bar, a marker's name.</summary>
    public const string Words = "Words";
    public const string SectionName = "SectionName";
    public const string Label = "Label";

    /// <summary>The tasks: a bar, a milestone's diamond or a vertical marker, each standing for its task as written.</summary>
    public const string Tasks = "Tasks";
    public const string Task = "Task";

    /// <summary>The line at today's date.</summary>
    public const string Today = "Today";
}

/// <summary>
/// Draws a <c>gantt</c> block as Mermaid lays one out: a row for each task, banded by its section with the section's name at the
/// left; a bar from each task's start to its end, its name in it where it fits and beside it where it does not; a diamond for a
/// milestone half way through its time; a line down the chart for a vertical marker; dates along the foot, lines up the chart at
/// each; bands behind excluded days; and a line at today.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A bar, a diamond or a marker stands for its task's line, and
/// the names are the characters written — a task's, a section's — typed into where they are drawn. How wide the chart is, is the
/// room it has; its paddings, bar sizes and fonts are Mermaid's unless the front matter says otherwise.
/// </para>
/// </summary>
internal sealed class GanttBuilder : MermaidBuilder<GanttChart>
{
    /// <summary>How wide a chart is drawn where nothing bounds its room.</summary>
    private const double Wide = 800;

    /// <summary>The least room the dates are given between the paddings.</summary>
    private const double Narrowest = 120;

    private const double DateSize = 10;
    private const double MarkerSize = 15;

    private GanttBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new GanttBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override GanttChart Of(MermaidBlock block) => GanttChart.Of(block);

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    protected override string? TitleColour => Diagram?.Config.TitleColour;

    protected override Size Draw(GanttChart chart, LayoutBuilder build)
    {
        // A chart with no task worked out is the source.
        if (chart.Tasks.Count == 0) return AsWritten(build);

        var c = chart.Config;
        var bar = c.BarHeight ?? 20;
        var gap = bar + (c.BarGap ?? 4);
        var top = c.TopPadding ?? 50;
        var left = c.LeftPadding ?? 75;
        var right = c.RightPadding ?? 75;
        var gridStart = c.GridLineStartPadding ?? 35;
        var titleTop = c.TitleTopMargin ?? 25;
        var styles = c.NumberSectionStyles ?? 4;

        var width = Math.Max(c.UseWidth ?? (double.IsInfinity(Space) ? Wide : Space), left + right + Narrowest);
        var span = width - left - right;

        var rowed = chart.Tasks.Where(task => !task.Vert).ToList();
        var rows = rowed.Count == 0 ? 0 : rowed.Max(task => task.Order) + 1;
        var height = (2 * top) + (rows * gap);

        var (first, last) = (chart.Tasks.Min(task => task.Start), chart.Tasks.Max(task => task.End));
        double X(DateTime date) => Math.Round(last > first ? DiagramTime.At(date, first, last) * span : span / 2);

        var text = Ink.Written(c.TextColour) ?? Palette.TextMuted;
        var words = new List<(DiagramWords Words, Point At, string Kind)>();

        // Each section's name at the left, in the middle of its rows.
        foreach (var section in chart.Sections)
        {
            var own = rowed.Where(task => task.Section == section).ToList();
            var (from, to) = (own.Min(task => task.Order), own.Max(task => task.Order) + 1);
            var beside = Math.Max(40, left - 20);
            var lines = Wrapped(section.Name, section.Hole, c.SectionFontSize ?? 11, Ink.Written(c.TitleColour) ?? Palette.Text, beside);
            var tall = lines.Sum(line => line.Height);
            var aside = new Rect(10, top + ((from + to) * gap / 2) - (tall / 2), beside, tall);

            words.AddRange(DiagramWords.Placed(lines, aside, GanttPiece.SectionName, TextAlignment.Left));
        }

        // Each task: its bar, diamond or marker, and its name in the bar where it fits, beside it where it does not.
        var shapes = new List<(GanttTask Task, DiagramShape Shape, Rect Bounds, Brush Fill, DiagramStroke? Stroke, DiagramWords? Words)>();
        foreach (var task in chart.Tasks.OrderBy(task => task.Vert).ThenBy(task => task.Start))
        {
            var y = (task.Order * gap) + top;
            var (from, to) = (X(task.Start), X(task.Shown));
            var (fill, stroke, inside) = Inks(task, c);
            var name = Written(task.Name, task.Hole, task.Vert ? MarkerSize : c.FontSize ?? 11,
                               task.Vert ? Marker(c) : task.Clickable ? Ink.Written(c.TaskTextClickable) ?? Palette.Accent : inside,
                               task.Clickable ? FontWeights.Bold : null, task.Milestone ? FontStyles.Italic : null);

            if (task.Vert)
            {
                var x = X(task.Start) + left;
                shapes.Add((task, DiagramShape.Rectangle, new Rect(x, gridStart, Math.Max(1.5, 0.08 * bar), (rows * gap) + (2 * bar)), Marker(c), null, null));
                words.Add((name, new Point(x - (name.Width / 2), gridStart + (rows * gap) + 60 - name.Baseline), GanttPiece.Label));
                continue;
            }

            if (task.Milestone)
            {
                var middle = from + (0.5 * (X(task.End) - from));
                var side = bar * 0.8 * Math.Sqrt(2);
                shapes.Add((task, DiagramShape.Diamond, new Rect(middle + left - (side / 2), y + (bar / 2) - (side / 2), side, side), fill, stroke, null));
                (from, to) = (middle - (bar / 2), middle + (bar / 2));
            }

            if (name.Width <= to - from && !task.Milestone)
            {
                shapes.Add((task, DiagramShape.Rounded, new Rect(from + left, y, Math.Max(1, to - from), bar), fill, stroke, name));
                continue;
            }

            if (!task.Milestone)
                shapes.Add((task, DiagramShape.Rounded, new Rect(from + left, y, Math.Max(1, to - from), bar), fill, stroke, null));

            if (!task.Clickable)
                name = Written(task.Name, task.Hole, c.FontSize ?? 11, Ink.Written(c.TaskTextOutside) ?? Palette.Text, null,
                               task.Milestone ? FontStyles.Italic : null);
            var beside = to + name.Width + (1.5 * left) > width ? from + left - 5 - name.Width : to + left + 5;
            words.Add((name, new Point(beside, y + (bar / 2) - (name.Height / 2)), GanttPiece.Label));
        }

        // The dates: along the foot, and over the top where the chart asks.
        // As many dates as have room to be read, where the chart does not say how far apart they are.
        var sample = Worked(MermaidTimeFormat.Write(last, chart.AxisFormat), null, DateSize, text).Width + (4 * DiagramAxis.Gap);
        var room = (int)Math.Max(2, Math.Floor(span / sample) + 1);
        var marks = Ticks(chart, first, last, Math.Min(10, room));
        for (var count = Math.Min(10, room) - 1; marks.Count > room && count >= 1 && chart.TickInterval is null; count--)
            marks = Ticks(chart, first, last, count);

        var ticks = marks
            .Select(date => new DiagramTick(span > 0 ? X(date) / span : 0, Worked(MermaidTimeFormat.Write(date, chart.AxisFormat), null, DateSize, text)))
            .ToList();

        // Words reach past the chart's edges: everything moves over so they are not cut off.
        // The chart reaches from where its grid starts to under its dates; Mermaid's room over the grid is the title's, set above it.
        var foot = height - 50;
        var taken = new DiagramRoom();
        taken.Reach(new Rect(0, Math.Min(top + gridStart - 50, top - 2), width, 0));
        taken.Reach(new Rect(0, foot, width, DiagramAxis.Room(ticks, upright: false, tick: 0)));
        if (chart.TopAxis) taken.Reach(new Rect(0, top - DiagramAxis.Room(ticks, upright: false, tick: 0), width, 0));
        foreach (var (said, at, _) in words) taken.Reach(said, at);
        foreach (var shape in shapes) taken.Reach(shape.Bounds);
        var shift = taken.Shift;

        Excluded(build, chart, first, last, X, left, gridStart, height - top - gridStart, shift);
        Rows(build, chart, rowed, width - (right / 2), gap, top, styles, shift);
        Grid(build, chart, ticks, span, left, foot, top, height, gridStart, shift);

        build.Open(GanttPiece.Words, part: null, stops: Stops.None);
        foreach (var (said, at, kind) in words) said.Set(build, at + shift, kind);
        build.Close();

        build.Open(GanttPiece.Tasks, part: null, stops: Stops.None);
        foreach (var (task, shape, bounds, fill, stroke, name) in shapes)
            DiagramShapes.Draw(build, GanttPiece.Task, task.Part, shape, Rect.Offset(bounds, shift), fill, stroke, name, GanttPiece.Label);
        build.Close();

        Today(build, chart, first, last, X(DateTime.Now) + left, Math.Max(titleTop, taken.Reached.Top), Math.Min(height - titleTop, taken.Reached.Bottom), shift);

        return taken.Size;
    }

    /// <summary>The dates the axis marks: every so often as <c>tickInterval</c> says, where it says so sensibly, or else about <paramref name="count"/> on round boundaries.</summary>
    private static IReadOnlyList<DateTime> Ticks(GanttChart chart, DateTime first, DateTime last, int count)
    {
        if (chart.TickInterval is { } interval)
        {
            var digits = interval.TakeWhile(char.IsAsciiDigit).Count();
            if (digits > 0 && int.TryParse(interval.AsSpan(0, digits), out var every)
                && DiagramTime.Every(first, last, every, interval[digits..], chart.Weekday) is { } marks)
                return marks;
        }

        return DiagramTime.Ticks(first, last, count);
    }

    /// <summary>A task's fill, outline and the ink of a name set in it, by whether it is active, done or critical.</summary>
    private (Brush Fill, DiagramStroke Stroke, Brush Text) Inks(GanttTask task, GanttConfig c)
    {
        var critical = Ink.Written(c.CritBorder) ?? Palette.Danger;
        var dark = Ink.Written(c.TaskTextDark) ?? Palette.Text;

        var (fill, border, text) = task switch
        {
            { Active: true } => (Ink.Written(c.ActiveTaskBackground) ?? DiagramInk.Faded(Palette.Accent, 0.25), task.Critical ? critical : Ink.Written(c.ActiveTaskBorder) ?? Palette.Accent, dark),
            { Done: true } => (Ink.Written(c.DoneTaskBackground) ?? DiagramInk.Faded(Palette.TextMuted, 0.35), task.Critical ? critical : Ink.Written(c.DoneTaskBorder) ?? Palette.TextMuted, dark),
            { Critical: true } => (Ink.Written(c.CritBackground) ?? DiagramInk.Faded(Palette.Danger, 0.6), critical, dark),
            _ => (Ink.Written(c.TaskBackground) ?? DiagramInk.Faded(Palette.Accent, 0.6), Ink.Written(c.TaskBorder) ?? Palette.Accent, Ink.Written(c.TaskText) ?? Palette.Text),
        };

        return (fill, new DiagramStroke(border, 2), text);
    }

    private Brush Marker(GanttConfig c) => Ink.Written(c.VertLine) ?? Palette.Important;

    private void Excluded(LayoutBuilder build, GanttChart chart, DateTime first, DateTime last, Func<DateTime, double> x, double left, double top, double tall, Vector shift)
    {
        if (chart.Excludes.Count == 0 && chart.Includes.Count == 0 || last > first.AddYears(5)) return;

        var bands = new GeometryGroup();
        DateTime? from = null, to = null;
        for (var day = first; day <= last.AddDays(1); day = day.AddDays(1))
        {
            if (day <= last && chart.Excluded(day))
            {
                from ??= day;
                to = day;
                continue;
            }

            if (from is { } start && to is { } end)
                bands.Children.Add(new RectangleGeometry(Rect.Offset(new Rect(x(start.Date) + left, top, Math.Max(0, x(end.Date.AddDays(1)) - x(start.Date)), tall), shift)));
            (from, to) = (null, null);
        }

        if (bands.Children.Count == 0) return;
        bands.Freeze();

        build.Open(GanttPiece.Excluded, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(bands, Ink.Written(chart.Config.ExcludeBackground) ?? DiagramInk.Faded(Palette.TextMuted, 0.15), null, 0));
        build.Close();
    }

    private void Rows(LayoutBuilder build, GanttChart chart, IReadOnlyList<GanttTask> rowed, double wide, double gap, double top, int styles, Vector shift)
    {
        var c = chart.Config;
        build.Open(GanttPiece.Rows, part: null, stops: Stops.None);

        foreach (var row in rowed.GroupBy(task => task.Order).Select(group => group.First()))
        {
            var band = new RectangleGeometry(Rect.Offset(new Rect(0, (row.Order * gap) + top - 2, wide, gap), shift));
            band.Freeze();

            var style = Math.Max(0, row.Section is null ? 0 : chart.Sections.ToList().IndexOf(row.Section)) % styles;
            var fill = style switch
            {
                0 => Ink.Written(c.SectionBackground) is { } written ? DiagramInk.Faded(written, 0.2) : DiagramInk.Faded(Ink.Series(0), 0.12),
                2 => Ink.Written(c.SectionBackground2) is { } written ? DiagramInk.Faded(written, 0.2) : DiagramInk.Faded(Ink.Series(2), 0.12),
                _ => Ink.Written(c.AltSectionBackground) is { } written ? DiagramInk.Faded(written, 0.2) : DiagramInk.Faded(Palette.TextMuted, 0.06),
            };

            build.Draw(new GeometryMark(band, fill, null, 0));
        }

        build.Close();
    }

    private void Grid(LayoutBuilder build, GanttChart chart, IReadOnlyList<DiagramTick> ticks, double span, double left, double foot, double top, double height, double gridStart, Vector shift)
    {
        var lines = new GeometryGroup();
        foreach (var tick in ticks)
        {
            var x = left + (tick.At * span) + shift.X;
            lines.Children.Add(new LineGeometry(new Point(x, top + gridStart - 50 + shift.Y), new Point(x, foot + shift.Y)));
            if (chart.TopAxis) lines.Children.Add(new LineGeometry(new Point(x, top + shift.Y), new Point(x, height - gridStart + shift.Y)));
        }

        lines.Freeze();

        build.Open(GanttPiece.Grid, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(lines, null, DiagramInk.Faded(Ink.Written(chart.Config.Grid) ?? Palette.CodeBorder, 0.8), 1));

        var stroke = new DiagramStroke(Palette.CodeBorder);
        DiagramAxis.Draw(build, GanttPiece.Axis, null, new Point(left, foot) + shift, new Point(left + span, foot) + shift, ticks, stroke, GanttPiece.Date, after: true, line: false, tick: 0);
        if (chart.TopAxis)
            DiagramAxis.Draw(build, GanttPiece.Axis, null, new Point(left, top) + shift, new Point(left + span, top) + shift, ticks, stroke, GanttPiece.Date, after: false, line: false, tick: 0);

        build.Close();
    }

    /// <summary>The line at today, where today is on the chart and <c>todayMarker</c> does not turn it off — styled as it says.</summary>
    private void Today(LayoutBuilder build, GanttChart chart, DateTime first, DateTime last, double x, double from, double to, Vector shift)
    {
        var now = DateTime.Now;
        if (string.Equals(chart.TodayMarker, "off", StringComparison.OrdinalIgnoreCase) || now < first || now > last) return;

        var ink = Ink.Written(chart.Config.TodayLine) ?? Palette.Danger;
        var thickness = 2d;
        foreach (var style in (chart.TodayMarker ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var (key, value) = style.Split(':', 2) is [var k, var v] ? (k.Trim().ToLowerInvariant(), v.Trim()) : (string.Empty, string.Empty);
            switch (key)
            {
                case "stroke": ink = Ink.Written(value) ?? ink; break;
                case "stroke-width": thickness = MermaidNumber.Pixels(value) ?? thickness; break;
                case "opacity" when MermaidNumber.Read(value) is { } opacity: ink = DiagramInk.Faded(ink, Math.Clamp(opacity, 0, 1)); break;
            }
        }

        var line = new LineGeometry(new Point(x, from) + shift, new Point(x, to) + shift);
        line.Freeze();

        build.Open(GanttPiece.Today, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(line, null, ink, thickness));
        build.Close();
    }
}
