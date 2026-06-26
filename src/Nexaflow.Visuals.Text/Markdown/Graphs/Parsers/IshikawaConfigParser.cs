using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>ishikawa</c> front-matter config block (<c>config: ishikawa:</c>) into an
/// <see cref="IshikawaConfig"/>.  The documented surface is just <c>diagramPadding</c> and
/// <c>useMaxWidth</c>.  Same shallow, indentation-aware reader as the other diagram config parsers;
/// never throws.
/// </summary>
public static class IshikawaConfigParser
{
    public static IshikawaConfig Parse(string? frontMatter)
    {
        var cfg = new IshikawaConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, IshikawaConfig cfg)
    {
        var stack = new List<(int indent, string key)>();

        foreach (var raw in yaml.Split('\n'))
        {
            var t = raw.TrimStart();
            if (t.Length == 0 || t[0] == '#') continue;

            int indent = raw.Length - t.Length;
            int colon = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            if (stack.Count == 0 || !stack[^1].key.Equals("ishikawa", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (key.ToLowerInvariant())
            {
                case "diagrampadding":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p >= 0)
                        cfg.DiagramPadding = p;
                    break;
                case "usemaxwidth":
                    cfg.UseMaxWidth = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
    }
}
