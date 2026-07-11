using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>architecture</c> front-matter config block into an <see cref="ArchitectureConfig"/>:
/// the <c>config: architecture:</c> keys (<c>nodeSeparation</c>, <c>randomize</c>, <c>seed</c>,
/// <c>idealEdgeLengthMultiplier</c>).  Same shallow, indentation-aware reader as the other diagram config
/// parsers; never throws.
/// </summary>
public static class ArchitectureConfigParser
{
    public static ArchitectureConfig Parse(string? frontMatter)
    {
        var cfg = new ArchitectureConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, ArchitectureConfig cfg)
    {
        var stack = new List<(int indent, string key)>();

        foreach (var raw in yaml.Split('\n'))
        {
            var ts = raw.TrimStart();
            if (ts.Length == 0 || ts[0] == '#') continue;

            int indent = raw.Length - ts.Length;
            int colon  = raw.IndexOf(':');
            if (colon < 0) continue;

            string key   = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (parent.Equals("architecture", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "nodeseparation":            if (TryNum(value, out var n)) cfg.NodeSeparation = n; break;
                    case "randomize":                 cfg.Randomize = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "seed":                      if (int.TryParse(value, out var s)) cfg.Seed = s; break;
                    case "idealedgelengthmultiplier": if (TryNum(value, out var m)) cfg.IdealEdgeLengthMultiplier = m; break;
                }
            }
        }
    }

    private static bool TryNum(string v, out double value) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
