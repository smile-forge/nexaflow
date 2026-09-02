using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>journey</c> front-matter config block into a <see cref="JourneyConfig"/>:
/// the <c>config: journey:</c> keys (<c>width</c>/<c>height</c>/<c>boxMargin</c>/<c>taskFontSize</c>,
/// and the colour lists <c>actorColours</c>/<c>sectionFills</c> in either flow <c>["#a", "#b"]</c>
/// or block <c>- "#a"</c> form) plus the <c>themeVariables</c> <c>fillType0…7</c> section palette
/// (used only when <c>sectionFills</c> is absent).  Same shallow, indentation-aware reader as the
/// other diagram config parsers; never throws.
/// </summary>
public static class JourneyConfigParser
{
    public static JourneyConfig Parse(string? frontMatter)
    {
        var cfg = new JourneyConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, JourneyConfig cfg)
    {
        var stack     = new List<(int indent, string key)>();
        var fillTypes = new SortedDictionary<int, Brush>();

        foreach (var raw in yaml.Split('\n'))
        {
            var ts = raw.TrimStart();
            if (ts.Length == 0 || ts[0] == '#') continue;

            int indent = raw.Length - ts.Length;
            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;

            // Block-list item under a colour-list key:  - "#4e79a7"
            if (ts[0] == '-' && ListFor(cfg, parent) is List<Brush> list)
            {
                if (ParseBrush(Dequote(ts[1..])) is Brush b) list.Add(b);
                continue;
            }

            int colon = ts.IndexOf(':');
            if (colon < 0) continue;

            string key   = ts[..colon].Trim();
            string value = ts[(colon + 1)..].Trim();
            if (key.Length == 0) continue;

            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            if (parent.Equals("journey", StringComparison.OrdinalIgnoreCase))
            {
                if (ListFor(cfg, key) is List<Brush> target)
                {
                    // Flow list: ["#a", "#b"]
                    foreach (var item in value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (ParseBrush(Dequote(item)) is Brush b) target.Add(b);
                    continue;
                }
                switch (key.ToLowerInvariant())
                {
                    case "width":        if (TryNum(Dequote(value), out var w))  cfg.Width        = w;  break;
                    case "height":       if (TryNum(Dequote(value), out var h))  cfg.Height       = h;  break;
                    case "boxmargin":    if (TryNum(Dequote(value), out var m))  cfg.BoxMargin    = m;  break;
                    case "taskfontsize": if (TryNum(Dequote(value), out var fs)) cfg.TaskFontSize = fs; break;
                }
            }
            else if (parent.Equals("themeVariables", StringComparison.OrdinalIgnoreCase))
            {
                string k = key.ToLowerInvariant();
                if (k.StartsWith("filltype") && int.TryParse(k["filltype".Length..], out int idx) && ParseBrush(Dequote(value)) is Brush b)
                    fillTypes[idx] = b;
            }
        }

        if (cfg.SectionFills.Count == 0) cfg.SectionFills.AddRange(fillTypes.Values);
    }

    private static List<Brush>? ListFor(JourneyConfig cfg, string key) => key.ToLowerInvariant() switch
    {
        "actorcolours" or "actorcolors" => cfg.ActorColours,
        "sectionfills"                  => cfg.SectionFills,
        _                               => null,
    };

    private static string Dequote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1];
        return s.Trim();
    }

    private static bool TryNum(string v, out double value)
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
