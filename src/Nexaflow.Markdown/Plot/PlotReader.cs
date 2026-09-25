using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Settings;
using System.Globalization;
using static Nexaflow.Markdown.Settings.SettingValues;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// The settings a plot block carries, read off its tree into <see cref="PlotSettings"/>.
///
/// <para>
/// A value that is not what its key takes <strong>stops the block being a plot</strong> and the lines are
/// shown as they were written, because a setting nobody can read is a question about the whole picture —
/// where a cell that will not read is a question about one mark, and loses only that mark. The same line
/// a word cloud draws.
/// </para>
/// <para>
/// The channels are not read here. Whether <c>size: 4</c> names a column or sets a constant is not a fact
/// about the characters, so they are carried across as written and settled once the columns are known.
/// </para>
/// </summary>
public static class PlotReader
{
    /// <summary>
    /// The settings the tree describes, or false with the reason. <paramref name="fence"/> is the geom
    /// the fence's own name asks for, which a <c>geom:</c> line overrides.
    /// </summary>
    public static bool TrySettings(ContentNode root, PlotFence fence,
                                   out PlotSettings? settings, out string? error) =>
        TrySettings(root, fence, out settings, out error, out _);

    /// <summary>The settings the tree describes, or false with the reason and the setting that gave it, where one did.</summary>
    public static bool TrySettings(ContentNode root, PlotFence fence,
                                   out PlotSettings? settings, out string? error, out string? key) =>
        TrySettings(SettingKeys.Written(root, PlotKinds.Setting, PlotRoles.Value), fence, out settings, out error, out key);

    /// <summary>
    /// The tree with the setting that stopped it reading marked with why — its value, where one is written, and the last line
    /// setting it, since that is the one read — or the whole of it where no one setting did.
    /// </summary>
    public static ContentNode Marked(ContentNode root, string? key, string reason)
    {
        var plain = SettingKeys.Plain(key);
        var culprit = key is null
            ? null
            : root.SelfAndDescendants().LastOrDefault(node => node.Kind == PlotKinds.Setting && SettingKeys.Plain(node.Part(Roles.Name)?.Text) == plain);

        if (culprit is null) return root.Saying(reason);

        return AstRewrite.Each(root, node => !ReferenceEquals(node, culprit)
            ? node
            : node.Children.Any(child => child.Role == PlotRoles.Value)
                ? node.With([.. node.Children.Select(child => child.Role == PlotRoles.Value ? child.Saying(reason) : child)])
                : node.Saying(reason));
    }

