using System.Text.RegularExpressions;

namespace Nexaflow.Features.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>graph</c> / <c>flowchart</c> diagrams into a <see cref="Graph"/>.
///
/// Supported:
///   • Direction keywords: TD TB LR BT RL
///   • Node shapes: [rect] (rounded) ((circle)) {diamond} {{hex}} ([stadium]) [[sub]] [(cyl)] &gt;asym]
///   • Edge types: --&gt; --- -.-&gt; ==&gt; --o --x
///   • Edge labels: --&gt;|label|  and  -- label --&gt;
///   • &lt;br&gt; / &lt;br/&gt; in labels → rendered as newlines
///   • Multiple targets: A --&gt; B &amp; C
///   • Subgraph blocks (nodes and edges parsed; the subgraph box is not rendered)
///   • classDef / class styling (fill and stroke colours)
///   • %% line comments, trailing semicolons
/// </summary>
public sealed class MermaidParser : IGraphParser
{
    public bool CanParse(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public Graph Parse(string source)
    {
        var graph = new Graph();
        try { ParseInto(source, graph); }
        catch { /* never throw; return partial graph */ }
        return graph;
    }

    // ── Regexes (node shape extraction only) ──────────────────────────────

    // Extracts id + optional shape from a node expression string.
    // Groups: id, od (open delim), lbl (label), cd (close delim)
    private static readonly Regex RxNode =
        new(@"(?<id>[A-Za-z0-9_\-\.]+)(?:(?<od>\[{1,2}|>{1}|\({1,2}|\{{1,2})(?<lbl>.*?)(?<cd>\]{1,2}|\){1,2}|\}{1,2}|\]))?",
            RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RxBr =
        new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxHeader =
        new(@"^\s*(?:graph|flowchart)\s+([A-Z]{1,2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Parser ─────────────────────────────────────────────────────────────

    private static void ParseInto(string source, Graph graph)
    {
        // Class definitions (name → fill colour) and pending applications
        var classDefs = new Dictionary<string, (string? fill, string? stroke)>(StringComparer.OrdinalIgnoreCase);
        var classApps = new List<(string[] nodeIds, string cls)>();

        var subgraphStack = new Stack<Subgraph>();
        bool headerSeen   = false;

        foreach (var rawLine in source.Split('\n'))
        {
            // Strip comment, trailing semicolon and whitespace
            var line = StripComment(rawLine).TrimEnd(';').Trim();
            if (line.Length == 0) continue;

            // Header: "graph LR" / "flowchart TD"
            if (!headerSeen)
            {
                var hm = RxHeader.Match(line);
                if (hm.Success)
                {
                    graph.Direction = ParseDirection(hm.Groups[1].Value);
                    headerSeen = true;
                    continue;
                }
            }

            // Subgraph / end
            if (line.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase))
            {
                subgraphStack.Push(ParseSubgraphHeader(line));
                continue;
            }
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                if (subgraphStack.Count > 0)
                    graph.Subgraphs.Add(subgraphStack.Pop());
                continue;
            }

            // classDef: "classDef name fill:#xxx,stroke:#yyy,..."
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase))
            {
                ParseClassDef(line, classDefs);
                continue;
            }

            // class application: "class id1,id2 className"
            if (line.StartsWith("class ", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line["class ".Length..].Trim().Split(' ', 2);
                if (parts.Length == 2)
                    classApps.Add((parts[0].Split(','), parts[1].Trim()));
                continue;
            }

            // Other directives — skip
            if (line.StartsWith("style ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("click ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("linkStyle ", StringComparison.OrdinalIgnoreCase))
                continue;

            // Try to parse as edge line, fall back to bare node declaration
            if (!TryParseEdgeLine(line, graph))
                TryParseNodeDeclaration(line, graph);

            // Record which nodes were referenced on this line into the current subgraph
            if (subgraphStack.Count > 0)
                TrackSubgraphNodes(line, graph, subgraphStack.Peek());
        }

        // Apply class colours to nodes
        foreach (var (nodeIds, cls) in classApps)
        {
            if (!classDefs.TryGetValue(cls, out var def)) continue;
            foreach (var rawId in nodeIds)
            {
                var node = graph.FindNode(rawId.Trim());
                if (node is null) continue;
                if (def.fill   is string f) node.FillColor   = f;
                if (def.stroke is string s) node.StrokeColor = s;
            }
        }
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    private static void ParseClassDef(string line, Dictionary<string, (string?, string?)> classDefs)
    {
        // "classDef green fill:#9f6,stroke:#333,stroke-width:2px"
        var rest = line["classDef ".Length..].Trim();
        int sp   = rest.IndexOf(' ');
        if (sp < 0) return;

        string cls   = rest[..sp];
        string props = rest[(sp + 1)..];
        string? fill = null, stroke = null;

        foreach (var prop in props.Split(','))
        {
            var kv = prop.Split(':', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var val = kv[1].Trim();
            if (key == "fill"   && val != "none") fill   = val;
            if (key == "stroke" && val != "none") stroke = val;
        }
        classDefs[cls] = (fill, stroke);
    }

    // ── Subgraph helpers ──────────────────────────────────────────────────

    private static Subgraph ParseSubgraphHeader(string line)
    {
        // Forms: "subgraph id [title]"  |  "subgraph title"  |  "subgraph"
        var rest = line["subgraph".Length..].Trim();
        if (rest.Length == 0)
            return new Subgraph { Id = "_sg" };

        int bracketOpen = rest.IndexOf('[');
        if (bracketOpen > 0)
        {
            string id    = rest[..bracketOpen].Trim();
            int    close = rest.IndexOf(']', bracketOpen + 1);
            string title = close > bracketOpen ? rest[(bracketOpen + 1)..close] : rest[(bracketOpen + 1)..];
            return new Subgraph { Id = id.Length > 0 ? id : title, Label = title };
        }
        return new Subgraph { Id = rest, Label = rest };
    }

    /// <summary>
    /// Finds every node ID from <paramref name="graph"/> that appears as a whole word on
    /// <paramref name="line"/> and adds it to <paramref name="sg"/>.NodeIds.
    /// </summary>
    private static void TrackSubgraphNodes(string line, Graph graph, Subgraph sg)
    {
        foreach (var n in graph.Nodes)
        {
            if (sg.NodeIds.Contains(n.Id)) continue;
            int idx = 0;
            while ((idx = line.IndexOf(n.Id, idx, StringComparison.Ordinal)) >= 0)
            {
                bool preOk  = idx == 0 ||
                    !(char.IsLetterOrDigit(line[idx - 1]) || line[idx - 1] is '_' or '-' or '.');
                bool postOk = idx + n.Id.Length >= line.Length ||
                    !(char.IsLetterOrDigit(line[idx + n.Id.Length]) || line[idx + n.Id.Length] is '_' or '-' or '.');
                if (preOk && postOk) { sg.NodeIds.Add(n.Id); break; }
                idx++;
            }
        }
    }

    // ── Edge parsing (tokeniser-based) ────────────────────────────────────

    /// <summary>
    /// Tokenises one line as a Mermaid edge definition.
    /// Handles:
    ///   • src --&gt; dst           (direct arrow)
    ///   • src --&gt;|label| dst    (inline label)
    ///   • src -- label --&gt; dst  (space-label form)
    ///   • src expression may contain any shape including &gt;..] (asymmetric)
    /// </summary>
    private static bool TryParseEdgeLine(string line, Graph graph)
    {
        // Step 1: source node expression (bracket-balanced)
        (int srcEnd, string srcExpr) = ReadNodeExpr(line, 0);
        if (srcEnd < 0 || srcExpr.Length == 0) return false;

        int pos = srcEnd;
        while (pos < line.Length && line[pos] == ' ') pos++;
        if (pos >= line.Length) return false;

        // Step 2: edge marker
        string? label    = null;
        string  arrowStr = "";

        // Space-label form: "-- label --> dst" (exactly two dashes then a space)
        if (pos + 2 < line.Length && line[pos] == '-' && line[pos + 1] == '-' && line[pos + 2] == ' ')
        {
            int labelStart = pos + 3;
            bool found = false;
            for (int i = labelStart; i < line.Length && !found; i++)
            {
                if (line[i] != ' ') continue;
                foreach (var t in ArrowTerminals)
                {
                    if (i + 1 + t.Length <= line.Length && line.AsSpan(i + 1).StartsWith(t))
                    {
                        label    = ProcessLabel(line[labelStart..i]);
                        arrowStr = t;
                        pos      = i + 1 + t.Length;
                        found    = true;
                        break;
                    }
                }
            }
            if (!found) return false;
        }
        else
        {
            // Direct arrow
            string? matched = null;
            foreach (var a in ArrowTerminals)
            {
                if (pos + a.Length <= line.Length && line.AsSpan(pos).StartsWith(a))
                { matched = a; break; }
            }
            if (matched is null) return false;
            arrowStr = matched;
            pos += arrowStr.Length;

            // Inline label: -->|label|
            if (pos < line.Length && line[pos] == '|')
            {
                int closeBar = line.IndexOf('|', pos + 1);
                if (closeBar > pos) { label = ProcessLabel(line[(pos + 1)..closeBar]); pos = closeBar + 1; }
            }
        }

        // Step 3: target node(s)
        while (pos < line.Length && line[pos] == ' ') pos++;
        string dstStr = line[pos..];

        var (style, arrow) = ParseArrowStyle(arrowStr);
        string? srcId = ParseNodeExpr(srcExpr, graph);
        if (srcId is null) return false;

        foreach (var tgtRaw in dstStr.Split('&'))
        {
            (_, string tgtExpr) = ReadNodeExpr(tgtRaw.Trim(), 0);
            if (tgtExpr.Length == 0) continue;
            string? dstId = ParseNodeExpr(tgtExpr, graph);
            if (dstId is null) continue;
            graph.AddEdge(srcId, dstId, label ?? "", style, arrow);
        }
        return true;
    }

    // Arrow tokens ordered longest-first to avoid prefix ambiguity
    private static readonly string[] ArrowTerminals =
        ["==>", "-.->", "-.-", "-->", "---", "--o", "--x"];

    /// <summary>
    /// Reads one Mermaid node expression from <paramref name="s"/> at <paramref name="start"/>.
    /// A node expression is a node-id followed by an optional shape bracket.
    /// Bracket content may contain <c>&lt;br&gt;</c> and other arbitrary text; brackets are depth-matched.
    /// Returns (endPosition, expressionString).  endPosition is -1 on failure.
    /// </summary>
    private static (int end, string expr) ReadNodeExpr(string s, int start)
    {
        int i = start;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int exprStart = i;

        // Node id: letters, digits, underscore, hyphen, dot
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '-' or '.'))
            i++;

        if (i == exprStart) return (-1, "");

        // Optional shape bracket immediately following the id
        if (i < s.Length && s[i] is '[' or '(' or '{' or '>')
        {
            char openCh  = s[i];
            char closeCh = openCh is '[' or '>' ? ']' : openCh is '(' ? ')' : '}';
            i++;
            // Handle double delimiters: (( [[ {{  — but not >>
            bool doubled = openCh != '>' && i < s.Length && s[i] == openCh;
            if (doubled) i++;

            while (i < s.Length)
            {
                if (doubled && i + 1 < s.Length && s[i] == closeCh && s[i + 1] == closeCh)
                { i += 2; break; }
                if (!doubled && s[i] == closeCh)
                { i++; break; }
                i++;
            }
        }

        return (i, s[exprStart..i]);
    }

    // ── Node expression interpretation ────────────────────────────────────

    /// <summary>
    /// Interprets a node expression string (as returned by <see cref="ReadNodeExpr"/>),
    /// updates the graph, and returns the node id.
    /// </summary>
    private static string? ParseNodeExpr(string expr, Graph graph)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return null;

        var m = RxNode.Match(expr);
        if (!m.Success) return null;

        string id      = m.Groups["id"].Value.Trim();
        string od      = m.Groups["od"].Value;
        string rawLbl  = m.Groups["lbl"].Value;
        string cd      = m.Groups["cd"].Value;

        if (id.Length == 0) return null;

        // Detect composite shapes whose opener is two different chars.
        // RxNode captures the outer bracket only, so the inner bracket appears in rawLbl.
        var (shape, innerLbl) = ClassifyComposite(od, cd, rawLbl);
        string lbl = ProcessLabel(innerLbl.Length > 0 ? innerLbl : rawLbl);

        var n = graph.GetOrAdd(id, lbl.Length > 0 ? lbl : null);
        if (od.Length > 0)
            n.Shape = shape;

        return id;
    }

