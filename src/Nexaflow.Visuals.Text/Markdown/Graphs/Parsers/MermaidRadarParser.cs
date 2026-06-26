using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>radar-beta</c> diagrams (radar / spider / Kiviat charts).
///
/// Syntax:
/// <code>
/// radar-beta
///     title &lt;text&gt;
///     axis A, B, C                       (bare ids)
///     axis m["Math"], s["Science"]       (id + label; several per line)
///     curve c1{1, 2, 3}                  (positional — maps to axes in order)
///     curve a["Alice"]{85, 90, 80}       (id + label)
///     curve id{ axis3: 30, axis1: 20 }   (keyed — maps by axis id)
///     max 100        min 0        ticks 5
///     graticule circle|polygon           showLegend true|false
/// </code>
/// Several axes / curves may be packed onto one comma-separated line; axis labels and the title need
/// quotes only when they contain spaces.  The front-matter <c>config:</c> block is parsed separately by
/// <see cref="RadarConfigParser"/>.
/// </summary>
public sealed class MermaidRadarParser
{
    public bool CanParse(string language) =>
        language.StartsWith("radar", StringComparison.OrdinalIgnoreCase);

    public RadarChart Parse(string source)
    {
        var chart = new RadarChart();
        try { ParseInto(source, chart); }
        catch { /* never throw; return partial chart */ }
        return chart;
    }

    private static void ParseInto(string source, RadarChart chart)
    {
        var rawCurves = new List<string>();   // resolved in a second pass, once all axes are known

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            string keyword = FirstToken(line).ToLowerInvariant();
            string rest = line.Length > keyword.Length ? line[keyword.Length..].Trim() : string.Empty;

            switch (keyword)
            {
                case "radar-beta":
                case "radar":
                    break;
                case "title":
                    chart.Title = Dequote(rest);
                    break;
                case "axis":
                    foreach (var item in SplitTopLevel(rest))
                        chart.Axes.Add(ParseAxis(item));
                    break;
                case "curve":
                    rawCurves.AddRange(SplitTopLevel(rest));
                    break;
                case "max":
                    if (TryNum(rest, out double mx)) chart.Max = mx;
                    break;
                case "min":
                    if (TryNum(rest, out double mn)) chart.Min = mn;
                    break;
                case "ticks":
                    if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)) chart.Ticks = Math.Max(1, t);
                    break;
                case "showlegend":
                    chart.ShowLegend = !rest.Equals("false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "graticule":
                    chart.Graticule = rest.StartsWith("p", StringComparison.OrdinalIgnoreCase)
                        ? RadarGraticule.Polygon : RadarGraticule.Circle;
                    break;
            }
        }

        foreach (var raw in rawCurves)
            if (ParseCurve(raw, chart.Axes) is RadarCurve c)
                chart.Curves.Add(c);
    }

    // ── Axis / curve items ───────────────────────────────────────────────────

    private static RadarAxis ParseAxis(string item)
    {
        item = item.Trim();
        int lb = item.IndexOf('[');
        if (lb < 0) return new RadarAxis { Id = item, Label = string.Empty };

        int rb = item.LastIndexOf(']');
        string id    = item[..lb].Trim();
        string label = Dequote(rb > lb ? item[(lb + 1)..rb] : item[(lb + 1)..]);
        return new RadarAxis { Id = id, Label = label };
    }

    private static RadarCurve? ParseCurve(string item, List<RadarAxis> axes)
    {
        item = item.Trim();
        int br = item.IndexOf('{');
        if (br < 0) return null;

        string head = item[..br].Trim();
        int rb = item.LastIndexOf('}');
        string body = rb > br ? item[(br + 1)..rb] : item[(br + 1)..];

        // head = "id" or "id[\"Label\"]"
        string id, label;
        int lb = head.IndexOf('[');
        if (lb < 0) { id = head; label = string.Empty; }
        else
        {
            int hrb = head.LastIndexOf(']');
            id = head[..lb].Trim();
            label = Dequote(hrb > lb ? head[(lb + 1)..hrb] : head[(lb + 1)..]);
        }

        var curve = new RadarCurve { Id = id, Label = label };
        var values = new double?[axes.Count];

        bool keyed = body.Contains(':');
        if (keyed)
        {
            foreach (var part in body.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                string axisId = Dequote(part[..colon].Trim());
                int idx = axes.FindIndex(a => a.Id.Equals(axisId, StringComparison.Ordinal));
                if (idx >= 0 && TryNum(part[(colon + 1)..], out double v)) values[idx] = v;
            }
        }
        else
        {
            var parts = body.Split(',');
            for (int i = 0; i < parts.Length && i < values.Length; i++)
                if (TryNum(parts[i], out double v)) values[i] = v;
        }

        curve.Values.AddRange(values);
        return curve;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FirstToken(string line)
    {
        int i = 0;
        while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
        return line[..i];
    }

    /// <summary>Splits on top-level commas, leaving commas inside <c>[]</c>/<c>{}</c>/quotes intact.</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int depth = 0;
        foreach (char ch in s)
        {
            if (ch == '"') { inQuotes = !inQuotes; sb.Append(ch); }
            else if (!inQuotes && (ch == '[' || ch == '{')) { depth++; sb.Append(ch); }
            else if (!inQuotes && (ch == ']' || ch == '}')) { depth = Math.Max(0, depth - 1); sb.Append(ch); }
            else if (ch == ',' && !inQuotes && depth == 0) { Flush(result, sb); }
            else sb.Append(ch);
        }
        Flush(result, sb);
        return result;

        static void Flush(List<string> list, StringBuilder sb)
        {
            string s = sb.ToString().Trim();
            sb.Clear();
            if (s.Length > 0) list.Add(s);
        }
    }

    private static string Dequote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Trim();
    }

    private static bool TryNum(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
