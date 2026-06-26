using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>erDiagram</c> front-matter config block (<c>config: er:</c>) into an
/// <see cref="ErConfig"/>.  Same shallow, indentation-aware reader as the other diagram config parsers;
/// never throws.
/// </summary>
public static class ErConfigParser
{
    public static ErConfig Parse(string? frontMatter)
    {
        var cfg = new ErConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, ErConfig cfg)
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
            if (stack.Count == 0 || !stack[^1].key.Equals("er", StringComparison.OrdinalIgnoreCase)) continue;

            switch (key.ToLowerInvariant())
            {
                case "fill":            cfg.Fill   = value; break;
                case "stroke":          cfg.Stroke = value; break;
                case "fontsize":        if (TryInt(value, out var fs)) cfg.FontSize = fs; break;
                case "titletopmargin":  if (TryInt(value, out var tm)) cfg.TitleTopMargin = tm; break;
                case "diagrampadding":  if (TryInt(value, out var dp)) cfg.DiagramPadding = dp; break;
                case "minentitywidth":  if (TryInt(value, out var mw)) cfg.MinEntityWidth = mw; break;
                case "minentityheight": if (TryInt(value, out var mh)) cfg.MinEntityHeight = mh; break;
                case "entitypadding":   if (TryInt(value, out var ep)) cfg.EntityPadding = ep; break;
                case "nodespacing":     if (TryInt(value, out var ns)) cfg.NodeSpacing = ns; break;
                case "rankspacing":     if (TryInt(value, out var rs)) cfg.RankSpacing = rs; break;
                case "usemaxwidth":     cfg.UseMaxWidth = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "layoutdirection":
                    cfg.LayoutDirection = value.ToUpperInvariant() switch
                    {
                        "LR" => GraphDirection.LeftRight,
                        "RL" => GraphDirection.RightLeft,
                        "BT" => GraphDirection.BottomUp,
                        "TB" => GraphDirection.TopDown,
                        _    => cfg.LayoutDirection,
                    };
                    break;
            }
        }
    }

    private static bool TryInt(string v, out int value) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