    /// <summary>
    /// Resolves the node shape and the real label text.
    /// Handles composite openers like <c>([</c>, <c>[(</c>, <c>[/</c>, <c>[\</c>, <c>(((</c>.
    /// </summary>
    private static (NodeShape shape, string label) ClassifyComposite(string od, string cd, string rawLbl)
    {
        // (( → Circle already; detect triple (((
        if (od == "((" && rawLbl.StartsWith('(') && rawLbl.EndsWith(')'))
            return (NodeShape.DoubleCircle, rawLbl[1..^1]);

        // ([label]) → Stadium: RxNode sees od='(', lbl='[…]'
        if (od == "(" && rawLbl.StartsWith('[') && rawLbl.EndsWith(']'))
            return (NodeShape.Stadium, rawLbl[1..^1]);

        // [(label)] → Cylinder: od='[', lbl='(…)'
        if (od == "[" && rawLbl.StartsWith('(') && rawLbl.EndsWith(')'))
            return (NodeShape.Cylinder, rawLbl[1..^1]);

        // [/label/] → Parallelogram; [/label\] → Trapezoid
        if (od == "[" && rawLbl.StartsWith('/'))
        {
            if (rawLbl.EndsWith('/'))
                return (NodeShape.Parallelogram, rawLbl[1..^1]);
            if (rawLbl.EndsWith('\\'))
                return (NodeShape.Trapezoid, rawLbl[1..^1]);
        }

        // [\label\] → ParallelogramAlt; [\label/] → TrapezoidAlt
        if (od == "[" && rawLbl.StartsWith('\\'))
        {
            if (rawLbl.EndsWith('\\'))
                return (NodeShape.ParallelogramAlt, rawLbl[1..^1]);
            if (rawLbl.EndsWith('/'))
                return (NodeShape.TrapezoidAlt, rawLbl[1..^1]);
        }

        return (ClassifyShape(od, cd), "");
    }