    private static bool TrySettings(IReadOnlyDictionary<string, string> fields, PlotFence fence,
                                    out PlotSettings? settings, out string? error, out string? key)
    {
        settings = null;
        key = null;

        // Every key here is a setting already: the parser only makes a setting of a key it knows, and one
        // it does not know is a row. So there is no unknown-setting fault to report here.
        var it = PlotSettings.Default;

        if (!Choice(fields, "geom", PlotFences.Geom(fence), out var geom, out error)) return Stop("geom", out key);
        if (!Choice(fields, "legend", it.Legend, out var legend, out error)) return Stop("legend", out key);
        if (!Choice(fields, "xScale", it.XScale, out var xScale, out error)) return Stop("xScale", out key);
        if (!Choice(fields, "yScale", it.YScale, out var yScale, out error)) return Stop("yScale", out key);
        if (!Choice(fields, "grid", it.Grid, out var grid, out error)) return Stop("grid", out key);

        if (!Limits(fields, "xLimits", out var xLimits, out error)) return Stop("xLimits", out key);
        if (!Limits(fields, "yLimits", out var yLimits, out error)) return Stop("yLimits", out key);
        if (!Limits(fields, "fillLimits", out var fillLimits, out error)) return Stop("fillLimits", out key);

        if (!Numbers(fields, "xBreaks", out var xBreaks, out error)) return Stop("xBreaks", out key);
        if (!Numbers(fields, "yBreaks", out var yBreaks, out error)) return Stop("yBreaks", out key);

        if (!Number(fields, "width", 0, 0, PlotSettings.MaxSide, out var width, out error)) return Stop("width", out key);
        if (!Number(fields, "height", 0, 0, PlotSettings.MaxSide, out var height, out error)) return Stop("height", out key);

        if (!Range(fields, "sizeRange", it.MinSize, it.MaxSize, PlotSettings.SmallestSize,
                   PlotSettings.LargestSize, out var minSize, out var maxSize, out error)) return Stop("sizeRange", out key);

    if (!Maybe(fields, "header", out var header, out error)) return Stop("header", out key);
            if (!Flag(fields, "flip", it.Flip, out var flip, out error)) return Stop("flip", out key);

            if (!Number(fields, "jitter", it.Jitter, 0, 1, out var jitter, out error)) return Stop("jitter", out key);
            if (!Range(fields, "alphaRange", it.MinAlpha, it.MaxAlpha, 0, 1,
                       out var minAlpha, out var maxAlpha, out error)) return Stop("alphaRange", out key);

            double? aspect = null;
            if (Text(fields, "aspect") is not null)
            {
                if (!Number(fields, "aspect", 1, 0.05, 20, out var shape, out error)) return Stop("aspect", out key);

                aspect = shape;
            }
        if (!Flag(fields, "labels", it.Labels, out var labels, out error)) return Stop("labels", out key);

        if (!Colours(fields, "palette", out var palette, out error)) return Stop("palette", out key);
    if (!Counting(fields, it, out var binsX, out var binsY, out error)) return Stop("bins", out key);
            if (!Choice(fields, "contour", it.Contour, out var contour, out error)) return Stop("contour", out key);
            if (!Widths(fields, out var bandwidth, out error)) return Stop("bandwidth", out key);
        if (!Flag(fields, "points", it.Points, out var points, out error)) return Stop("points", out key);
                if (!Choice(fields, "fit", it.Fit, out var fit, out error)) return Stop("fit", out key);
                if (!Choice(fields, "method", it.Method, out var method, out error)) return Stop("method", out key);
                if (!Flag(fields, "se", it.Se, out var se, out error)) return Stop("se", out key);
                if (!Number(fields, "level", it.Level, 0.5, 0.999, out var confidence, out error)) return Stop("level", out key);
                if (!Reported(fields, out var stats, out error)) return Stop("stats", out key);

    if (!Number(fields, "levels", it.Levels, 1, 40, out var levels, out error)) return Stop("levels", out key);
            if (!Number(fields, "facetCols", 0, 0, 12, out var facetCols, out error)) return Stop("facetCols", out key);
            if (!Number(fields, "adjust", it.Adjust, 0.05, 20, out var adjust, out error)) return Stop("adjust", out key);
        if (!Middle(fields, out var midpoint, out error)) return Stop("midpoint", out key);

        settings = it with
        {
            Geom = geom,
            Third = PlotFences.Third(fence, geom),

            X = Text(fields, "x"),
            Y = Text(fields, "y"),
            Colour = Text(fields, "colour") ?? Text(fields, "color"),
            Fill = Text(fields, "fill"),
            Size = Text(fields, "size"),
            Shape = Text(fields, "shape"),
            Alpha = Text(fields, "alpha"),
            Label = Text(fields, "label"),
            Group = Text(fields, "group"),
            Facet = Text(fields, "facet"),
            FacetCols = facetCols > 0 ? (int)facetCols : null,

            Header = header,

            Title = Text(fields, "title"),
            Subtitle = Text(fields, "subtitle"),
            Caption = Text(fields, "caption"),
            XTitle = Text(fields, "xTitle"),
            YTitle = Text(fields, "yTitle"),
            LegendTitle = Text(fields, "legendTitle"),

            XScale = xScale,
            YScale = yScale,
            XLimits = xLimits,
            YLimits = yLimits,
            XBreaks = xBreaks,
            YBreaks = yBreaks,
            Grid = grid,

                    Legend = legend,

            MinSize = minSize,
                    MaxSize = maxSize,
                    MinAlpha = minAlpha,
                    MaxAlpha = maxAlpha,

                    Jitter = jitter,
                    Aspect = aspect,
                    Flip = flip,

            Palette = palette,
            Gradient = Text(fields, "gradient"),
            Midpoint = midpoint,
            FillLimits = fillLimits,
            Labels = labels,

            BinsX = binsX,
                    BinsY = binsY,

                    Contour = contour,
                    Levels = (int)levels,
                    Bandwidth = bandwidth,
                    Adjust = adjust,
            Points = points,

                            Fit = fit,
                            Se = se,
                            Level = confidence,
                            Stats = stats,
                            Method = method,

            Width = width,
            Height = height,
        };

        return true;
    }

    /// <summary>Says which setting stopped the block reading, and that it stopped.</summary>
    private static bool Stop(string setting, out string? key)
    {
        key = setting;
        return false;
    }

    /// <summary>
    /// How many bins the plane is cut into: one number for both sides, or one each.
    /// </summary>
    private static bool Counting(IReadOnlyDictionary<string, string> fields, PlotSettings it,
                                 out int across, out int up, out string? error)
    {
        across = it.BinsX;
        up = it.BinsY;

        if (!Numbers(fields, "bins", out var bins, out error)) return false;
        if (bins is null) return true;

        var written = Text(fields, "bins");

        if (bins.Count > 2)
        {
            error = $"`bins: {written}` is more than two numbers — one for both sides, or one each.";
            return false;
        }

        foreach (var many in bins)
            if (many != Math.Floor(many) || many < PlotBins.FewestBins || many > PlotBins.MostBins)
            {
                error = $"`bins: {written}` takes whole numbers from {PlotBins.FewestBins} to {PlotBins.MostBins}.";
                return false;
            }

        across = (int)bins[0];
        up = (int)bins[^1];

        return true;
    }

