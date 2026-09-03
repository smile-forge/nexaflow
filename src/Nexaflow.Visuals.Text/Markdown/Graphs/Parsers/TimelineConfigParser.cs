using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>timeline</c> front-matter config block into a <see cref="TimelineConfig"/>:
/// the <c>config: timeline:</c> keys (<c>disableMulticolor</c>, <c>padding</c>) and the
/// <c>config: themeVariables:</c> colour slots <c>cScale0…11</c> / <c>cScaleLabel0…11</c>.  Same
/// shallow, indentation-aware reader as the other diagram config parsers; never throws.
/// </summary>
public static class TimelineConfigParser
{
    public static TimelineConfig Parse(string? frontMatter)
    {
        var cfg = new TimelineConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, TimelineConfig cfg)
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
            if (parent.Equals("timeline", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "disablemulticolor": cfg.DisableMulticolor = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "padding":           if (TryNum(value, out var p)) cfg.Padding = p; break;
                }
            }
            else if (parent.Equals("themeVariables", StringComparison.OrdinalIgnoreCase))
            {
                string k = key.ToLowerInvariant();
                // cScaleLabelN is checked first: it also starts with "cscale".
                if (k.StartsWith("cscalelabel") && int.TryParse(k["cscalelabel".Length..], out int li) && ParseBrush(value) is Brush lb)
                    cfg.ScaleLabel[li] = lb;
                else if (k.StartsWith("cscale") && int.TryParse(k["cscale".Length..], out int ci) && ParseBrush(value) is Brush cb)
                    cfg.Scale[ci] = cb;
            }
        }
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