    private static bool TryParseNodeDeclaration(string line, Graph graph)
    {
        (_, string expr) = ReadNodeExpr(line, 0);
        if (expr.Length == 0) return false;
        ParseNodeExpr(expr, graph);
        return true;
    }

    /// <summary>Replaces &lt;br&gt; / &lt;br/&gt; with newline characters.</summary>
    private static string ProcessLabel(string raw) =>
        RxBr.Replace(raw.Trim(), "\n");

    // ── Shape classification ───────────────────────────────────────────────

    private static NodeShape ClassifyShape(string open, string close) =>
        (open, close) switch
        {
            ("((", "))") => NodeShape.Circle,
            ("(", ")")   => NodeShape.RoundedRect,
            ("([", "])")  => NodeShape.Stadium,
            ("[[", "]]") => NodeShape.Subroutine,
            ("[(", ")]") => NodeShape.Cylinder,
            ("{{", "}}") => NodeShape.Hexagon,
            ("{", "}")   => NodeShape.Diamond,
            (">", "]")   => NodeShape.Asymmetric,
            _            => NodeShape.Rectangle,
        };

    // ── Arrow style ────────────────────────────────────────────────────────

    private static (EdgeStyle style, EdgeArrow arrow) ParseArrowStyle(string raw)
    {
        EdgeStyle style = raw.Contains("==")              ? EdgeStyle.Thick
                        : raw.Contains(".-") || raw.Contains("-.") ? EdgeStyle.Dashed
                        : EdgeStyle.Solid;

        EdgeArrow arrow = raw.EndsWith("o") ? EdgeArrow.Circle
                        : raw.EndsWith("x") ? EdgeArrow.Cross
                        : raw.EndsWith(">") ? EdgeArrow.Normal
                        : EdgeArrow.None;

        return (style, arrow);
    }

    // ── Direction ──────────────────────────────────────────────────────────

    private static GraphDirection ParseDirection(string d) =>
        d.ToUpperInvariant() switch
        {
            "LR"           => GraphDirection.LeftRight,
            "RL"           => GraphDirection.RightLeft,
            "BT"           => GraphDirection.BottomUp,
            "TD" or "TB"   => GraphDirection.TopDown,
            _              => GraphDirection.TopDown,
        };
}
