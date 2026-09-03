using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>c4</c> front-matter config block into a <see cref="C4Config"/>: the
/// <c>config: c4:</c> keys <c>wrap</c>, <c>c4ShapeInRow</c>, <c>c4BoundaryInRow</c>, <c>width</c>
/// and <c>height</c>. Same shallow, indentation-aware reader as the other diagram config parsers;
/// never throws.
///
/// The two <c>*InRow</c> keys are recorded rather than obeyed: they exist to steer Mermaid's
/// statement-order grid, and the elements here are placed by the shared Sugiyama layout instead.
/// Keeping them parsed means a diagram written for Mermaid still loads, and means the values are
/// there if the layout ever wants a hint.
/// </summary>
public static class C4ConfigParser
{
    public static C4Config Parse(string? frontMatter)
    {
        var cfg = new C4Config();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, C4Config cfg)
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
            if (!parent.Equals("c4", StringComparison.OrdinalIgnoreCase)) continue;

            switch (key.ToLowerInvariant())
            {
                case "wrap":            cfg.Wrap = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "c4shapeinrow":    if (TryInt(value, out int s)) cfg.C4ShapeInRow = s; break;
                case "c4boundaryinrow": if (TryInt(value, out int b)) cfg.C4BoundaryInRow = b; break;
                case "width":           if (TryNum(value, out double w)) cfg.Width = w; break;
                case "height":          if (TryNum(value, out double h)) cfg.Height = h; break;
            }
        }
    }

    private static bool TryInt(string v, out int value) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryNum(string v, out double value) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
