using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>xychart</c> / <c>xychart-beta</c> diagrams.
///
/// Syntax:
/// <code>
/// xychart[-beta] [horizontal|vertical]
///     title "&lt;text&gt;"
///     x-axis "&lt;title&gt;" [cat1, "cat 2", cat3]     (categorical)
///     x-axis "&lt;title&gt;" &lt;min&gt; --&gt; &lt;max&gt;        (numeric range — either axis)
///     y-axis "&lt;title&gt;" &lt;min&gt; --&gt; &lt;max&gt;
///     bar  "&lt;name&gt;" [v1, v2, …]                  (name optional; opts into the legend)
///     line "&lt;name&gt;" [v1 "label", v2, …]          (per-point labels on line plots)
/// </code>
/// Text values need quotes only when they contain spaces.  Axis titles and explicit ranges are
/// optional (the renderer auto-ranges from the data).  Values may be signed, decimal or leading-dot
/// (<c>+1.3</c>, <c>.6</c>, <c>-.34</c>).  <c>%%</c> lines are comments.  The front-matter
/// <c>config:</c> block is parsed separately by <see cref="XyChartConfigParser"/>.
/// </summary>
public sealed class MermaidXyChartParser
{
    public bool CanParse(string language) =>
        language.StartsWith("xychart", StringComparison.OrdinalIgnoreCase);

    public XyChart Parse(string source)
    {
        var chart = new XyChart();
        try { ParseInto(source, chart); }
        catch { /* never throw; return partial chart */ }
        return chart;
    }

    private static readonly Regex RxTitle =
        new(@"^title\s+(?<t>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseInto(string source, XyChart chart)
    {
        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            // Declaration line: "xychart" / "xychart-beta", optional orientation keyword.
            if (line.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(line, @"\bhorizontal\b", RegexOptions.IgnoreCase))
                    chart.Orientation = XyOrientation.Horizontal;
                continue;
            }

            var tm = RxTitle.Match(line);
            if (tm.Success) { chart.Title = Dequote(tm.Groups["t"].Value); continue; }

            if (line.StartsWith("x-axis", StringComparison.OrdinalIgnoreCase))
            { ParseAxis(line["x-axis".Length..], chart.XAxis); continue; }

            if (line.StartsWith("y-axis", StringComparison.OrdinalIgnoreCase))
            { ParseAxis(line["y-axis".Length..], chart.YAxis); continue; }

            if (line.StartsWith("bar", StringComparison.OrdinalIgnoreCase) && HasBracket(line))
            { ParseSeries(line["bar".Length..], XySeriesKind.Bar, chart); continue; }

            if (line.StartsWith("line", StringComparison.OrdinalIgnoreCase) && HasBracket(line))
            { ParseSeries(line["line".Length..], XySeriesKind.Line, chart); continue; }
        }
    }

    // ── Axis ───────────────────────────────────────────────────────────────

    private static void ParseAxis(string rest, XyAxis axis)
    {
        rest = rest.Trim();
        if (rest.Length == 0) return;

        int lb = rest.IndexOf('[');
        if (lb >= 0)
        {
            axis.Title = Dequote(rest[..lb].Trim());
            int rb = rest.LastIndexOf(']');
            string inner = rb > lb ? rest[(lb + 1)..rb] : rest[(lb + 1)..];
            foreach (var cat in SplitList(inner))
                axis.Categories.Add(cat);
            return;
        }

        int arrow = rest.IndexOf("-->", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            string before = rest[..arrow].Trim();
            string maxStr = rest[(arrow + 3)..].Trim();

            string title, minStr;
            if (before.StartsWith('"'))
            {
                int q2 = before.IndexOf('"', 1);
                title  = q2 > 0 ? before[1..q2] : before.Trim('"');
                minStr = q2 > 0 ? before[(q2 + 1)..].Trim() : string.Empty;
            }
            else
            {
                var toks = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 0) { title = string.Empty; minStr = string.Empty; }
                else { minStr = toks[^1]; title = string.Join(' ', toks[..^1]); }
            }

            axis.Title = Dequote(title);
            if (TryNum(minStr, out double mn)) axis.Min = mn;
            if (TryNum(maxStr, out double mx)) axis.Max = mx;
            return;
        }

        // Title only — range is auto-generated from the data.
        axis.Title = Dequote(rest);
    }

    // ── Series ───────────────────────────────────────────────────────────────

    private static void ParseSeries(string rest, XySeriesKind kind, XyChart chart)
    {
        rest = rest.Trim();
        int lb = rest.IndexOf('[');
        if (lb < 0) return;

        string namePart = rest[..lb].Trim();
        string? name = namePart.Length > 0 ? Dequote(namePart) : null;

        int rb = rest.LastIndexOf(']');
        string inner = rb > lb ? rest[(lb + 1)..rb] : rest[(lb + 1)..];

        var series = new XySeries { Kind = kind, Name = name };
        foreach (var entry in SplitList(inner))
        {
            int q = entry.IndexOf('"');
            string numStr;
            string? label = null;
            if (q >= 0)
            {
                numStr = entry[..q].Trim();
                int q2 = entry.IndexOf('"', q + 1);
                label  = q2 > q ? entry[(q + 1)..q2] : entry[(q + 1)..].Trim('"');
            }
            else
            {
                numStr = entry.Trim();
            }

            if (TryNum(numStr, out double v))
                series.Points.Add(new XyPoint { Value = v, Label = string.IsNullOrWhiteSpace(label) ? null : label });
        }

        if (series.Points.Count > 0)
            chart.Series.Add(series);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasBracket(string line) => line.Contains('[');

    /// <summary>Splits a comma-separated list, leaving commas inside double-quotes intact, and
    /// returns each element trimmed and de-quoted (empty elements dropped).</summary>
    private static List<string> SplitList(string inner)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char ch in inner)
        {
            if (ch == '"') { inQuotes = !inQuotes; sb.Append(ch); }
            else if (ch == ',' && !inQuotes) { AddTrimmed(result, sb); sb.Clear(); }
            else sb.Append(ch);
        }
        AddTrimmed(result, sb);
        return result;

        static void AddTrimmed(List<string> list, System.Text.StringBuilder sb)
        {
            string s = Dequote(sb.ToString().Trim());
            if (s.Length > 0) list.Add(s);
        }
    }

    private static string Dequote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];
        return s.Trim();
    }

    private static bool TryNum(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
