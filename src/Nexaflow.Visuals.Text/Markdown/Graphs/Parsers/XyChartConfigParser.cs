using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>xychart</c> front-matter config block into an <see cref="XyChartConfig"/>.
/// Reads the <c>config: xyChart:</c> layout/flag keys (including the nested <c>xAxis:</c> / <c>yAxis:</c>
/// objects) and the <c>config: themeVariables: xyChart:</c> colour keys.  A small indentation-aware
/// reader (the config is a shallow, known shape — no general YAML library) keyed off the nearest
/// mapping ancestor; only keys nested under an <c>xyChart</c> object are applied, so unrelated config
/// (e.g. <c>theme: dark</c>) is ignored.  Never throws — unrecognised or malformed keys are skipped.
/// </summary>
public static class XyChartConfigParser
{
    public static XyChartConfig Parse(string? frontMatter)
    {
        var cfg = new XyChartConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw; return whatever was parsed */ }
        return cfg;
    }

    private static void ParseInto(string yaml, XyChartConfig cfg)
    {
        var stack = new List<(int indent, string key)>();

        foreach (var raw in yaml.Split('\n'))
        {
            if (IsBlankOrComment(raw)) continue;
            int indent = LeadingWhitespace(raw);

            int colon = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = CleanValue(raw[(colon + 1)..]);
            if (key.Length == 0) continue;

            // Drop ancestors at the same or deeper indent — they're no longer on the path.
            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            if (value.Length == 0)
            {
                // A mapping parent (e.g. "xyChart:", "xAxis:") — becomes an ancestor.
                stack.Add((indent, key));
                continue;
            }

            // Only honour keys nested under an xyChart object.
            if (!stack.Any(s => s.key.Equals("xyChart", StringComparison.OrdinalIgnoreCase)))
                continue;

            string parent = stack[^1].key;
            if (parent.Equals("xAxis", StringComparison.OrdinalIgnoreCase))
                ApplyAxis(cfg.XAxis, key, value);
            else if (parent.Equals("yAxis", StringComparison.OrdinalIgnoreCase))
                ApplyAxis(cfg.YAxis, key, value);
            else
                ApplyChartOrTheme(cfg, key, value);
        }
    }

    // ── Key routing ──────────────────────────────────────────────────────────

    private static void ApplyChartOrTheme(XyChartConfig cfg, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            // layout / flags
            case "width":                    if (TryNum(value, out var w))  cfg.Width  = w; break;
            case "height":                   if (TryNum(value, out var h))  cfg.Height = h; break;
            case "titlepadding":             if (TryNum(value, out var tp)) cfg.TitlePadding  = tp; break;
            case "titlefontsize":            if (TryNum(value, out var tf)) cfg.TitleFontSize = tf; break;
            case "showtitle":                cfg.ShowTitle  = Truthy(value); break;
            case "showlegend":               cfg.ShowLegend = Truthy(value); break;
            case "legendfontsize":           if (TryNum(value, out var lf)) cfg.LegendFontSize = lf; break;
            case "legendpadding":            if (TryNum(value, out var lp)) cfg.LegendPadding  = lp; break;
            case "plotreservedspacepercent": if (TryNum(value, out var pr)) cfg.PlotReservedSpacePercent = pr; break;
            case "showdatalabel":            cfg.ShowDataLabel           = Truthy(value); break;
            case "showdatalabeloutsidebar":  cfg.ShowDataLabelOutsideBar = Truthy(value); break;
            case "chartorientation":
                cfg.Orientation = value.StartsWith("h", StringComparison.OrdinalIgnoreCase)
                    ? XyOrientation.Horizontal : XyOrientation.Vertical;
                break;

            // theme colours
            case "backgroundcolor": cfg.BackgroundColor = ParseBrush(value); break;
            case "titlecolor":      cfg.TitleColor      = ParseBrush(value); break;
            case "datalabelcolor":  cfg.DataLabelColor  = ParseBrush(value); break;
            case "legendtextcolor": cfg.LegendTextColor = ParseBrush(value); break;
            case "xaxislabelcolor": cfg.XAxisLabelColor = ParseBrush(value); break;
            case "xaxistitlecolor": cfg.XAxisTitleColor = ParseBrush(value); break;
            case "xaxistickcolor":  cfg.XAxisTickColor  = ParseBrush(value); break;
            case "xaxislinecolor":  cfg.XAxisLineColor  = ParseBrush(value); break;
            case "yaxislabelcolor": cfg.YAxisLabelColor = ParseBrush(value); break;
            case "yaxistitlecolor": cfg.YAxisTitleColor = ParseBrush(value); break;
            case "yaxistickcolor":  cfg.YAxisTickColor  = ParseBrush(value); break;
            case "yaxislinecolor":  cfg.YAxisLineColor  = ParseBrush(value); break;
            case "plotcolorpalette":
                foreach (var c in value.Split(','))
                    if (ParseBrush(c.Trim()) is Brush b) cfg.PlotPalette.Add(b);
                break;
        }
    }

    private static void ApplyAxis(XyAxisConfig axis, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "showlabel":     axis.ShowLabel = Truthy(value); break;
            case "labelfontsize": if (TryNum(value, out var lf)) axis.LabelFontSize = lf; break;
            case "labelpadding":  if (TryNum(value, out var lp)) axis.LabelPadding  = lp; break;
            case "showtitle":     axis.ShowTitle = Truthy(value); break;
            case "titlefontsize": if (TryNum(value, out var tf)) axis.TitleFontSize = tf; break;
            case "titlepadding":  if (TryNum(value, out var tp)) axis.TitlePadding  = tp; break;
            case "showtick":      axis.ShowTick = Truthy(value); break;
            case "ticklength":    if (TryNum(value, out var tl)) axis.TickLength = tl; break;
            case "tickwidth":     if (TryNum(value, out var tw)) axis.TickWidth  = tw; break;
            case "showaxisline":  axis.ShowAxisLine = Truthy(value); break;
            case "axislinewidth": if (TryNum(value, out var aw)) axis.AxisLineWidth = aw; break;
            case "labelrotation": if (TryNum(value, out var lr)) axis.LabelRotation = lr; break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsBlankOrComment(string line)
    {
        var t = line.TrimStart();
        return t.Length == 0 || t[0] == '#';
    }

    private static int LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    /// <summary>Trims a scalar value: drops a trailing unquoted <c>#</c> comment, then strips
    /// surrounding single/double quotes (hex colours arrive quoted, so their <c>#</c> survives).</summary>
    private static string CleanValue(string raw)
    {
        string s = raw.Trim();
        if (s.Length == 0) return s;

        if (s[0] is '"' or '\'')
        {
            char q = s[0];
            int end = s.IndexOf(q, 1);
            return end > 0 ? s[1..end] : s[1..].Trim();
        }

        int hash = s.IndexOf('#');
        if (hash > 0 && char.IsWhiteSpace(s[hash - 1]))   // " # comment" — but not a leading bare value
            s = s[..hash].Trim();
        return s.Trim();
    }

    private static bool Truthy(string v) => v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool TryNum(string v, out double value) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static Brush? ParseBrush(string color)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch { return null; }
    }
}
