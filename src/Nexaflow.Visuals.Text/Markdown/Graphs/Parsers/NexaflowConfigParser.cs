using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses the <c>config: nexaflow:</c> front-matter block into a <see cref="NexaflowGraphConfig"/>.
/// Same shallow, indentation-aware reader as <see cref="ErConfigParser"/> / <see cref="RadarConfigParser"/>;
/// never throws, and every key it does not recognise is skipped — which is the whole point of the
/// namespace, since stock mermaid skips the block the same way.
/// <para>
/// <c>collapsed</c> / <c>expanded</c> accept either shape: a flow list (<c>[n1, n2]</c>) when the ids
/// are all the producer needs, or a nested block (<c>n1: KERNEL32.dll</c>) when it wants its own key
/// echoed back on the expand request.
/// </para>
/// </summary>
public static class NexaflowConfigParser
{
    private const string Section = "nexaflow";

    public static NexaflowGraphConfig Parse(string? frontMatter)
    {
        var cfg = new NexaflowGraphConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw; return whatever was parsed */ }
        return cfg;
    }

    private static void ParseInto(string yaml, NexaflowGraphConfig cfg)
    {
        var stack = new List<(int indent, string key)>();

        foreach (var raw in yaml.Split('\n'))
        {
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            int indent = raw.Length - trimmed.Length;

            // A block-sequence entry ("- n3") under collapsed:/expanded:.
            if (trimmed[0] == '-' && (trimmed.Length == 1 || trimmed[1] != '-'))
            {
                if (SectionMap(cfg, stack) is { } seq)
                {
                    string item = Clean(trimmed[1..]);
                    if (item.Length > 0) seq[item] = item;
                }
                continue;
            }

            int colon = trimmed.IndexOf(':');
            if (colon < 0) continue;

            string key   = trimmed[..colon].Trim();
            string value = Clean(trimmed[(colon + 1)..]);
            if (key.Length == 0) continue;

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            if (value.Length == 0) { stack.Add((indent, key)); continue; }

            // "collapsed: [n1, n2]" — the list form, which opens and closes on one line.
            if (IsUnder(stack, Section) && TryMapFor(cfg, key) is { } target)
            {
                foreach (var id in SplitFlowList(value)) target[id] = id;
                continue;
            }

            // "n3: KERNEL32.dll" — a member of the nested block form.
            if (SectionMap(cfg, stack) is { } map) { map[key] = value; continue; }

            if (!IsUnder(stack, Section)) continue;

            switch (key.ToLowerInvariant())
            {
                case "expanddepth": if (TryInt(value, out var d) && d >= 0) cfg.ExpandDepth = d; break;
                case "maxfanout":   if (TryInt(value, out var f) && f >= 0) cfg.MaxFanOut   = f; break;
            }
        }
    }

    /// <summary>The collapsed/expanded map when the reader is currently inside one, else null.</summary>
    private static Dictionary<string, string>? SectionMap(
        NexaflowGraphConfig cfg, List<(int indent, string key)> stack)
    {
        if (stack.Count < 2 || !stack[^2].key.Equals(Section, StringComparison.OrdinalIgnoreCase))
            return null;
        return TryMapFor(cfg, stack[^1].key);
    }

    private static Dictionary<string, string>? TryMapFor(NexaflowGraphConfig cfg, string key) =>
        key.Equals("collapsed", StringComparison.OrdinalIgnoreCase) ? cfg.Collapsed
      : key.Equals("expanded",  StringComparison.OrdinalIgnoreCase) ? cfg.Expanded
      : null;

    /// <summary>True when the innermost open block is <paramref name="section"/>.</summary>
    private static bool IsUnder(List<(int indent, string key)> stack, string section) =>
        stack.Count > 0 && stack[^1].key.Equals(section, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitFlowList(string value)
    {
        if (value.Length < 2 || value[0] != '[' || value[^1] != ']') yield break;
        foreach (var part in value[1..^1].Split(','))
        {
            string item = Clean(part);
            if (item.Length > 0) yield return item;
        }
    }

    private static string Clean(string raw)
    {
        string s = raw.Trim();
        if (s.Length == 0) return s;
        if (s[0] is '"' or '\'')
        {
            char quote = s[0];
            int end = s.IndexOf(quote, 1);
            return end > 0 ? s[1..end] : s[1..].Trim();
        }
        int hash = s.IndexOf('#');
        if (hash > 0 && char.IsWhiteSpace(s[hash - 1])) s = s[..hash].Trim();
        return s.Trim();
    }

    private static bool TryInt(string v, out int value) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
