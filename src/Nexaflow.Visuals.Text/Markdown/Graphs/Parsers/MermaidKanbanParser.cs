using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>kanban</c> boards.  The first keyword is <c>kanban</c>; every line after it is
/// either a column or a card, distinguished purely by source indentation — the shallowest lines are
/// columns, anything indented further is a card of the most recent column.  A node is written
/// <c>id[Title]</c>, <c>[Title]</c> (id defaults to the title) or bare <c>Title</c>, optionally
/// followed by a <c>@{ key: value, … }</c> metadata block whose keys are <c>assigned</c>,
/// <c>ticket</c> and <c>priority</c> (<c>Very High</c> / <c>High</c> / <c>Low</c> / <c>Very Low</c>).
/// </summary>
public sealed class MermaidKanbanParser
{
    public bool CanParse(string language) =>
        language.Equals("kanban", StringComparison.OrdinalIgnoreCase);

    public KanbanBoard Parse(string source)
    {
        var board = new KanbanBoard();
        try { ParseInto(source, board); } catch { /* never throw; return partial */ }
        return board;
    }

    private static readonly Regex RxNode = new(@"^(?<id>[^\[\]@]*)\[(?<title>.*)\]$", RegexOptions.Compiled);
    private static readonly Regex RxBr   = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseInto(string source, KanbanBoard board)
    {
        // Collect the meaningful lines first so the column indent can be taken as the shallowest
        // of them — robust against the whole board being indented or the first column being deep.
        var lines = new List<(int indent, string text)>();
        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var trimmed = line.Trim();
            if (trimmed.Equals("kanban", StringComparison.OrdinalIgnoreCase)) continue;   // header

            lines.Add((LeadingWidth(line), trimmed));
        }
        if (lines.Count == 0) return;

        int columnIndent = lines.Min(l => l.indent);
        KanbanColumn? current = null;

        foreach (var (indent, text) in lines)
        {
            var (id, title, meta) = ParseNode(text);

            if (current is null || indent <= columnIndent)
            {
                current = new KanbanColumn { Id = id, Title = title };
                board.Columns.Add(current);
            }
            else
            {
                var item = new KanbanItem { Id = id, Text = title };
                if (meta is not null) ApplyMetadata(item, meta);
                current.Items.Add(item);
            }
        }
    }

    // ── Node + metadata parsing ────────────────────────────────────────────

    /// <summary>Splits a line into (id, title, metadata-body).  Metadata is the text inside a
    /// trailing <c>@{ … }</c>; the node is <c>id[Title]</c>, <c>[Title]</c> or bare <c>Title</c>.</summary>
    private static (string id, string title, string? meta) ParseNode(string content)
    {
        string? meta = null;
        int at = content.IndexOf("@{", StringComparison.Ordinal);
        if (at >= 0)
        {
            int close = content.LastIndexOf('}');
            if (close > at + 1) meta = content[(at + 2)..close];
            content = content[..at];
        }
        content = content.Trim();

        var m = RxNode.Match(content);
        if (m.Success)
        {
            string id    = m.Groups["id"].Value.Trim();
            string title = Br(m.Groups["title"].Value.Trim());
            return (id.Length == 0 ? title : id, title, meta);
        }

        var bare = Br(content);
        return (bare, bare, meta);
    }

    private static void ApplyMetadata(KanbanItem item, string meta)
    {
        foreach (var pair in meta.Split(','))
        {
            int colon = pair.IndexOf(':');
            if (colon < 0) continue;

            string key = pair[..colon].Trim().Trim('"', '\'').ToLowerInvariant();
            string val = pair[(colon + 1)..].Trim().Trim('"', '\'').Trim();
            if (val.Length == 0) continue;

            switch (key)
            {
                case "assigned": item.Assigned = val; break;
                case "ticket":   item.Ticket   = val; break;
                case "priority": item.Priority = ParsePriority(val); break;
            }
        }
    }

    private static KanbanPriority ParsePriority(string val) =>
        val.Replace(" ", "").ToLowerInvariant() switch
        {
            "veryhigh" => KanbanPriority.VeryHigh,
            "high"     => KanbanPriority.High,
            "low"      => KanbanPriority.Low,
            "verylow"  => KanbanPriority.VeryLow,
            _          => KanbanPriority.None,
        };

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int LeadingWidth(string line)
    {
        int w = 0;
        foreach (char c in line)
        {
            if (c == ' ') w += 1;
            else if (c == '\t') w += 4;
            else break;
        }
        return w;
    }

    private static string Br(string s) => RxBr.Replace(s, "\n").Trim();

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
