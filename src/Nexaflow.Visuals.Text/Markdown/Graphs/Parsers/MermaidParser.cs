using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

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
///   • Chains: A --&gt; B --&gt; C (each hop becomes its own edge)
///   • Subgraph blocks, including nested subgraphs (drawn as nested boxes via Subgraph.ParentId)
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
                var sg = ParseSubgraphHeader(line);
                // A subgraph opened inside another nests under it (the clustered layout draws the parent
                // box around its children via Subgraph.ParentId); top-level subgraphs leave it null.
                if (subgraphStack.Count > 0) sg.ParentId = subgraphStack.Peek().Id;
                subgraphStack.Push(sg);
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

            // click: a node link. Real mermaid syntax, so a generated diagram stays portable.
            if (line.StartsWith("click ", StringComparison.OrdinalIgnoreCase))
            {
                if (MermaidDirectives.TryParseClick(line, out var clickId, out var href, out var tip) &&
                    graph.Nodes.FirstOrDefault(n => n.Id == clickId) is { } clicked)
                {
                    clicked.Href    = href;
                    clicked.Tooltip = tip;
                }
                continue;
            }

            // Other directives — skip
            if (line.StartsWith("style ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("linkStyle ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))   // per-subgraph direction — not laid out
                continue;

            // Standalone node metadata:  id@{ shape: …, label: "…" }
            if (RxNodeMeta.IsMatch(line))
            {
                if (TryParseNodeMeta(line, graph)) continue;   // false → edge metadata (e.g. e1@{ curve: … }) → skip
                continue;
            }

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
                // '-' is a boundary (it begins an arrow), so "a1" in "a1-->a2" is a whole word.
                bool preOk  = idx == 0 ||
                    !(char.IsLetterOrDigit(line[idx - 1]) || line[idx - 1] is '_' or '.');
                bool postOk = idx + n.Id.Length >= line.Length ||
                    !(char.IsLetterOrDigit(line[idx + n.Id.Length]) || line[idx + n.Id.Length] is '_' or '.');
                if (preOk && postOk) { sg.NodeIds.Add(n.Id); break; }
                idx++;
            }
        }
    }

    // ── Edge parsing (tokeniser-based) ────────────────────────────────────

    /// <summary>
    /// Tokenises one line as a Mermaid edge definition.
    /// Handles:
    ///   • src --&gt; dst              (direct arrow)
    ///   • src --&gt;|label| dst       (inline label)
    ///   • src -- label --&gt; dst     (space-label form)
    ///   • src --&gt; B &amp; C           (fan-out to several targets)
    ///   • src --&gt; mid --&gt; dst     (chains — each hop becomes its own edge)
    ///   • src expression may contain any shape including &gt;..] (asymmetric)
    /// </summary>
    private static bool TryParseEdgeLine(string line, Graph graph)
    {
        (int pos, string srcExpr) = ReadNodeExpr(line, 0);
        if (pos < 0 || srcExpr.Length == 0) return false;

        List<string>? sources = null;   // realised only once the first arrow confirms this is an edge line
        bool madeEdge = false;

        while (true)
        {
            while (pos < line.Length && line[pos] == ' ') pos++;
            if (pos >= line.Length) break;

            pos = SkipEdgeId(line, pos);                       // optional inline edge id: A e1@--> B
            if (TryReadArrow(line, ref pos) is not { } arrow) break;

            if (sources is null)
            {
                if (ParseNodeExpr(srcExpr, graph) is not { } sid) return false;
                sources = [sid];
            }

            // Target list: one or more node expressions separated by '&'.
            var targets = new List<string>();
            while (true)
            {
                while (pos < line.Length && line[pos] == ' ') pos++;
                (int tend, string texpr) = ReadNodeExpr(line, pos);
                if (tend < 0 || texpr.Length == 0) break;
                if (ParseNodeExpr(texpr, graph) is { } tid) targets.Add(tid);
                pos = tend;
                while (pos < line.Length && line[pos] == ' ') pos++;
                if (pos < line.Length && line[pos] == '&') { pos++; continue; }
                break;
            }
            if (targets.Count == 0) break;

            foreach (var s in sources)
                foreach (var t in targets)
                {
                    graph.AddEdge(s, t, arrow.label ?? "", arrow.style, arrow.end).StartArrow = arrow.start;
                    madeEdge = true;
                }

            sources = targets;   // a chain (A --> B --> C) continues from the targets just read
        }

        return madeEdge;
    }

    /// <summary>Reads a link operator at <paramref name="pos"/> in either the inline-label
    /// (<c>--&gt;|label|</c>) or space-label (<c>-- label --&gt;</c>) form, advancing <paramref name="pos"/>
    /// past it (and any label). Returns null when no operator starts here.</summary>
    private static (string? label, EdgeStyle style, EdgeArrow start, EdgeArrow end)? TryReadArrow(string line, ref int pos)
    {
        // Space-label form: "-- label --> dst" (two dashes then a space).
        if (pos + 2 < line.Length && line[pos] == '-' && line[pos + 1] == '-' && line[pos + 2] == ' ')
        {
            int labelStart = pos + 3;
            for (int i = labelStart; i < line.Length; i++)
            {
                if (line[i] != ' ') continue;
                if (MatchArrow(line, i + 1) is not { } am) continue;
                pos = i + 1 + am.len;
                return (ProcessLabel(line[labelStart..i]), am.style, am.start, am.end);
            }
            return null;
        }

        if (MatchArrow(line, pos) is not { } a) return null;
        pos += a.len;

        string? label = null;
        if (pos < line.Length && line[pos] == '|')   // inline label: -->|label|
        {
            int closeBar = line.IndexOf('|', pos + 1);
            if (closeBar > pos) { label = ProcessLabel(line[(pos + 1)..closeBar]); pos = closeBar + 1; }
        }
        return (label, a.style, a.start, a.end);
    }

    /// <summary>Skips an inline edge id (<c>e1@</c>) that precedes an arrow operator; returns the new position.</summary>
    private static int SkipEdgeId(string line, int pos)
    {
        int q = pos;
        while (q < line.Length && (char.IsLetterOrDigit(line[q]) || line[q] == '_')) q++;
        if (q > pos && q < line.Length && line[q] == '@'
            && q + 1 < line.Length && line[q + 1] is '-' or '=' or '<' or 'o' or 'x')
            return q + 1;
        return pos;
    }

    /// <summary>
    /// Matches a flowchart link operator at <paramref name="pos"/>, tolerating arbitrary link length
    /// (<c>--&gt;</c> … <c>-----&gt;</c>), dotted (<c>-.-&gt;</c>) and thick (<c>==&gt;</c>) lines, plus head
    /// markers at either end: <c>&gt;</c>/<c>&lt;</c> (arrow), <c>o</c> (circle), <c>x</c> (cross).
    /// Returns the token length and resolved style/heads, or null when no operator starts here.
    /// </summary>
    private static (int len, EdgeStyle style, EdgeArrow start, EdgeArrow end)? MatchArrow(string s, int pos)
    {
        int i = pos, len = s.Length;

        EdgeArrow start = EdgeArrow.None;
        if (i < len && s[i] == '<') { start = EdgeArrow.Normal; i++; }
        else if (i + 1 < len && (s[i] == 'o' || s[i] == 'x') && s[i + 1] is '-' or '=' or '.')
        { start = s[i] == 'o' ? EdgeArrow.Circle : EdgeArrow.Cross; i++; }

        int lineStart = i;
        EdgeStyle style;
        if (i < len && s[i] == '=')
        {
            while (i < len && s[i] == '=') i++;
            style = EdgeStyle.Thick;
        }
        else if (i < len && s[i] == '-')
        {
            i++;
            if (i < len && s[i] == '.')                       // dotted:  -.- / -..-
            {
                while (i < len && s[i] == '.') i++;
                if (i >= len || s[i] != '-') return null;
                while (i < len && s[i] == '-') i++;
                style = EdgeStyle.Dotted;
            }
            else
            {
                while (i < len && s[i] == '-') i++;
                style = EdgeStyle.Solid;
            }
        }
        else return null;

        int lineLen = i - lineStart;
        if (style != EdgeStyle.Dotted && lineLen < 2) return null;

        EdgeArrow end = EdgeArrow.None;
        if (i < len)
        {
            if (s[i] == '>') { end = EdgeArrow.Normal; i++; }
            else if (s[i] == 'o') { end = EdgeArrow.Circle; i++; }
            else if (s[i] == 'x') { end = EdgeArrow.Cross; i++; }
        }

        // Reject a bare "--" with no heads (not a complete link); "---" open links are fine.
        if (start == EdgeArrow.None && end == EdgeArrow.None && style == EdgeStyle.Solid && lineLen < 3)
            return null;

        return (i - pos, style, start, end);
    }

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

        // Node id: letters, digits, underscore, dot, and hyphen — but a hyphen counts only when
        // it continues the id (next char is an id char), so "c1-->a2" splits at the arrow.
        while (i < s.Length &&
               (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.'
                || (s[i] == '-' && i + 1 < s.Length && (char.IsLetterOrDigit(s[i + 1]) || s[i + 1] is '_' or '.'))))
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

        // ([label]) → Stadium: RxNode sees od='(', lbl='[…]' (its close ']' may be consumed as cd).
        if (od == "(" && rawLbl.StartsWith('['))
            return (NodeShape.Stadium, rawLbl.TrimStart('[').TrimEnd(']'));

        // [(label)] → Cylinder: od='[', lbl='(…)' (its close ')' may be consumed as cd).
        if (od == "[" && rawLbl.StartsWith('('))
            return (NodeShape.Cylinder, rawLbl.TrimStart('(').TrimEnd(')'));

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

    // ── Node metadata:  id@{ shape: …, label: "…" } ─────────────────────────

    private static readonly Regex RxNodeMeta =
        new(@"^(?<id>[A-Za-z0-9_.\-]+)@\{(?<body>.*)\}\s*$",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Parses an <c>id@{ … }</c> metadata line into a node (shape/label).  Returns false when the body
    /// carries no node keys (e.g. edge metadata like <c>e1@{ curve: linear }</c>), so the caller can skip it.
    /// </summary>
    private static bool TryParseNodeMeta(string line, Graph graph)
    {
        var m = RxNodeMeta.Match(line);
        if (!m.Success) return false;

        string? shape = null, label = null;
        bool isNode = false;
        foreach (var part in SplitTopLevel(m.Groups["body"].Value))
        {
            int colon = part.IndexOf(':');
            if (colon < 0) continue;
            string key = part[..colon].Trim().Trim('"').ToLowerInvariant();
            string val = part[(colon + 1)..].Trim().Trim('"').Trim();
            switch (key)
            {
                case "shape":          shape = val; isNode = true; break;
                case "label":
                case "title":          label = val; isNode = true; break;
                case "icon":
                case "img":
                case "form":           isNode = true; break;
            }
        }
        if (!isNode) return false;   // edge metadata — let the caller skip the line

        var node = graph.GetOrAdd(m.Groups["id"].Value, label is { Length: > 0 } ? ProcessLabel(label) : null);
        if (shape is not null) node.Shape = MapShape(shape);
        return true;
    }

    /// <summary>Splits a comma-separated metadata body, ignoring commas inside double quotes.</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        bool inQuote = false;
        int start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '"') inQuote = !inQuote;
            else if (body[i] == ',' && !inQuote) { parts.Add(body[start..i]); start = i + 1; }
        }
        parts.Add(body[start..]);
        return parts;
    }

    /// <summary>Maps a Mermaid <c>@{ shape }</c> name (and its many aliases) to the nearest <see cref="NodeShape"/>.</summary>
    private static NodeShape MapShape(string name) => name.Trim().ToLowerInvariant() switch
    {
        "rect" or "rectangle" or "proc" or "process" or "rectangular" or "notch-rect" => NodeShape.Rectangle,
        "rounded" or "round-rect" or "event"                                          => NodeShape.RoundedRect,
        "stadium" or "pill" or "terminal"                                             => NodeShape.Stadium,
        "subroutine" or "subprocess" or "framed-rectangle" or "procs" or "processes"  => NodeShape.Subroutine,
        "cyl" or "cylinder" or "database" or "db" or "disk"                           => NodeShape.Cylinder,
        "circle" or "circ"                                                            => NodeShape.Circle,
        "dbl-circ" or "double-circle"                                                 => NodeShape.DoubleCircle,
        "diam" or "diamond" or "decision" or "question"                              => NodeShape.Diamond,
        "hex" or "hexagon" or "prepare"                                               => NodeShape.Hexagon,
        "lean-r" or "lean-right" or "in-out" or "manual-input"                        => NodeShape.Parallelogram,
        "lean-l" or "lean-left" or "out-in"                                           => NodeShape.ParallelogramAlt,
        "trap-b" or "trapezoid-bottom" or "trapezoid" or "priority"                   => NodeShape.Trapezoid,
        "trap-t" or "trapezoid-top" or "manual" or "manual-file"                      => NodeShape.TrapezoidAlt,
        "doc" or "document" or "docs" or "documents" or "paper-tape"
            or "tag-doc" or "tagged-document" or "lined-document"                     => NodeShape.Document,
        "card" or "notch-rect" or "notched-rectangle"                                 => NodeShape.Card,
        "flag" or "tag-rect" or "tagged-process"                                      => NodeShape.Asymmetric,
        _                                                                             => NodeShape.Rectangle,
    };

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
