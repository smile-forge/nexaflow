using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>quadrantChart</c> diagrams.
///
/// Syntax:
/// <code>
/// quadrantChart
///     title &lt;text&gt;
///     x-axis &lt;low&gt; --&gt; &lt;high&gt;
///     y-axis &lt;low&gt; --&gt; &lt;high&gt;
///     quadrant-1 &lt;text&gt;                         (top-right; 2/3/4 go counter-clockwise)
///     "&lt;point&gt;": [x, y]                          (x, y in 0..1; quotes optional)
///     &lt;point&gt;: [x, y] radius: 12, color: #f00    (inline styling)
///     &lt;point&gt;:::class: [x, y]                    (apply a classDef)
///     classDef class color: #f00, radius: 10     (reusable style)
/// </code>
/// Supported point/class style keys: <c>radius</c>, <c>color</c>, <c>stroke-color</c>,
/// <c>stroke-width</c>.  Inline styles take precedence over a referenced class; classDefs may be
/// declared before or after the points that use them.
/// </summary>
public sealed class MermaidQuadrantParser
{
    public bool CanParse(string language) =>
        language.Equals("quadrantChart", StringComparison.OrdinalIgnoreCase);

    public QuadrantChart Parse(string source)
    {
        var chart = new QuadrantChart();
        try { ParseInto(source, chart); }
        catch { /* never throw; return partial chart */ }
        return chart;
    }

    // ── Regexes ────────────────────────────────────────────────────────────

    private static readonly Regex RxTitle =
        new(@"^title\s+(?<t>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // x-axis low --> high   (the "--> high" tail is optional)
    private static readonly Regex RxXAxis =
        new(@"^x-axis\s+(?<lo>.+?)(?:\s*-->\s*(?<hi>.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxYAxis =
        new(@"^y-axis\s+(?<lo>.+?)(?:\s*-->\s*(?<hi>.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxQuadrant =
        new(@"^quadrant-(?<n>[1-4])\s+(?<t>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // <name>: [x, y] <optional style tail>   — name may carry a trailing :::class reference.
    private static readonly Regex RxPoint =
        new(@"^(?<name>.+?)\s*:\s*\[\s*(?<x>[\d.]+)\s*,\s*(?<y>[\d.]+)\s*\]\s*(?<style>.*)$",
            RegexOptions.Compiled);

    // ── Parser ─────────────────────────────────────────────────────────────

    private static void ParseInto(string source, QuadrantChart chart)
    {
        var classDefs = new Dictionary<string, QuadrantStyle>(StringComparer.OrdinalIgnoreCase);
        var pending   = new List<(QuadrantPoint point, string? className)>();

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            // Skip the opening keyword.
            if (line.Equals("quadrantChart", StringComparison.OrdinalIgnoreCase)) continue;

            // classDef <name> <styles>
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line["classDef ".Length..].TrimStart();
                int sp   = rest.IndexOf(' ');
                if (sp > 0)
                    classDefs[rest[..sp]] = ParseStyle(rest[(sp + 1)..]);
                continue;
            }

            var tm = RxTitle.Match(line);
            if (tm.Success) { chart.Title = tm.Groups["t"].Value.Trim(); continue; }

            var xm = RxXAxis.Match(line);
            if (xm.Success)
            {
                chart.XAxisLeft  = Clean(xm.Groups["lo"].Value);
                chart.XAxisRight = Clean(xm.Groups["hi"].Value);
                continue;
            }

            var ym = RxYAxis.Match(line);
            if (ym.Success)
            {
                chart.YAxisBottom = Clean(ym.Groups["lo"].Value);
                chart.YAxisTop    = Clean(ym.Groups["hi"].Value);
                continue;
            }

            var qm = RxQuadrant.Match(line);
            if (qm.Success)
            {
                string text = Clean(qm.Groups["t"].Value);
                switch (qm.Groups["n"].Value)
                {
                    case "1": chart.Quadrant1 = text; break;
                    case "2": chart.Quadrant2 = text; break;
                    case "3": chart.Quadrant3 = text; break;
                    case "4": chart.Quadrant4 = text; break;
                }
                continue;
            }

            var pm = RxPoint.Match(line);
            if (pm.Success
                && double.TryParse(pm.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(pm.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                var (label, className) = SplitName(pm.Groups["name"].Value);
                var inline = ParseStyle(pm.Groups["style"].Value);

                var point = new QuadrantPoint
                {
                    Label = label,
                    X     = Math.Clamp(x, 0, 1),
                    Y     = Math.Clamp(y, 0, 1),
                    Style = inline.IsEmpty ? null : inline,
                };
                chart.Points.Add(point);
                pending.Add((point, className));
            }
        }

        // Resolve class references now that every classDef has been seen (they may follow the points).
        foreach (var (point, className) in pending)
        {
            if (className is null || !classDefs.TryGetValue(className, out var cls)) continue;
            // Class is the base; inline (point.Style) wins where set.
            point.Style = cls.With(point.Style ?? new QuadrantStyle());
        }
    }

    private static string Clean(string s) => s.Trim().Trim('"').Trim();

    /// <summary>Splits a point name into its display label and optional <c>:::class</c> reference.</summary>
    private static (string label, string? className) SplitName(string raw)
    {
        var s = raw.Trim();
        int ci = s.IndexOf(":::", StringComparison.Ordinal);
        if (ci < 0) return (Clean(s), null);

        string cls = s[(ci + 3)..].Trim();
        return (Clean(s[..ci]), cls.Length > 0 ? cls : null);
    }

    /// <summary>
    /// Parses a comma-separated style list (<c>radius: 12, color: #f00, stroke-width: 2px</c>).
    /// Whitespace around keys/values/colons is tolerated; unknown keys are ignored.
    /// </summary>
    private static QuadrantStyle ParseStyle(string raw)
    {
        var style = new QuadrantStyle();
        if (string.IsNullOrWhiteSpace(raw)) return style;

        foreach (var part in raw.Split(','))
        {
            int colon = part.IndexOf(':');
            if (colon < 0) continue;

            string key = part[..colon].Trim().ToLowerInvariant();
            string val = part[(colon + 1)..].Trim();
            if (val.Length == 0) continue;

            switch (key)
            {
                case "color":        style.FillColor   = val; break;
                case "stroke-color": style.StrokeColor = val; break;
                case "radius":       if (TryLength(val, out double r)) style.Radius      = r; break;
                case "stroke-width": if (TryLength(val, out double w)) style.StrokeWidth = w; break;
            }
        }
        return style;
    }

    /// <summary>Parses a length like <c>12</c> or <c>5px</c> into a double.</summary>
    private static bool TryLength(string val, out double result)
    {
        var s = val.Trim();
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            s = s[..^2].Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
