using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>cynefin</c> front-matter config block into a <see cref="CynefinConfig"/>: the
/// <c>config: cynefin:</c> keys (<c>width</c>/<c>height</c>/<c>padding</c>/<c>showDomainDescriptions</c>)
/// and the <c>config: themeVariables: cynefin:</c> domain backgrounds (<c>complexBg</c>/<c>complicatedBg</c>/
/// <c>clearBg</c>/<c>chaoticBg</c>/<c>confusionBg</c>/<c>boundaryColor</c>).  Same shallow,
/// indentation-aware reader as the other diagram config parsers; never throws.
/// </summary>
public static class CynefinConfigParser
{
    public static CynefinConfig Parse(string? frontMatter)
    {
        var cfg = new CynefinConfig();
        if (string.IsNullOrWhiteSpace(frontMatter)) return cfg;
        try { ParseInto(frontMatter!, cfg); }
        catch { /* never throw */ }
        return cfg;
    }

    private static void ParseInto(string yaml, CynefinConfig cfg)
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

            // Both `config: cynefin:` and `config: themeVariables: cynefin:` present `cynefin` as the
            // immediate parent, and their key sets are disjoint, so one branch handles both blocks.
            string parent = stack.Count > 0 ? stack[^1].key : string.Empty;
            if (parent.Equals("cynefin", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "width":                  if (TryNum(value, out var w)) cfg.Width   = w; break;
                    case "height":                 if (TryNum(value, out var h)) cfg.Height  = h; break;
                    case "padding":                if (TryNum(value, out var p)) cfg.Padding = p; break;
                    case "showdomaindescriptions": cfg.ShowDomainDescriptions = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "complexbg":              cfg.ComplexBg     = ParseBrush(value) ?? cfg.ComplexBg;     break;
                    case "complicatedbg":          cfg.ComplicatedBg = ParseBrush(value) ?? cfg.ComplicatedBg; break;
                    case "clearbg":                cfg.ClearBg       = ParseBrush(value) ?? cfg.ClearBg;       break;
                    case "chaoticbg":              cfg.ChaoticBg     = ParseBrush(value) ?? cfg.ChaoticBg;     break;
                    case "confusionbg":            cfg.ConfusionBg   = ParseBrush(value) ?? cfg.ConfusionBg;   break;
                    case "boundarycolor":          cfg.BoundaryColor = ParseBrush(value) ?? cfg.BoundaryColor; break;
                }
            }
        }
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
