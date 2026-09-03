using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>block-beta</c> (also plain <c>block</c>) diagrams.
///
/// Syntax:
/// <code>
/// block-beta
///   columns 3                         (grid width; "auto" or absent = one row)
///   a["A label"] b:2 c                (a row of items; id:N spans N columns)
///   space  space:2                    (empty cells)
///   id&lt;["Label"]&gt;(right)            (block arrow; right/left/up/down/x/y, comma-combinable)
///   block:group1:2                    (composite block spanning 2 columns …)
///     columns 2
///     d e
///   end                               (… with its own grid; `block` alone is anonymous)
///   a --&gt; b   a --- b   a -- "label" --&gt; b
///   style a fill:#f9f,stroke:#333,stroke-width:2px,color:#fff,stroke-dasharray: 5 5
///   classDef blue fill:#66f;   class a,b blue
/// </code>
/// Every flowchart bracket shape is recognised — <c>()</c> <c>([])</c> <c>[[]]</c> <c>[()]</c> <c>(())</c>
/// <c>((()))</c> <c>&gt;]</c> <c>{}</c> <c>{{}}</c> <c>[//]</c> <c>[\\]</c> <c>[/\]</c> <c>[\/]</c>.
/// Labels may use <c>&lt;br&gt;</c> and HTML entities (<c>&amp;nbsp;</c>).  A <c>style</c> or
/// <c>class</c> may precede the item it names.  The front-matter <c>config:</c> block is parsed
/// separately by <see cref="BlockConfigParser"/>.  Never throws; returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidBlockParser
{
    private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // a --> b | a --- b | a -- "label" --> b | a -->|label| b   (either end may carry a shape: id("x"))
    private static readonly Regex RxEdge = new(
        @"^(?<a>.+?)\s*-{2,}(?:\s*""(?<l1>[^""]*)""\s*-{2,})?(?<head>>?)(?:\|""?(?<l2>[^|""]*)""?\|)?\s*(?<b>[A-Za-z0-9_.].*)$",
        RegexOptions.Compiled);

    public bool CanParse(string language) =>
        language.StartsWith("block", StringComparison.OrdinalIgnoreCase);

    public BlockDiagram Parse(string source)
    {
        var diagram = new BlockDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, BlockDiagram diagram)
    {
        var groups    = new Stack<BlockGroup>();
        groups.Push(diagram.Root);
        var classDefs = new Dictionary<string, BlockStyle>(StringComparer.Ordinal);
        var classes   = new List<(string id, string cls)>();
        var styles    = new List<(string id, BlockStyle style)>();
        bool headerSeen = false;
        int anonymous = 0;

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string firstRaw = line.Split(' ', '\t')[0];
            string first    = firstRaw.ToLowerInvariant();
            string rest     = line.Length > firstRaw.Length ? line[firstRaw.Length..].Trim() : string.Empty;

            // The first line is the header (`block-beta` / `block`); a later bare `block` opens a group.
            if (!headerSeen)
            {
                headerSeen = true;
                if (first.StartsWith("block") && !first.StartsWith("block:")) continue;
            }

            if (first.TrimEnd(':') is "acctitle" or "accdescr") continue;
            if (first == "title") { diagram.Title = CleanLabel(rest); continue; }

            var group = groups.Peek();

            if (first == "columns")
            {
                group.Columns = int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;
                continue;
            }
            if (first == "end")
            {
                if (groups.Count > 1) groups.Pop();
                continue;
            }
            if (first == "block" || first.StartsWith("block:"))
            {
                // block | block:id | block:id:width
                var parts = firstRaw.Split(':');
                string id = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : $"__block{++anonymous}";
                int width = parts.Length > 2 && int.TryParse(parts[2], out int w) && w > 0 ? w : 1;
                var g = new BlockGroup { Id = id, Width = width };
                group.Items.Add(g);
                groups.Push(g);
                continue;
            }
            if (first == "style")
            {
                int sp = rest.IndexOfAny([' ', '\t']);
                if (sp > 0)
                {
                    var style = ParseStyleProps(rest[(sp + 1)..]);
                    foreach (var id in rest[..sp].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        styles.Add((id, style));
                }
                continue;
            }
            if (first == "classdef")
            {
                int sp = rest.IndexOfAny([' ', '\t']);
                if (sp > 0) classDefs[rest[..sp]] = ParseStyleProps(rest[(sp + 1)..]);
                continue;
            }
            if (first == "class")
            {
                int sp = rest.IndexOfAny([' ', '\t']);
                if (sp > 0)
                {
                    string cls = rest[(sp + 1)..].Trim().TrimEnd(';');
                    foreach (var id in rest[..sp].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        classes.Add((id, cls));
                }
                continue;
            }

            if (TryParseEdge(line, diagram, group)) continue;

            ParseRow(line, diagram, group);
        }

        // Styling resolves against the finished tree: a class first, an explicit style over it.
        foreach (var (id, cls) in classes)
            if (classDefs.TryGetValue(cls, out var def) && diagram.Find(id) is BlockItem item)
                item.Style = (item.Style ?? new BlockStyle()).Merge(def);
        foreach (var (id, style) in styles)
            if (diagram.Find(id) is BlockItem item)
                item.Style = (item.Style ?? new BlockStyle()).Merge(style);
    }

    // ── Rows & items ─────────────────────────────────────────────────────────

    private static void ParseRow(string line, BlockDiagram diagram, BlockGroup group)
    {
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;

            var item = ReadItem(line, ref i);
            if (item is null) { i++; continue; }        // stray character — skip it, keep the row

            Place(item, diagram, group);
        }
    }

    /// <summary>Adds a freshly read item to <paramref name="group"/>, or folds a re-mention of a known id into that item.</summary>
    private static void Place(BlockItem item, BlockDiagram diagram, BlockGroup group)
    {
        if (item is BlockSpace) { group.Items.Add(item); return; }

        if (diagram.Find(item.Id) is BlockItem existing)
        {
            if (existing is BlockNode en && item is BlockNode nn)
            {
                if (nn.Label != nn.Id || nn.Shape != NodeShape.Rectangle) { en.Label = nn.Label; en.Shape = nn.Shape; }
            }
            if (item.Width > 1) existing.Width = item.Width;
            return;
        }
        group.Items.Add(item);
    }

    /// <summary>Reads one item at <paramref name="i"/>: <c>id</c>, <c>id[shape]</c>, <c>id&lt;[label]&gt;(dirs)</c>, <c>space</c>, each with an optional <c>:N</c>.</summary>
    private static BlockItem? ReadItem(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && IsIdChar(s[i])) i++;
        if (i == start) return null;
        string id = s[start..i];

        // Block arrow: id<["label"]>(right, down)
        if (i + 1 < s.Length && s[i] == '<' && s[i + 1] == '[')
        {
            int close = s.IndexOf("]>", i, StringComparison.Ordinal);
            if (close > 0)
            {
                string arrowLabel = CleanLabel(s[(i + 2)..close]);
                i = close + 2;
                var dirs = BlockArrowDirections.None;
                if (i < s.Length && s[i] == '(')
                {
                    int rp = s.IndexOf(')', i);
                    if (rp > 0) { dirs = ParseDirections(s[(i + 1)..rp]); i = rp + 1; }
                }
                return new BlockArrow
                {
                    Id = id, Label = arrowLabel,
                    Directions = dirs == BlockArrowDirections.None ? BlockArrowDirections.Right : dirs,
                    Width = ReadWidth(s, ref i),
                };
            }
        }

        string? label = null;
        var shape = NodeShape.Rectangle;
        bool shaped = false;
        if (i < s.Length && s[i] is '[' or '(' or '{' or '>')
        {
            int end = ReadBracketGroup(s, i);
            if (end > 0) { (shape, label) = ClassifyShape(s[i..end]); shaped = true; i = end; }
        }
        int width = ReadWidth(s, ref i);

        if (!shaped && id.Equals("space", StringComparison.OrdinalIgnoreCase))
            return new BlockSpace { Width = width };

        return new BlockNode { Id = id, Label = label ?? id, Shape = shape, Width = width };
    }

    private static bool IsIdChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';

    /// <summary>Index just past the bracket group opening at <paramref name="i"/>, or -1 when unbalanced.</summary>
    private static int ReadBracketGroup(string s, int i)
    {
        if (s[i] == '>')
        {
            bool q = false;
            for (int k = i + 1; k < s.Length; k++)
            {
                if (s[k] == '"') q = !q;
                else if (!q && s[k] == ']') return k + 1;
            }
            return -1;
        }

        int depth = 0; bool quoted = false;
        for (int k = i; k < s.Length; k++)
        {
            char c = s[k];
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c is '[' or '(' or '{') depth++;
            else if (c is ']' or ')' or '}' && --depth == 0) return k + 1;
        }
        return -1;
    }

    /// <summary>Maps a bracket group like <c>[/"text"/]</c> to its shape and cleaned label.</summary>
    private static (NodeShape shape, string label) ClassifyShape(string g)
    {
        (NodeShape shape, int n) =
              g.StartsWith("(((") ? (NodeShape.DoubleCircle, 3)
            : g.StartsWith("((")  ? (NodeShape.Circle, 2)
            : g.StartsWith("([")  ? (NodeShape.Stadium, 2)
            : g.StartsWith('(')   ? (NodeShape.RoundedRect, 1)
            : g.StartsWith("[[")  ? (NodeShape.Subroutine, 2)
            : g.StartsWith("[(")  ? (NodeShape.Cylinder, 2)
            : g.StartsWith("[/")  ? (g.EndsWith("/]") ? NodeShape.Parallelogram    : NodeShape.Trapezoid, 2)
            : g.StartsWith("[\\") ? (g.EndsWith("\\]") ? NodeShape.ParallelogramAlt : NodeShape.TrapezoidAlt, 2)
            : g.StartsWith('[')   ? (NodeShape.Rectangle, 1)
            : g.StartsWith("{{")  ? (NodeShape.Hexagon, 2)
            : g.StartsWith('{')   ? (NodeShape.Diamond, 1)
            : g.StartsWith('>')   ? (NodeShape.Asymmetric, 1)
            :                       (NodeShape.Rectangle, 0);

        string inner = g.Length >= 2 * n ? g[n..^n] : string.Empty;
        return (shape, CleanLabel(inner));
    }

    private static int ReadWidth(string s, ref int i)
    {
        if (i < s.Length && s[i] == ':')
        {
            int k = i + 1;
            while (k < s.Length && char.IsDigit(s[k])) k++;
            if (k > i + 1 && int.TryParse(s[(i + 1)..k], out int w)) { i = k; return Math.Max(1, w); }
        }
        return 1;
    }

    private static BlockArrowDirections ParseDirections(string text)
    {
        var dirs = BlockArrowDirections.None;
        foreach (var d in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            dirs |= d.ToLowerInvariant() switch
            {
                "right" => BlockArrowDirections.Right,
                "left"  => BlockArrowDirections.Left,
                "up"    => BlockArrowDirections.Up,
                "down"  => BlockArrowDirections.Down,
                "x"     => BlockArrowDirections.Left | BlockArrowDirections.Right,
                "y"     => BlockArrowDirections.Up | BlockArrowDirections.Down,
                _       => BlockArrowDirections.None,
            };
        return dirs;
    }

    // ── Edges ────────────────────────────────────────────────────────────────

    private static bool TryParseEdge(string line, BlockDiagram diagram, BlockGroup group)
    {
        var m = RxEdge.Match(line);
        if (!m.Success) return false;

        int ia = 0, ib = 0;
        var a = ReadItem(m.Groups["a"].Value.Trim(), ref ia);
        var b = ReadItem(m.Groups["b"].Value.Trim(), ref ib);
        if (a is null || b is null || a is BlockSpace || b is BlockSpace) return false;

        // An endpoint may declare its shape here (`id1("Start")-->id2("Stop")`); an unknown one is created.
        Place(a, diagram, group);
        Place(b, diagram, group);

        string label = m.Groups["l1"].Success ? m.Groups["l1"].Value : m.Groups["l2"].Value;
        diagram.Edges.Add(new BlockEdge { From = a.Id, To = b.Id, Label = CleanLabel(label), HasArrow = m.Groups["head"].Value.Length > 0 });
        return true;
    }

    // ── Styling ──────────────────────────────────────────────────────────────

    /// <summary>Parses <c>fill:#f9f,stroke:#333,stroke-width:2px,color:#fff,stroke-dasharray: 5 5</c>.</summary>
    private static BlockStyle ParseStyleProps(string props)
    {
        var style = new BlockStyle();
        foreach (var prop in props.TrimEnd(';').Split(','))
        {
            var kv = prop.Split(':', 2);
            if (kv.Length != 2) continue;
            string key = kv[0].Trim().ToLowerInvariant(), val = kv[1].Trim().TrimEnd(';');
            if (val.Length == 0 || val == "none") continue;
            switch (key)
            {
                case "fill":   style.Fill      = val; break;
                case "stroke": style.Stroke    = val; break;
                case "color":  style.TextColor = val; break;
                case "stroke-width":
                    if (double.TryParse(val.Replace("px", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var sw))
                        style.StrokeWidth = sw;
                    break;
                case "stroke-dasharray": style.Dashed = val != "0"; break;
            }
        }
        return style;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Dequotes, decodes entities (<c>&amp;nbsp;</c>), turns <c>&lt;br&gt;</c> into a line break.</summary>
    private static string CleanLabel(string raw)
    {
        string s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        s = RxBr.Replace(s, "\n");
        s = WebUtility.HtmlDecode(s).Replace('\u00A0', ' ');   // &nbsp; is padding in Mermaid, not text
        return s.Trim();
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
