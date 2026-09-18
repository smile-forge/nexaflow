using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// What a plot block's settings come to, with everything nobody wrote left at its default.
///
/// <para>
/// The channels are held as the characters they were written with rather than as columns, because
/// whether <c>size: 4</c> names a column or sets a constant cannot be known until the columns are —
/// which is <c>ResolveAesthetics</c>'. Everything else is read here, where the key says what its value
/// has to be.
/// </para>
/// </summary>
public sealed record PlotSettings
{
    public static readonly PlotSettings Default = new();

    // ── What is drawn ───────────────────────────────────────────────────────

    /// <summary>What the table is drawn as. The fence names one, and <c>geom:</c> overrides it.</summary>
    public PlotGeom Geom { get; init; } = PlotGeom.Point;

    // ── Which column feeds which channel, as written ────────────────────────

    public string? X { get; init; }
    public string? Y { get; init; }
    public string? Colour { get; init; }
    public string? Fill { get; init; }
    public string? Size { get; init; }
    public string? Shape { get; init; }
    public string? Alpha { get; init; }
    public string? Label { get; init; }
    public string? Group { get; init; }

    /// <summary>
    /// The channel a third column feeds where nobody mapped one. Not written anywhere — it is what the
    /// fence asks for, and it is the whole difference between a scatter plot and a bubble plot.
    /// </summary>
    public PlotAesthetic? Third { get; init; }

    /// <summary>
    /// Whether the first row names the columns. Nothing written leaves it to the shape of the table,
    /// which is what a reader means nearly always; <c>header: false</c> is for a table of categories
    /// whose first row happens to be all words.
    /// </summary>
    public bool? Header { get; init; }

    // ── What it is called ───────────────────────────────────────────────────

    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Caption { get; init; }

    /// <summary>The axis titles. Nothing written takes the name of the column mapped to that axis.</summary>
    public string? XTitle { get; init; }

    public string? YTitle { get; init; }
    public string? LegendTitle { get; init; }

    // ── How a value becomes a place ─────────────────────────────────────────

    /// <summary>How each axis reads its values — ggplot2's <c>scale_x_log10</c> and the rest.</summary>
    public PlotScale XScale { get; init; } = PlotScale.Linear;

    public PlotScale YScale { get; init; } = PlotScale.Linear;

    /// <summary>
    /// The ends of an axis where the block wrote them, which is a reader asking for exactly those rather
    /// than for the round numbers either side of the values.
    /// </summary>
    public (double Min, double Max)? XLimits { get; init; }

    public (double Min, double Max)? YLimits { get; init; }

    /// <summary>Where an axis is marked, where the block says — otherwise the round numbers along it.</summary>
    public IReadOnlyList<double>? XBreaks { get; init; }

    public IReadOnlyList<double>? YBreaks { get; init; }

    /// <summary>Which gridlines are drawn behind the marks.</summary>
    public PlotGrid Grid { get; init; } = PlotGrid.Both;

    /// <summary>
    /// How far a mark is moved off its place at random, as a share of the gap between ticks, so rows
    /// landing on the same value stop hiding one another. The same block always moves them the same way.
    /// </summary>
    public double Jitter { get; init; }

    /// <summary>
    /// How much wider than tall the panel is drawn, where the block would rather say than take the room —
    /// ggplot2's <c>coord_fixed</c>. One makes a square panel, which is what a correlation matrix wants.
    /// </summary>
    public double? Aspect { get; init; }

    /// <summary>Whether the axes are swapped, so what was drawn across is drawn up — <c>coord_flip</c>.</summary>
    public bool Flip { get; init; }

    // ── How big a value is drawn ────────────────────────────────────────────

    /// <summary>The smallest and largest a mark is drawn, across which the size channel is spread.</summary>
    public double MinSize { get; init; } = 4;

    public double MaxSize { get; init; } = 24;

    /// <summary>
    /// How see-through the faintest and plainest marks are, across which a column mapped to alpha is
    /// spread. Nothing mapped leaves every mark at the plain end.
    /// </summary>
    public double MinAlpha { get; init; } = 0.2;

    public double MaxAlpha { get; init; } = 0.85;

    public const double SmallestSize = 0.5;
    public const double LargestSize = 200;

    /// <summary>What a mark is drawn at where no column is mapped to size.</summary>
    public const double PlainSize = 5;

    // ── What it is drawn in ─────────────────────────────────────────────────

    /// <summary>
    /// Colours taken in turn by the groups the colour channel makes, or null for the theme's own series
    /// colours. A colour nobody wrote belongs to whoever is drawing, so none is invented here.
    /// </summary>
    public IReadOnlyList<string>? Palette { get; init; }

    /// <summary>
    /// The run of colours a number is read along, where a channel carries numbers rather than names — a
    /// ramp by name, or two colours written out, or three with a middle. Nothing written leaves it to
    /// whoever is drawing.
    /// </summary>
    public string? Gradient { get; init; }

