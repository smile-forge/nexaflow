using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>sankey</c> diagrams.  After the <c>sankey</c> keyword the body is RFC-4180 CSV with
/// three columns — <c>source,target,value</c> — one link per row.  Nodes are inferred from the names in
/// first-appearance order.  Fields containing commas are wrapped in double quotes, and a literal quote is
/// a doubled <c>""</c> inside a quoted field; blank lines and <c>%%</c> comment lines are ignored.
/// </summary>
public sealed class MermaidSankeyParser
{
    public bool CanParse(string language) =>
        language.StartsWith("sankey", StringComparison.OrdinalIgnoreCase);

    public SankeyDiagram Parse(string source)
    {
        var diagram = new SankeyDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, SankeyDiagram diagram)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int NodeIndex(string name)
        {
            if (index.TryGetValue(name, out int i)) return i;
            i = diagram.Nodes.Count;
            index[name] = i;
            diagram.Nodes.Add(new SankeyNode { Name = name });
            return i;
        }

        bool sawKeyword = false;
        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            if (!sawKeyword)
            {
                sawKeyword = true;
                if (line.StartsWith("sankey", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count < 3) continue;

            string src = fields[0].Trim();
            string tgt = fields[1].Trim();
            if (src.Length == 0 || tgt.Length == 0) continue;
            if (!double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;

            diagram.Links.Add(new SankeyLink { Source = NodeIndex(src), Target = NodeIndex(tgt), Value = value });
        }
    }

    /// <summary>Splits one CSV record into fields (RFC 4180: quoted fields keep commas; <c>""</c> is a literal quote).</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
