namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>
/// What a <c>gantt</c> block's front matter asks for: the chart under <c>config: gantt:</c> — its paddings, bar sizes, fonts,
/// <c>axisFormat</c>, <c>tickInterval</c>, <c>topAxis</c>, <c>displayMode</c> and <c>weekday</c> — a <c>displayMode</c> written at the
/// top of the front matter too, and its colours under <c>themeVariables:</c>.
///
/// <para>A size nobody wrote is Mermaid's own, and a colour nobody wrote the theme's, so it is null here.</para>
/// </summary>
public sealed record GanttConfig
{
    public static GanttConfig Default { get; } = new();

    public double? TitleTopMargin { get; init; }
    public double? BarHeight { get; init; }
    public double? BarGap { get; init; }
    public double? TopPadding { get; init; }
    public double? RightPadding { get; init; }
    public double? LeftPadding { get; init; }
    public double? GridLineStartPadding { get; init; }
    public double? FontSize { get; init; }
    public double? SectionFontSize { get; init; }
    public int? NumberSectionStyles { get; init; }
    public double? UseWidth { get; init; }
    public bool UseMaxWidth { get; init; }

    public string? AxisFormat { get; init; }
    public string? TickInterval { get; init; }
    public bool TopAxis { get; init; }
    public bool Compact { get; init; }
    public string? Weekday { get; init; }

    public string? SectionBackground { get; init; }
    public string? AltSectionBackground { get; init; }
    public string? SectionBackground2 { get; init; }
    public string? ExcludeBackground { get; init; }
    public string? TaskBorder { get; init; }
    public string? TaskBackground { get; init; }
    public string? TaskText { get; init; }
    public string? TaskTextOutside { get; init; }
    public string? TaskTextDark { get; init; }
    public string? TaskTextClickable { get; init; }
    public string? ActiveTaskBorder { get; init; }
    public string? ActiveTaskBackground { get; init; }
    public string? DoneTaskBorder { get; init; }
    public string? DoneTaskBackground { get; init; }
    public string? CritBorder { get; init; }
    public string? CritBackground { get; init; }
    public string? Grid { get; init; }
    public string? TodayLine { get; init; }
    public string? VertLine { get; init; }
    public string? TitleColour { get; init; }
    public string? TextColour { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static GanttConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static GanttConfig From(MermaidConfig config)
    {
        var gantt = config.Diagram("gantt");
        var theme = config.Theme;
        var styles = gantt.Number("numberSectionStyles");

        return new GanttConfig
        {
            TitleTopMargin = Room(gantt, "titleTopMargin"),
            BarHeight = gantt.Size("barHeight"),
            BarGap = Room(gantt, "barGap"),
            TopPadding = Room(gantt, "topPadding"),
            RightPadding = Room(gantt, "rightPadding"),
            LeftPadding = Room(gantt, "leftPadding"),
            GridLineStartPadding = Room(gantt, "gridLineStartPadding"),
            FontSize = gantt.Size("fontSize"),
            SectionFontSize = gantt.Size("sectionFontSize"),
            NumberSectionStyles = styles is >= 1 ? (int)styles.Value : null,
            UseWidth = gantt.Size("useWidth"),
            UseMaxWidth = gantt.Flag("useMaxWidth") ?? false,

            AxisFormat = gantt.Value("axisFormat"),
            TickInterval = gantt.Value("tickInterval"),
            TopAxis = gantt.Flag("topAxis") ?? false,
            Compact = string.Equals((gantt.Value("displayMode") ?? config.Value("displayMode"))?.Trim(), "compact", StringComparison.OrdinalIgnoreCase),
            Weekday = gantt.Value("weekday"),

            SectionBackground = theme.Value("sectionBkgColor"),
            AltSectionBackground = theme.Value("altSectionBkgColor"),
            SectionBackground2 = theme.Value("sectionBkgColor2"),
            ExcludeBackground = theme.Value("excludeBkgColor"),
            TaskBorder = theme.Value("taskBorderColor"),
            TaskBackground = theme.Value("taskBkgColor"),
            TaskText = theme.Value("taskTextColor"),
            TaskTextOutside = theme.Value("taskTextOutsideColor"),
            TaskTextDark = theme.Value("taskTextDarkColor"),
            TaskTextClickable = theme.Value("taskTextClickableColor"),
            ActiveTaskBorder = theme.Value("activeTaskBorderColor"),
            ActiveTaskBackground = theme.Value("activeTaskBkgColor"),
            DoneTaskBorder = theme.Value("doneTaskBorderColor"),
            DoneTaskBackground = theme.Value("doneTaskBkgColor"),
            CritBorder = theme.Value("critBorderColor"),
            CritBackground = theme.Value("critBkgColor"),
            Grid = theme.Value("gridColor"),
            TodayLine = theme.Value("todayLineColor"),
            VertLine = theme.Value("vertLineColor"),
            TitleColour = theme.Value("titleColor"),
            TextColour = theme.Value("textColor"),
        };
    }

    private static double? Room(MermaidConfig section, string key) => section.Number(key) is { } room ? Math.Max(0, room) : null;
}
