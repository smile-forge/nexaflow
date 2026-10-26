using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Gantt;

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
internal sealed class GanttBuilder : ContentBuilder
{
    /// <summary>How wide a chart is drawn where nothing bounds its room.</summary>
    private const double Wide = 800;

    /// <summary>The least room the dates are given between the paddings.</summary>
    private const double Narrowest = 120;

    private const double DateSize = 10;
    private const double MarkerSize = 15;

    /// <summary>How tall a bar is and the room between one row's and the next, where the front matter does not say: slimmer than Mermaid's, as the bars were drawn before.</summary>
    private const double BarHeight = 16;
    private const double BarGap = 10;

    /// <summary>The room between a section's name and the dates, round the name, and how wide the name runs before it wraps.</summary>
    private const double Beside = 12;
    private const double Pad = 10;
    private const double Widest = 200;

    /// <summary>How faint the line along a row is drawn, and its dashes.</summary>
    private const double RowWash = 0.35;
    private static readonly double[] Dotted = [3, 3];


    internal GanttBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                  LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private DiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private DiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), Ink.Written(TitleColour), null);
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    /// <summary>The chart as its stages left it on the block.</summary>
    private GanttBlockNode? Chart => Reading.Root.Node as GanttBlockNode;

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    private string? TitleColour => Chart?.Config.TitleColour;

    /// <summary>A section, and the name it is written with — every section written with the same name the one section.</summary>
    private sealed record Section(string Says, ContentPart? Name, ContentPart? Hole);

    /// <summary>
    /// A task drawn: the part of the reading it is, when its stages said it runs, its name, its section, its tags, and its row —
    /// the order it is written in among the tasks taking one, or the row it shares in compact mode.
    /// </summary>
    private sealed record Task(ContentPart Part, GanttTaskNode Said, ContentPart? Name, ContentPart? Hole, Section? Section,
                               bool Active, bool Done, bool Critical, bool Milestone, bool Vert, int Order)
    {
        public DateTime Start => Said.Start;
        public DateTime End => Said.End;
        public DateTime Shown => Said.DrawnTo;
        public bool Clickable => Said.Clickable;
    }

    /// <summary>
    /// The tasks its stages gave a start and an end, in the order they are written, each under the section written last above
    /// it — and the sections holding a task that takes a row, which are the only ones given a place.
    /// </summary>
    private static (List<Task> Tasks, List<Section> Sections) Read(ContentPart root, bool compact)
    {
        var sections = new List<Section>();
        var named = new Dictionary<string, Section>(StringComparer.Ordinal);
        var tasks = new List<Task>();
        Section? section = null;
        var order = 0;

        foreach (var part in root.SelfAndDescendants())
        {
            switch (part.Node)
            {
                case { Kind: GanttKinds.Section }:
                    var name = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Text);
                    var says = name.Words()?.Text ?? string.Empty;
                    if (!named.TryGetValue(says, out section))
                    {
                        section = new Section(says, name.Words(), name.Hole());
                        named[says] = section;
                        sections.Add(section);
                    }

                    break;

                case GanttTaskNode said:
                    var text = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Text);
                    var tags = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Schedule)?.Children
                                   .Where(child => child.Kind == GanttKinds.Tag).Select(child => child.Text).ToHashSet(StringComparer.Ordinal) ?? [];
                    var vert = tags.Contains("vert");

                    tasks.Add(new Task(part, said, text.Words() is { Length: > 0 } words ? words : null, text.Hole(), section,
                                       tags.Contains("active"), tags.Contains("done"), tags.Contains("crit"), tags.Contains("milestone"), vert,
                                       vert ? -1 : order++));
                    break;
            }
        }

        if (compact) tasks = Compacted(tasks);

        // A section keeps its place only where a task in it takes a row.
        var rowed = tasks.Where(task => !task.Vert && task.Section is not null).Select(task => task.Section!).ToHashSet();
        return (tasks, [.. sections.Where(rowed.Contains)]);
    }

    /// <summary>Tasks sharing rows, as compact mode draws them: each section's tasks by start, each in the first row it fits.</summary>
    private static List<Task> Compacted(List<Task> tasks)
    {
        var placed = tasks.ToDictionary(task => task, task => task.Order);
        var offset = 0;

        foreach (var group in tasks.Where(task => !task.Vert).GroupBy(task => task.Section))
        {
            var rows = new List<DateTime>();
            foreach (var task in group.OrderBy(task => task.Start).ThenBy(task => task.Order))
            {
                var row = rows.FindIndex(ends => task.Start >= ends);
                if (row < 0) { row = rows.Count; rows.Add(task.End); }
                else rows[row] = task.End;
                placed[task] = offset + row;
            }

            offset += Math.Max(1, rows.Count);
        }

        return [.. tasks.Select(task => task with { Order = placed[task] })];
    }

    private LayoutSize Draw(MermaidBlock block, LayoutBuilder build)
    {
        // A chart with no task its stages gave a start and an end is the source.
        if (Chart is not { } chart) return Sheet.AsWritten();

        var c = chart.Config;
        var (tasks, sections) = Read(Reading.Root, c.Compact);
        if (tasks.Count == 0) return Sheet.AsWritten();

        var bar = c.BarHeight ?? BarHeight;
        var gap = bar + (c.BarGap ?? BarGap);
        var top = c.TopPadding ?? 50;
        var right = c.RightPadding ?? 75;
        var gridStart = c.GridLineStartPadding ?? 35;
        var titleTop = c.TitleTopMargin ?? 25;
        var styles = c.NumberSectionStyles ?? 4;

        // Each section's name first: the room left of the dates is as wide as the widest needs, where nothing says how wide.
        var names = sections.ToDictionary(section => section, section =>
            Words.Wrapped(section.Name, section.Hole, c.SectionFontSize ?? 11, Ink.Written(c.TitleColour) ?? Style.Text, Widest));
        var left = c.LeftPadding ?? Math.Max(75, names.Values.Select(lines => lines.Max(line => line.Width) + Beside + Pad).DefaultIfEmpty(0).Max());

        var width = Math.Max(c.UseWidth ?? (double.IsInfinity(Sheet.Space) ? Wide : Sheet.Space), left + right + Narrowest);
        var span = width - left - right;

        var rowed = tasks.Where(task => !task.Vert).ToList();
        var rows = rowed.Count == 0 ? 0 : rowed.Max(task => task.Order) + 1;
        var height = (2 * top) + (rows * gap);

        var (first, last) = (tasks.Min(task => task.Start), tasks.Max(task => task.End));
        double X(DateTime date) => Math.Round(last > first ? DiagramTime.At(date, first, last) * span : span / 2);

        var text = Ink.Written(c.TextColour) ?? Style.TextMuted;
        var words = new List<(DiagramWords Words, LayoutPoint At, string Kind)>();

        // The rows each section takes, found in one pass over the rows rather than one for each section.
        var taken = new Dictionary<Section, (int From, int To)>();
        foreach (var task in rowed)
            if (task.Section is { } holding)
                taken[holding] = taken.TryGetValue(holding, out var had)
                    ? (Math.Min(had.From, task.Order), Math.Max(had.To, task.Order + 1))
                    : (task.Order, task.Order + 1);

        // Each section's name set right against the dates, in the middle of its rows.
        foreach (var section in sections)
        {
            if (!taken.TryGetValue(section, out var their)) continue;

            var (from, to) = their;
            var lines = names[section];
            var tall = lines.Sum(line => line.Height);
            var middle = top + (((from + to) * gap) - (gap - bar)) / 2;
            var aside = new LayoutRect(Pad, middle - (tall / 2), Math.Max(1, left - Beside - Pad), tall);

            words.AddRange(DiagramWords.Placed(lines, aside, GanttPiece.SectionName, LayoutAlign.Right));
        }

        // Each task: its bar, diamond or marker, and its name in the bar where it fits, beside it where it does not.
        var shapes = new List<(Task Task, DiagramShape Shape, LayoutRect Bounds, LayoutInk Fill, DiagramStroke? Stroke, DiagramWords? Words)>();
        var (marker, clickable, outside) = (Marker(c), Ink.Written(c.TaskTextClickable) ?? Style.Accent, Ink.Written(c.TaskTextOutside) ?? Style.Text);
        foreach (var task in tasks.OrderBy(task => task.Vert).ThenBy(task => task.Start))
        {
            var y = (task.Order * gap) + top;
            var (from, to) = (X(task.Start), X(task.Shown));
            var (fill, stroke, inside) = Inks(task, c);
            var name = Words.Written(task.Name, task.Hole, task.Vert ? MarkerSize : c.FontSize ?? 11,
                                     task.Vert ? marker : task.Clickable ? clickable : inside,
                                     task.Clickable ? 700 : null, task.Milestone ? true : null);

            if (task.Vert)
            {
                var x = X(task.Start) + left;
                shapes.Add((task, DiagramShape.Rectangle, new LayoutRect(x, gridStart, Math.Max(1.5, 0.08 * bar), (rows * gap) + (2 * bar)), marker, null, null));
                words.Add((name, new LayoutPoint(x - (name.Width / 2), gridStart + (rows * gap) + 60 - name.Baseline), GanttPiece.Label));
                continue;
            }

            if (task.Milestone)
            {
                var middle = from + (0.5 * (X(task.End) - from));
                var side = bar * 0.8 * Math.Sqrt(2);
                shapes.Add((task, DiagramShape.Diamond, new LayoutRect(middle + left - (side / 2), y + (bar / 2) - (side / 2), side, side), fill, stroke, null));
                (from, to) = (middle - (bar / 2), middle + (bar / 2));
            }

            if (name.Width <= to - from && !task.Milestone)
            {
                shapes.Add((task, DiagramShape.Rounded, new LayoutRect(from + left, y, Math.Max(1, to - from), bar), fill, stroke, name));
                continue;
            }

            if (!task.Milestone)
                shapes.Add((task, DiagramShape.Rounded, new LayoutRect(from + left, y, Math.Max(1, to - from), bar), fill, stroke, null));

            // Beside the bar it is on the page rather than on the bar, so it takes the page's ink — the same words, set once.
            if (!task.Clickable) name = name.In(outside);
            var beside = to + name.Width + (1.5 * left) > width ? from + left - 5 - name.Width : to + left + 5;
            words.Add((name, new LayoutPoint(beside, y + (bar / 2) - (name.Height / 2)), GanttPiece.Label));
        }

        // The dates: along the foot, and over the top where the chart asks.
        // As many dates as have room to be read, where the chart does not say how far apart they are.
        var sample = Words.Worked(MermaidTimeFormat.Write(last, chart.AxisFormat), null, DateSize, text).Width + (4 * DiagramAxis.Gap);
        var room = (int)Math.Max(2, Math.Floor(span / sample) + 1);
        var marks = Ticks(chart, first, last, Math.Min(10, room));
        for (var count = Math.Min(10, room) - 1; marks.Count > room && count >= 1 && chart.Tick is null; count--)
            marks = Ticks(chart, first, last, count);

        var ticks = marks
            .Select(date => new DiagramTick(span > 0 ? X(date) / span : 0, Words.Worked(MermaidTimeFormat.Write(date, chart.AxisFormat), null, DateSize, text)))
            .ToList();

        // Words reach past the chart's edges: everything moves over so they are not cut off.
        // The chart reaches from where its grid starts to under its dates; Mermaid's room over the grid is the title's, set above it.
        var foot = height - 50;
        var reached = new DiagramRoom();
        reached.Reach(new LayoutRect(0, Math.Min(top + gridStart - 50, top - 2), width, 0));
        reached.Reach(new LayoutRect(0, foot, width, DiagramAxis.Room(ticks, upright: false, tick: 0)));
        if (chart.TopAxis) reached.Reach(new LayoutRect(0, top - DiagramAxis.Room(ticks, upright: false, tick: 0), width, 0));
        foreach (var (said, at, _) in words) reached.Reach(said, at);
        foreach (var shape in shapes) reached.Reach(shape.Bounds);
        var shift = reached.Shift;

        Excluded(build, chart, first, last, X, left, gridStart, height - top - gridStart, shift);
        Rows(build, chart, sections, rowed, (left, left + span, width - (right / 2)), bar, gap, top, styles, shift);
        Grid(build, chart, ticks, span, left, foot, top, height, gridStart, shift);

        build.Open(GanttPiece.Words, part: null, stops: LayoutStops.None);
        foreach (var (said, at, kind) in words) said.Set(build, at + shift, kind);
        build.Close();

        build.Open(GanttPiece.Tasks, part: null, stops: LayoutStops.None);
        foreach (var (task, shape, bounds, fill, stroke, name) in shapes)
            DiagramShapes.Draw(build, GanttPiece.Task, task.Part, shape, LayoutRect.Offset(bounds, shift), fill, stroke, name, GanttPiece.Label);
        build.Close();

        Today(build, chart, first, last, X(chart.Now) + left, Math.Max(titleTop, reached.Reached.Top), Math.Min(height - titleTop, reached.Reached.Bottom), shift);

        return reached.Size;
    }

    /// <summary>The dates the axis marks: every so often as <c>tickInterval</c> says, or else about <paramref name="count"/> on round boundaries.</summary>
    private static IReadOnlyList<DateTime> Ticks(GanttBlockNode chart, DateTime first, DateTime last, int count) =>
        chart.Tick is { } tick && DiagramTime.Every(first, last, tick.Every, tick.Unit, chart.Weekday) is { } marks
            ? marks
            : DiagramTime.Ticks(first, last, count);

    /// <summary>
    /// A task's fill, outline and the ink of a name set in it, by whether it is active, done or critical — worked out once for
    /// each of those a chart has rather than once for every task, since each is colours read from the front matter.
    /// </summary>
    private (LayoutInk Fill, DiagramStroke Stroke, LayoutInk Text) Inks(Task task, GanttConfig c)
    {
        var kind = (task.Active, task.Done, task.Critical);
        if (_inks.TryGetValue(kind, out var known)) return known;

        var critical = Ink.Written(c.CritBorder) ?? Style.Danger;
        var dark = Ink.Written(c.TaskTextDark) ?? Style.Text;

        var (fill, border, text) = task switch
        {
            { Active: true } => (Ink.Written(c.ActiveTaskBackground) ?? DiagramInk.Faded(Medium, Style.Accent, 0.25), task.Critical ? critical : Ink.Written(c.ActiveTaskBorder) ?? Style.Accent, dark),
            { Done: true } => (Ink.Written(c.DoneTaskBackground) ?? DiagramInk.Faded(Medium, Style.TextMuted, 0.35), task.Critical ? critical : Ink.Written(c.DoneTaskBorder) ?? Style.TextMuted, dark),
            { Critical: true } => (Ink.Written(c.CritBackground) ?? DiagramInk.Faded(Medium, Style.Danger, 0.6), critical, dark),
            _ => (Ink.Written(c.TaskBackground) ?? DiagramInk.Faded(Medium, Style.Accent, 0.6), Ink.Written(c.TaskBorder) ?? Style.Accent, Ink.Written(c.TaskText) ?? Style.Text),
        };

        return _inks[kind] = (fill, new DiagramStroke(border, task.Active || task.Critical ? 1.5 : 1), text);
    }

    private readonly Dictionary<(bool Active, bool Done, bool Critical), (LayoutInk Fill, DiagramStroke Stroke, LayoutInk Text)> _inks = [];

    private LayoutInk Marker(GanttConfig c) => Ink.Written(c.VertLine) ?? Style.Important;

    private void Excluded(LayoutBuilder build, GanttBlockNode chart, DateTime first, DateTime last, Func<DateTime, double> x, double left, double top, double tall, LayoutVector shift)
    {
        if (!chart.Days.Any || last > first.AddYears(5)) return;

        var bands = new List<LayoutShape>();
        DateTime? from = null, to = null;
        for (var day = first; day <= last.AddDays(1); day = day.AddDays(1))
        {
            if (day <= last && chart.Days.Excluded(day))
            {
                from ??= day;
                to = day;
                continue;
            }

            if (from is { } start && to is { } end)
                bands.Add(build.Medium.Rectangle(LayoutRect.Offset(new LayoutRect(x(start.Date) + left, top, Math.Max(0, x(end.Date.AddDays(1)) - x(start.Date)), tall), shift)));
            (from, to) = (null, null);
        }

        if (bands.Count == 0) return;

        build.Open(GanttPiece.Excluded, part: null, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(build.Medium, build.Medium.Group(bands), Ink.Written(chart.Config.ExcludeBackground) ?? DiagramInk.Faded(build.Medium, Style.TextMuted, 0.15), null, 0));
        build.Close();
    }

    /// <summary>
    /// The band behind each row, tinted by its section, with the bar centred in it — and a faint line along the row from where
    /// the dates start to where they end, which its bars sit on.
    /// </summary>
    private void Rows(LayoutBuilder build, GanttBlockNode chart, IReadOnlyList<Section> sections, IReadOnlyList<Task> rowed, (double From, double To, double Wide) across,
                      double bar, double gap, double top, int styles, LayoutVector shift)
    {
        var c = chart.Config;
        var lines = new List<LayoutShape>();
        build.Open(GanttPiece.Rows, part: null, stops: LayoutStops.None);

        // Which of the section styles each section takes, and the three tints they come to — each worked out once.
        var placed = new Dictionary<Section, int>();
        foreach (var section in sections) placed.TryAdd(section, placed.Count);

        var tints = new LayoutInk[]
        {
            Ink.Written(c.SectionBackground) is { } first ? DiagramInk.Faded(build.Medium, first, 0.2) : DiagramInk.Faded(build.Medium, Ink.Series(0), 0.12),
            Ink.Written(c.AltSectionBackground) is { } other ? DiagramInk.Faded(build.Medium, other, 0.2) : DiagramInk.Faded(build.Medium, Style.TextMuted, 0.06),
            Ink.Written(c.SectionBackground2) is { } second ? DiagramInk.Faded(build.Medium, second, 0.2) : DiagramInk.Faded(build.Medium, Ink.Series(2), 0.12),
        };

        foreach (var row in rowed.GroupBy(task => task.Order).Select(group => group.First()))
        {
            var y = (row.Order * gap) + top;
            var band = build.Medium.Rectangle(LayoutRect.Offset(new LayoutRect(0, y - ((gap - bar) / 2), across.Wide, gap), shift));

            var style = (row.Section is { } section && placed.TryGetValue(section, out var index) ? index : 0) % styles;
            var fill = style switch
            {
                0 => tints[0],
                2 => tints[2],
                _ => tints[1],
            };

            build.Draw(DiagramMarks.Outlined(build.Medium, band, fill, null, 0));
            lines.Add(build.Medium.Line(new LayoutPoint(across.From, y + (bar / 2)) + shift, new LayoutPoint(across.To, y + (bar / 2)) + shift));
        }

        build.Draw(DiagramMarks.Outlined(build.Medium, build.Medium.Group(lines), null, DiagramInk.Faded(build.Medium, Ink.Written(c.Grid) ?? Style.TextMuted, RowWash), 1, dashes: Dotted));
        build.Close();
    }

    private void Grid(LayoutBuilder build, GanttBlockNode chart, IReadOnlyList<DiagramTick> ticks, double span, double left, double foot, double top, double height, double gridStart, LayoutVector shift)
    {
        // The dates run across, so their lines run down: from under the head of the chart to its foot, and again
        // over the top where a second axis is drawn up there.
        var lines = new List<LayoutShape>();

        DiagramGrid.Lines(build.Medium, lines, new LayoutRect(new LayoutPoint(left + shift.X, top + gridStart - 50 + shift.Y),
                                                                      new LayoutPoint(left + span + shift.X, foot + shift.Y)), ticks, upright: false);

        if (chart.TopAxis)
            DiagramGrid.Lines(build.Medium, lines, new LayoutRect(new LayoutPoint(left + shift.X, top + shift.Y),
                                                                          new LayoutPoint(left + span + shift.X, height - gridStart + shift.Y)), ticks, upright: false);


        build.Open(GanttPiece.Grid, part: null, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(build.Medium, build.Medium.Group(lines), null, DiagramInk.Faded(build.Medium, Ink.Written(chart.Config.Grid) ?? Style.CodeBorder, 0.8), 1));

        var stroke = new DiagramStroke(Style.CodeBorder);
        DiagramAxis.Draw(build, GanttPiece.Axis, null, new LayoutPoint(left, foot) + shift, new LayoutPoint(left + span, foot) + shift, ticks, stroke, GanttPiece.Date, after: true, line: false, tick: 0);
        if (chart.TopAxis)
            DiagramAxis.Draw(build, GanttPiece.Axis, null, new LayoutPoint(left, top) + shift, new LayoutPoint(left + span, top) + shift, ticks, stroke, GanttPiece.Date, after: false, line: false, tick: 0);

        build.Close();
    }

    /// <summary>The line at today, where today is on the chart and <c>todayMarker</c> does not turn it off — styled as it says.</summary>
    private void Today(LayoutBuilder build, GanttBlockNode chart, DateTime first, DateTime last, double x, double from, double to, LayoutVector shift)
    {
        var now = chart.Now;
        var today = chart.Marker;
        if (today.Off || now < first || now > last) return;

        var ink = Ink.Written(today.Stroke) ?? Ink.Written(chart.Config.TodayLine) ?? Style.Danger;
        if (today.Opacity is { } opacity) ink = DiagramInk.Faded(build.Medium, ink, opacity);

        var line = build.Medium.Line(new LayoutPoint(x, from) + shift, new LayoutPoint(x, to) + shift);

        var dashes = today.Dashes is { } written ? written.Select(dash => dash / (today.Width ?? 2)).ToArray() : null;

        build.Open(GanttPiece.Today, part: null, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(build.Medium, line, null, ink, today.Width ?? 2, dashes: dashes));
        build.Close();
    }
}