    /// <summary>How wide the kernel is: one number for both axes, or one each.</summary>
    private static bool Widths(IReadOnlyDictionary<string, string> fields,
                               out (double X, double Y)? result, out string? error)
    {
        result = null;

        if (!Numbers(fields, "bandwidth", out var widths, out error)) return false;
        if (widths is null) return true;

        var written = Text(fields, "bandwidth");

        if (widths.Count > 2)
        {
            error = $"`bandwidth: {written}` is more than two numbers — one for both axes, or one each.";
            return false;
        }

        foreach (var width in widths)
            if (width <= 0)
            {
                error = $"`bandwidth: {written}` takes widths greater than nothing.";
                return false;
            }

        result = (widths[0], widths[^1]);
        return true;
    }

    /// <summary>Which figures a block asks to have written on the panel.</summary>
    public static readonly IReadOnlyList<string> Reportable = ["r", "r2", "n", "p"];

    private static bool Reported(IReadOnlyDictionary<string, string> fields,
                                 out IReadOnlyList<string>? result, out string? error)
    {
        result = null;
        error = null;

        if (Text(fields, "stats") is not { } written) return true;

        var asked = new List<string>();

        foreach (var what in Split(written))
        {
            var plain = what.ToLowerInvariant();

            if (!Reportable.Contains(plain))
            {
                error = $"`stats: {written}` names nothing to report — `{what}` is not one of "
                      + $"{string.Join(", ", Reportable)}.";

                return false;
            }

            if (!asked.Contains(plain)) asked.Add(plain);
        }

        if (asked.Count == 0)
        {
            error = $"`stats: {written}` names nothing to report.";
            return false;
        }

        result = asked;
        return true;
    }

    /// <summary>
    /// The value a diverging run of colours turns about. Read apart from the other numbers because nought
    /// is the midpoint anybody actually writes, so "nought" and "nothing written" have to stay different
    /// answers.
    /// </summary>
    private static bool Middle(IReadOnlyDictionary<string, string> fields, out double? result, out string? error)
    {
        result = null;

        if (Text(fields, "midpoint") is null)
        {
            error = null;
            return true;
        }

        if (!Number(fields, "midpoint", 0, double.MinValue, double.MaxValue, out var middle, out error))
            return false;

        result = middle;
        return true;
    }

    // ── What a value has to be ──────────────────────────────────────────────

    /// <summary>Two numbers, low then high — what <c>sizeRange: 4 28</c> says.</summary>
    private static bool Range(IReadOnlyDictionary<string, string> fields, string key,
                              double lowIf, double highIf, double min, double max,
                              out double low, out double high, out string? error)
    {
        low = lowIf;
        high = highIf;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        var parts = Split(written);

        if (parts.Count != 2)
        {
            error = $"`{key}: {written}` is not two numbers, smallest first.";
            return false;
        }

        var read = new double[2];

        for (var at = 0; at < 2; at++)
            if (!double.TryParse(parts[at], NumberStyles.Float, CultureInfo.InvariantCulture, out read[at])
                || double.IsNaN(read[at]) || double.IsInfinity(read[at])
                || read[at] < min || read[at] > max)
            {
                error = $"`{key}: {written}` is not two numbers from {Written(min)} to {Written(max)}.";
                return false;
            }

        low = Math.Min(read[0], read[1]);
        high = Math.Max(read[0], read[1]);
        return true;
    }

    /// <summary>The two ends of an axis, or null where the block wrote none.</summary>
    private static bool Limits(IReadOnlyDictionary<string, string> fields, string key,
                               out (double Min, double Max)? result, out string? error)
    {
        result = null;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        if (!Numbers(fields, key, out var read, out error)) return false;

        if (read is not { Count: 2 })
        {
            error = $"`{key}: {written}` is not two numbers, the ends of the axis.";
            return false;
        }

        if (read[0] == read[1])
        {
            error = $"`{key}: {written}` is one number twice, so the axis would have no length.";
            return false;
        }

        result = (Math.Min(read[0], read[1]), Math.Max(read[0], read[1]));
        return true;
    }

    /// <summary>Several numbers written one after another, or null where the block wrote none.</summary>
    private static bool Numbers(IReadOnlyDictionary<string, string> fields, string key,
                                out IReadOnlyList<double>? result, out string? error)
    {
        result = null;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        var read = new List<double>();

        foreach (var part in Split(written))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || double.IsNaN(number) || double.IsInfinity(number))
            {
                error = $"`{key}: {written}` is not a list of numbers — `{part}` is not one.";
                return false;
            }

            read.Add(number);
        }

        if (read.Count == 0)
        {
            error = $"`{key}: {written}` names no numbers.";
            return false;
        }

        result = read;
        return true;
    }
    /// <summary>Colours written out, taken in turn — or null where none was written.</summary>
    private static bool Colours(IReadOnlyDictionary<string, string> fields, string key,
                                out IReadOnlyList<string>? result, out string? error)
    {
        result = null;
        error = null;

        if (Text(fields, key) is not { } written) return true;

        var colours = Split(written);

        if (colours.Count == 0)
        {
            error = $"`{key}: {written}` names no colours.";
            return false;
        }

        result = colours;
        return true;
    }
}
