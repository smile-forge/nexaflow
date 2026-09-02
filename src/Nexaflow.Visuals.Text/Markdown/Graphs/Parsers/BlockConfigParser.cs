using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>block</c> front-matter config block into a <see cref="BlockConfig"/>: the
/// <c>config: block:</c> keys <c>padding</c> and <c>useMaxWidth</c>.  Same shallow, indentation-aware
/// reader as the other diagram config parsers; never throws.
/// </summary>
public static class BlockConfigParser
{
    public static BlockConfig Parse(string? frontMatter)
    {
        var cfg = new BlockConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, BlockConfig cfg)
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
            string value = raw[(colon + 1)..].Trim().Trim('"', '\'');
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (!parent.Equals("block", StringComparison.OrdinalIgnoreCase)) continue;

            switch (key.ToLowerInvariant())
            {
                case "padding":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) cfg.Padding = Math.Max(0, p);
                    break;
                case "usemaxwidth":
                    cfg.UseMaxWidth = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
    }
}
