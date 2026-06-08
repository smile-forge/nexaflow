using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>mindmap</c> diagrams.  Hierarchy comes from source indentation: a line
/// indented further than the previous one is its child; a line indented less pops back up to the
/// nearest shallower ancestor.  Node shapes come from text delimiters:
/// <c>[square] (rounded) ((circle)) {{hexagon}} )cloud( ))bang((</c>; <c>::icon(...)</c> lines and
/// <c>:::class</c> suffixes are recognised and skipped/stripped, and <c>&lt;br&gt;</c> becomes a line break.
/// </summary>
public sealed class MermaidMindmapParser
{
    public bool CanParse(string language) =>
        language.Equals("mindmap", StringComparison.OrdinalIgnoreCase);

    public Mindmap Parse(string source)
    {
        var m = new Mindmap();
        try { ParseInto(source, m); } catch { /* never throw; return partial */ }
        if (m.Root is not null) AssignDepthAndBranch(m.Root, 0, -1);
        return m;
    }

    private static readonly Regex RxClass = new(@":::[A-Za-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex RxBr    = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseInto(string source, Mindmap m)
    {
        var stack = new List<(MindmapNode node, int indent)>();

        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var trimmed = line.Trim();
            if (trimmed.Equals("mindmap", StringComparison.OrdinalIgnoreCase)) continue;   // header
            if (trimmed.StartsWith("::icon", StringComparison.OrdinalIgnoreCase)) continue; // icon directive

            int indent = LeadingWidth(line);
            var (text, shape) = ParseShape(RxClass.Replace(trimmed, "").Trim());
            if (text.Length == 0) continue;

            var node = new MindmapNode { Text = Br(text), Shape = shape };

            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0)
            {
                if (m.Root is null) m.Root = node;
                else { node.Parent = m.Root; m.Root.Children.Add(node); }   // stray top-level → under root
            }
            else
            {
                var parent = stack[^1].node;
                node.Parent = parent;
                parent.Children.Add(node);
            }
            stack.Add((node, indent));
        }
    }

    // ── Shape detection ──────────────────────────────────────────────────────

    private static (string text, MindmapShape shape) ParseShape(string s)
    {
        // Bang ( [id])) text (( ) and cloud ( [id]) text ( ) close with '((' / '(' — the ending
        // distinguishes them from the paren shapes (an optional id may precede the opener).
        if (s.EndsWith("((", StringComparison.Ordinal))
        {
            int o = s.IndexOf("))", StringComparison.Ordinal);
            if (o >= 0 && o + 2 < s.Length - 2) return (s[(o + 2)..^2].Trim(), MindmapShape.Bang);
        }
        if (s.EndsWith("(", StringComparison.Ordinal))
        {
            int o = s.IndexOf(')');
            if (o >= 0 && o + 1 < s.Length - 1) return (s[(o + 1)..^1].Trim(), MindmapShape.Cloud);
        }

        if (Wrapped(s, "((", "))") is { } circ) return (circ, MindmapShape.Circle);
        if (Wrapped(s, "{{", "}}") is { } hex)  return (hex,  MindmapShape.Hexagon);
        if (Wrapped(s, "[",  "]")  is { } sq)   return (sq,   MindmapShape.Square);
        if (Wrapped(s, "(",  ")")  is { } rnd)  return (rnd,  MindmapShape.Rounded);

        return (s, MindmapShape.Default);
    }

    /// <summary>If <paramref name="s"/> has an (optional id +) <paramref name="open"/>…<paramref name="close"/>
    /// wrapper, returns the inner text; otherwise null.</summary>
    private static string? Wrapped(string s, string open, string close)
    {
        int o = s.IndexOf(open, StringComparison.Ordinal);
        if (o < 0 || !s.EndsWith(close, StringComparison.Ordinal)) return null;
        int inner = o + open.Length;
        int end   = s.Length - close.Length;
        return end > inner ? s[inner..end].Trim() : null;
    }

    // ── Depth + branch colouring ─────────────────────────────────────────────

    private static void AssignDepthAndBranch(MindmapNode node, int depth, int branch)
    {
        node.Depth = depth;
        node.BranchIndex = branch;
        for (int i = 0; i < node.Children.Count; i++)
            AssignDepthAndBranch(node.Children[i], depth + 1, depth == 0 ? i : branch);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
