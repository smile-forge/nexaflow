using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>pie</c> charts.
///
/// Syntax:
/// <code>
/// pie [showData] [title &lt;text&gt;]
///     [title &lt;text&gt;]
///     "&lt;label&gt;" : &lt;value&gt;
/// </code>
/// </summary>
public sealed class MermaidPieParser
{
    public bool CanParse(string language) =>
        language.Equals("pie", StringComparison.OrdinalIgnoreCase);

    public PieChart Parse(string source)
    {
        var chart = new PieChart();
        try { ParseInto(source, chart); }
        catch { }
        return chart;
    }

    // ── Regexes ────────────────────────────────────────────────────────────

    // Header: pie [showData] [title <text>]
    private static readonly Regex RxHeader =
        new(@"^\s*pie\s*(?<sd>showData)?\s*(?:title\s+(?<title>.+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Inline title on its own line: title <text>
    private static readonly Regex RxTitle =
        new(@"^\s*title\s+(?<title>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Data line: "label" : value
    private static readonly Regex RxSlice =
        new(@"^\s*""(?<label>[^""]+)""\s*:\s*(?<val>[\d.]+)\s*$",
            RegexOptions.Compiled);

    // ── Parser ─────────────────────────────────────────────────────────────

    private static void ParseInto(string source, PieChart chart)
    {
        bool headerSeen = false;

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (!headerSeen)
            {
                var hm = RxHeader.Match(line);
                if (hm.Success)
                {
                    chart.ShowData = hm.Groups["sd"].Success;
                    if (hm.Groups["title"].Success)
                        chart.Title = hm.Groups["title"].Value.Trim();
                    headerSeen = true;
                    continue;
                }
            }

            // Stand-alone title line (Mermaid allows it after the pie keyword)
            var tm = RxTitle.Match(line);
            if (tm.Success)
            {
                chart.Title = tm.Groups["title"].Value.Trim();
                continue;
            }

            // Data slice
            var sm = RxSlice.Match(line);
            if (sm.Success && double.TryParse(sm.Groups["val"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double val))
            {
                chart.Slices.Add(new PieSlice { Label = sm.Groups["label"].Value, Value = val });
            }
        }
    }
}
