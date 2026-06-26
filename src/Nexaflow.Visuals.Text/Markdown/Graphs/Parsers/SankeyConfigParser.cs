using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>sankey</c> front-matter config block (<c>config: sankey:</c>) into a
/// <see cref="SankeyConfig"/> — sizes, <c>linkColor</c>/<c>nodeAlignment</c>/<c>labelStyle</c> enums,
/// <c>showValues</c>/<c>prefix</c>/<c>suffix</c>, <c>nodeWidth</c>/<c>nodePadding</c>, and the nested
/// <c>nodeColors</c> map.  Same shallow, indentation-aware reader as the other diagram config parsers;
/// never throws.
/// </summary>
public static class SankeyConfigParser
{
    public static SankeyConfig Parse(string? frontMatter)
    {
        var cfg = new SankeyConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, SankeyConfig cfg)
    {
        var stack = new List<(int indent, string key)>();

        foreach (var raw in yaml.Split('\n'))
        {
            var ts = raw.TrimStart();
            if (ts.Length == 0 || ts[0] == '#') continue;

            int indent = raw.Length - ts.Length;
            int colon = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = CleanValue(raw[(colon + 1)..]);
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (parent.Equals("nodeColors", StringComparison.OrdinalIgnoreCase))
            {
                if (ParseBrush(value) is Brush b) cfg.NodeColors[Unquote(key)] = b;
            }
            else if (parent.Equals("sankey", StringComparison.OrdinalIgnoreCase))
            {
                ApplySankey(cfg, key, value);
            }
        }
    }

    private static void ApplySankey(SankeyConfig cfg, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "width":       if (TryNum(value, out var w))  cfg.Width  = w; break;
            case "height":      if (TryNum(value, out var h))  cfg.Height = h; break;
            case "nodewidth":   if (TryNum(value, out var nw)) cfg.NodeWidth   = nw; break;
            case "nodepadding": if (TryNum(value, out var np)) cfg.NodePadding = np; break;
            case "showvalues":  cfg.ShowValues = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "usemaxwidth": cfg.UseMaxWidth = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "prefix":      cfg.Prefix = value; break;
            case "suffix":      cfg.Suffix = value; break;
            case "linkcolor":
                cfg.LinkColor = value.ToLowerInvariant() switch
                {
                    "source"   => SankeyLinkColor.Source,
                    "target"   => SankeyLinkColor.Target,
                    "gradient" => SankeyLinkColor.Gradient,
                    _ when ParseBrush(value) is Brush lb => Custom(cfg, lb),
                    _ => cfg.LinkColor,
                };
                break;
            case "nodealignment":
                cfg.NodeAlignment = value.ToLowerInvariant() switch
                {
                    "left"   => SankeyNodeAlignment.Left,
                    "right"  => SankeyNodeAlignment.Right,
                    "center" => SankeyNodeAlignment.Center,
                    _        => SankeyNodeAlignment.Justify,
                };
                break;
            case "labelstyle":
                cfg.LabelStyle = value.StartsWith("o", StringComparison.OrdinalIgnoreCase)
                    ? SankeyLabelStyle.Outlined : SankeyLabelStyle.Legacy;
                break;
        }
    }

    private static SankeyLinkColor Custom(SankeyConfig cfg, Brush b)
    {
        cfg.LinkColorCustom = b;
        return SankeyLinkColor.Custom;
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

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0]) s = s[1..^1];
        return s.Trim();
    }

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
