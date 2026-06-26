using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>radar</c> front-matter config block into a <see cref="RadarConfig"/>.
/// Reads the <c>config: radar:</c> geometry keys, the <c>config: themeVariables: radar:</c> styling keys
/// (both nest under a <c>radar</c> object, and their key sets are disjoint), and the radar-relevant global
/// <c>config: themeVariables:</c> keys (<c>titleColor</c>, <c>fontSize</c>, and the <c>cScale0…N</c> curve
/// palette).  Same shallow, indentation-aware reader as <see cref="XyChartConfigParser"/>; never throws.
/// </summary>
public static class RadarConfigParser
{
    public static RadarConfig Parse(string? frontMatter)
    {
        var cfg = new RadarConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw; return whatever was parsed */ }
        return cfg;
    }

    private static void ParseInto(string yaml, RadarConfig cfg)
    {
        var stack = new List<(int indent, string key)>();
        var cScales = new SortedDictionary<int, Brush>();

        foreach (var raw in yaml.Split('\n'))
        {
            if (IsBlankOrComment(raw)) continue;
            int indent = LeadingWhitespace(raw);

            int colon = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = CleanValue(raw[(colon + 1)..]);
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (parent.Equals("radar", StringComparison.OrdinalIgnoreCase))
                ApplyRadar(cfg, key, value);
            else if (parent.Equals("themeVariables", StringComparison.OrdinalIgnoreCase))
                ApplyTheme(cfg, cScales, key, value);
        }

        cfg.CurvePalette.AddRange(cScales.Values);
    }

    // ── config.radar geometry + config.themeVariables.radar styling ──────────

    private static void ApplyRadar(RadarConfig cfg, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            // geometry
            case "width":           if (TryNum(value, out var w))  cfg.Width  = w; break;
            case "height":          if (TryNum(value, out var h))  cfg.Height = h; break;
            case "margintop":       if (TryNum(value, out var mt)) cfg.MarginTop    = mt; break;
            case "marginbottom":    if (TryNum(value, out var mb)) cfg.MarginBottom = mb; break;
            case "marginleft":      if (TryNum(value, out var ml)) cfg.MarginLeft   = ml; break;
            case "marginright":     if (TryNum(value, out var mr)) cfg.MarginRight  = mr; break;
            case "axisscalefactor": if (TryNum(value, out var sf)) cfg.AxisScaleFactor = sf; break;
            case "axislabelfactor": if (TryNum(value, out var lf)) cfg.AxisLabelFactor = lf; break;
            case "curvetension":    if (TryNum(value, out var ct)) cfg.CurveTension    = ct; break;

            // styling
            case "axiscolor":            cfg.AxisColor      = ParseBrush(value); break;
            case "axisstrokewidth":      if (TryLen(value, out var asw)) cfg.AxisStrokeWidth = asw; break;
            case "axislabelfontsize":    if (TryLen(value, out var alf)) cfg.AxisLabelFontSize = alf; break;
            case "curveopacity":         if (TryNum(value, out var co))  cfg.CurveOpacity     = co; break;
            case "curvestrokewidth":     if (TryLen(value, out var csw)) cfg.CurveStrokeWidth = csw; break;
            case "graticulecolor":       cfg.GraticuleColor = ParseBrush(value); break;
            case "graticuleopacity":     if (TryNum(value, out var go))  cfg.GraticuleOpacity = go; break;
            case "graticulestrokewidth": if (TryLen(value, out var gsw)) cfg.GraticuleStrokeWidth = gsw; break;
            case "legendboxsize":        if (TryLen(value, out var lbs)) cfg.LegendBoxSize  = lbs; break;
            case "legendfontsize":       if (TryLen(value, out var lfs)) cfg.LegendFontSize = lfs; break;
        }
    }

    // ── global themeVariables ───────────────────────────────────────────────

    private static void ApplyTheme(RadarConfig cfg, SortedDictionary<int, Brush> cScales, string key, string value)
    {
        string k = key.ToLowerInvariant();
        if (k == "titlecolor") { cfg.TitleColor = ParseBrush(value); return; }
        if (k == "fontsize")   { if (TryLen(value, out var fs)) cfg.TitleFontSize = fs; return; }
        if (k.StartsWith("cscale") && int.TryParse(k["cscale".Length..], out int idx) &&
            ParseBrush(value) is Brush b)
            cScales[idx] = b;
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
        if (hash > 0 && char.IsWhiteSpace(s[hash - 1])) s = s[..hash].Trim();
        return s.Trim();
    }

    private static bool TryNum(string v, out double value) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses a length like <c>12</c> or <c>12px</c>.</summary>
    private static bool TryLen(string v, out double value)
    {
        v = v.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v[..^2].Trim();
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

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