    /// <summary>
    /// The value the middle of a diverging run sits at. Written where a number has a meaningful nought —
    /// a correlation, a change, a difference — and the colour either side of it means the sign.
    /// </summary>
    public double? Midpoint { get; init; }

    /// <summary>The ends of the run of colours, where the block wrote them rather than taking the values' own.</summary>
    public (double Min, double Max)? FillLimits { get; init; }

    /// <summary>Whether each mark carries its own value written on it, which a small heat map can hold.</summary>
    public bool Labels { get; init; }

    // ── Counting rather than drawing ────────────────────────────────────────

    /// <summary>
    /// How many bins the plane is cut into across, where the geom counts rows into bins rather than
    /// drawing one mark each.
    /// </summary>
    public int BinsX { get; init; } = PlotBins.Bins;

    /// <summary>The same up the page. A hexagonal binning takes its own from the width.</summary>
    public int BinsY { get; init; } = PlotBins.Bins;

    // ── How thickly the points lie ──────────────────────────────────────────

    /// <summary>What a 2D density is drawn as.</summary>
    public PlotContour Contour { get; init; } = PlotContour.Bands;

    /// <summary>How many contours are drawn.</summary>
    public int Levels { get; init; } = PlotDensity.Levels;

    /// <summary>
    /// How wide the kernel is over each axis, where the block would rather say than be told. Nothing
    /// written takes Silverman's rule, which is what ggplot2 takes.
    /// </summary>
    public (double X, double Y)? Bandwidth { get; init; }

    /// <summary>What the widths worked out are multiplied by — ggplot2's <c>adjust</c>.</summary>
    public double Adjust { get; init; } = 1;

    /// <summary>Whether the rows themselves are drawn as marks over whatever else the geom draws.</summary>
    public bool Points { get; init; }

    // ── What is worked out and drawn over them ──────────────────────────────

    /// <summary>The line fitted through the points, where the block asks for one.</summary>
    public PlotFit Fit { get; init; } = PlotFit.None;

    /// <summary>Whether the fit carries the band its own uncertainty makes. ggplot2's <c>se</c>.</summary>
    public bool Se { get; init; } = true;

    /// <summary>How much of the uncertainty the band covers.</summary>
    public double Level { get; init; } = PlotFits.Level;

    /// <summary>Which of r, r squared, n and p are written on the panel, in the order they were asked for.</summary>
    public IReadOnlyList<string>? Stats { get; init; }

    /// <summary>Which correlation the stats report.</summary>
    public PlotMethod Method { get; init; } = PlotMethod.Pearson;

    public PlotLegend Legend { get; init; } = PlotLegend.Right;

    // ── The panel ───────────────────────────────────────────────────────────

    /// <summary>Nought for "as wide as the column it is in", which is what a plot with no <c>width:</c> takes.</summary>
    public double Width { get; init; }

    /// <summary>Nought for <see cref="HeightShare"/> of the width, which is what a plot with no <c>height:</c> takes.</summary>
    public double Height { get; init; }

    /// <summary>How tall a plot is for how wide it is, where it was not told.</summary>
    public const double HeightShare = 0.62;

    public const double MinSide = 80;
    public const double MaxSide = 4000;

    /// <summary>The widest a plot with no <c>width:</c> is drawn, however wide the column is.</summary>
    public const double RoomLimit = 900;
}

/// <summary>
/// The names a plot block's settings are written under.
///
/// <para>
/// Only the names live here, because they are all the parser needs: <strong>the settings are the lines
/// above the table, and the first row closes them</strong>, so which of two readings a <c>key: value</c>
/// line takes is settled by whether the key is one of these. What each one is set to, and what that
/// amounts to, is the reader's — a key naming a column is an aesthetic mapped to it, and the same key
/// set to anything else is that value for every mark.
/// </para>
/// </summary>
public static class PlotSetting
{
    /// <summary>Every setting name, as it is written, in the groups a reader thinks in.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        // What the plot is called
        "title", "subtitle", "caption", "xTitle", "yTitle", "legendTitle",

        // Which column feeds which channel — or, where it names no column, what every mark takes
        "x", "y", "color", "colour", "fill", "size", "shape", "alpha", "label", "group",

        // What is drawn
        "geom", "points", "jitter", "bins", "contour", "levels", "bandwidth", "adjust",

        // What is worked out and drawn over it
        "fit", "se", "level", "stats", "method",

        // How a value becomes a place
        "xScale", "yScale", "xLimits", "yLimits", "xBreaks", "yBreaks", "sizeRange", "alphaRange",

        // How a value becomes a colour
        "palette", "gradient", "midpoint", "fillLimits", "legend",

        // The panel itself
        "aspect", "flip", "grid", "width", "height", "header", "labels",
    ];

    /// <summary>What a diagnostic lists when it names them all.</summary>
    public static readonly string Names = string.Join(", ", Keys);

    /// <summary>Whether a key names a setting. Case and hyphens are ignored, so <c>x-scale</c> is <c>xScale</c>.</summary>
    public static bool Is(string? key) => SettingKeys.Is(key, Keys);
}
