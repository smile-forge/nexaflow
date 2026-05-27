using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses <see href="https://nomnoml.com/">nomnoml</see> diagrams.
///
/// Supported syntax:
///   • Associations: [A] -> [B]  [A] --> [B]  [A] -:> [B]  [A] -- [B]  [A] -/- [B]
///   • Labels on associations: [A] -> [B]: label
///   • Node compartments (first compartment = label): [Name|field|method()]
///   • Classifiers: [&lt;abstract&gt;Name]  [&lt;note&gt;Text]  [&lt;actor&gt;Name]  etc.
///   • Nested groups: [Group|[A]->[B]] — inner associations parsed recursively
///   • Directives: #direction: down | right  #.style: ...
/// </summary>
public sealed class NomnomlParser : IGraphParser
{
    public bool CanParse(string language) =>
        language.Equals("nomnoml", StringComparison.OrdinalIgnoreCase);

    public Graph Parse(string source)
    {
        var graph = new Graph();
        try { ParseInto(source, graph, prefix: string.Empty); }
        catch { }
        return graph;
    }

    // ── Parser ─────────────────────────────────────────────────────────────

    private static void ParseInto(string source, Graph graph, string prefix)
    {
        // Process directives first
        foreach (var line in source.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith('#') && t.Contains(':'))
                ProcessDirective(t, graph);
        }

        // Tokenise at the top level (respecting bracket nesting)
        var statements = Tokenise(source);
        foreach (var stmt in statements)
            ProcessStatement(stmt.Trim(), graph, prefix);
    }

    // ── Directive handling ─────────────────────────────────────────────────

    private static void ProcessDirective(string line, Graph graph)
    {
        // #direction: down | right | up | left
        var m = Regex.Match(line, @"#direction\s*:\s*(\w+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            graph.Direction = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "right" => GraphDirection.LeftRight,
                "left"  => GraphDirection.RightLeft,
                "up"    => GraphDirection.BottomUp,
                _       => GraphDirection.TopDown,
            };
        }
    }

    // ── Statement tokeniser ────────────────────────────────────────────────

    /// <summary>Split the source into top-level statements separated by newlines or semicolons, respecting nested brackets.</summary>
    private static List<string> Tokenise(string source)
    {
        var stmts = new List<string>();
        var buf   = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char c in source)
        {
            if (c == '[') depth++;
            if (c == ']') depth--;

            if ((c == '\n' || c == ';') && depth == 0)
            {
                var s = buf.ToString().Trim();
                if (s.Length > 0 && !s.StartsWith('#'))
                    stmts.Add(s);
                buf.Clear();
            }
            else
            {
                buf.Append(c);
            }
        }
        var last = buf.ToString().Trim();
        if (last.Length > 0 && !last.StartsWith('#'))
            stmts.Add(last);

        return stmts;
    }

    // ── Statement processing ───────────────────────────────────────────────

    private static readonly Regex RxAssoc =
        new(@"(?<src>\[[^\]]*(?:\[[^\]]*\][^\]]*)*\])\s*(?<arrow>-+(?::?>|/|-)?\>?|-+)\s*(?<dst>\[[^\]]*(?:\[[^\]]*\][^\]]*)*\])(?:\s*:\s*(?<lbl>.+))?",
            RegexOptions.Compiled);

    private static void ProcessStatement(string stmt, Graph graph, string prefix)
    {
        if (stmt.Length == 0) return;

        var m = RxAssoc.Match(stmt);
        if (m.Success)
        {
            string srcRaw = m.Groups["src"].Value;
            string dstRaw = m.Groups["dst"].Value;
            string arrow  = m.Groups["arrow"].Value;
            string label  = m.Groups["lbl"].Value.Trim();

            string srcId = ProcessNode(srcRaw, graph, prefix);
            string dstId = ProcessNode(dstRaw, graph, prefix);

            var (style, arrowType) = ParseAssocArrow(arrow);
            graph.AddEdge(srcId, dstId, label, style, arrowType);
        }
        else if (stmt.StartsWith('[') && stmt.EndsWith(']'))
        {
            // Bare node declaration
            ProcessNode(stmt, graph, prefix);
        }
    }

    // ── Node processing ────────────────────────────────────────────────────

    private static string ProcessNode(string raw, Graph graph, string prefix)
    {
        // Strip outer brackets
        string inner = raw.Length >= 2 && raw[0] == '[' && raw[^1] == ']'
            ? raw[1..^1]
            : raw;

        // Check for classifier: <type>name
        string? classifier = null;
        var clfm = Regex.Match(inner, @"^<([^>]+)>(.*)$");
        if (clfm.Success)
        {
            classifier = clfm.Groups[1].Value;
            inner      = clfm.Groups[2].Value;
        }

        // Split on | for compartments: Name|field1|field2
        // Nested brackets can appear in compartments — find top-level pipes
        var parts = SplitTopLevel(inner, '|');
        string label = parts.Count > 0 ? parts[0].Trim() : inner.Trim();
        string id    = prefix + NormaliseId(label);

        var node = graph.GetOrAdd(id, label);
        if (classifier is not null)
        {
            node.Classifier = classifier;
            node.Shape = classifier switch
            {
                "actor"      => NodeShape.Circle,
                "note"       => NodeShape.Parallelogram,
                "abstract"   => NodeShape.Rectangle,
                "interface"  => NodeShape.Rectangle,
                _            => NodeShape.Rectangle,
            };
        }

        // Recurse on nested sub-diagrams inside compartments
        for (int i = 1; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            if (part.Contains('[') && part.Contains(']'))
                ParseInto(part, graph, id + "_");
        }

        return id;
    }

    // ── Arrow classification ───────────────────────────────────────────────

    private static (EdgeStyle style, EdgeArrow arrow) ParseAssocArrow(string raw) =>
        raw switch
        {
            var a when a.Contains("=") => (EdgeStyle.Thick,  EdgeArrow.Normal),
            var a when a.Contains(".") => (EdgeStyle.Dashed, EdgeArrow.Normal),
            var a when a.EndsWith("/:") => (EdgeStyle.Solid, EdgeArrow.Open),
            var a when a.EndsWith(">") => (EdgeStyle.Solid, EdgeArrow.Normal),
            var a when a.Contains("/") => (EdgeStyle.Dashed, EdgeArrow.None),
            _                          => (EdgeStyle.Solid, EdgeArrow.None),
        };

    // ── Utilities ──────────────────────────────────────────────────────────

    private static string NormaliseId(string label) =>
        Regex.Replace(label, @"[^A-Za-z0-9_]", "_").Trim('_');

    /// <summary>Split <paramref name="s"/> on <paramref name="sep"/> ignoring characters inside brackets.</summary>
    private static List<string> SplitTopLevel(string s, char sep)
    {
        var parts = new List<string>();
        var buf   = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in s)
        {
            if (c == '[') depth++;
            if (c == ']') depth--;
            if (c == sep && depth == 0) { parts.Add(buf.ToString()); buf.Clear(); }
            else buf.Append(c);
        }
        parts.Add(buf.ToString());
        return parts;
    }
}
