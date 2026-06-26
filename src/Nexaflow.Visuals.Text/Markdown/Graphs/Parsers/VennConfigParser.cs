using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>venn</c> front-matter config block into a <see cref="VennConfig"/>: the
/// <c>config: venn:</c> keys (<c>width</c>/<c>height</c>/<c>padding</c>/<c>useDebugLayout</c>/<c>useMaxWidth</c>)
/// and the <c>config: themeVariables:</c> <c>venn1…venn8</c> circle palette.  Same shallow,
/// indentation-aware reader as the other diagram config parsers; never throws.
/// </summary>
public static class VennConfigParser
{
    public static VennConfig Parse(string? frontMatter)
    {
        var cfg = new VennConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, VennConfig cfg)
    {
        var stack = new List<(int indent, string key)>();
        var palette = new SortedDictionary<int, Brush>();

        foreach (var raw in yaml.Split('\n'))
        {
            var ts = raw.TrimStart();
            if (ts.Length == 0 || ts[0] == '#') continue;

            int indent = raw.Length - ts.Length;
            int colon = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (parent.Equals("venn", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "width":          if (TryNum(value, out var w)) cfg.Width   = w; break;
                    case "height":         if (TryNum(value, out var h)) cfg.Height  = h; break;
                    case "padding":        if (TryNum(value, out var p)) cfg.Padding = p; break;
                    case "usedebuglayout": cfg.UseDebugLayout = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "usemaxwidth":    cfg.UseMaxWidth    = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            else if (parent.Equals("themeVariables", StringComparison.OrdinalIgnoreCase))
            {
                string k = key.ToLowerInvariant();
                if (k.StartsWith("venn") && int.TryParse(k["venn".Length..], out int idx) && ParseBrush(value) is Brush b)
                    palette[idx] = b;
            }
        }

        cfg.SetPalette.AddRange(palette.Values);
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
