using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>classDiagram</c> into a <see cref="Graph"/>, reusing the shared graph model
/// so the existing Sugiyama layout + <c>WpfGraphRenderer</c> draw it. Each class is a
/// <see cref="NodeShape.ClassBox"/> node carrying a <see cref="ClassInfo"/>; each relationship is an
/// <see cref="Edge"/> whose <see cref="Edge.StartArrow"/>/<see cref="Edge.Arrow"/> hold the UML head
/// at each end and whose <see cref="Edge.StartLabel"/>/<see cref="Edge.EndLabel"/> hold multiplicity.
///
/// Supported:
///   • Classes: <c>class A</c>, block <c>class A { … }</c>, label override <c>class A["Pretty"]</c>,
///     generics <c>class A~T~</c>, implicit declaration from a member or a relationship
///   • Members: block lines or the <c>A : +member</c> shorthand; visibility <c>+ - # ~</c>, attribute vs
///     method by the presence of <c>()</c>, classifiers <c>*</c> (abstract → italic) / <c>$</c> (static → underline),
///     generic types <c>List~int~</c> → <c>List&lt;int&gt;</c>
///   • Annotations: <c>&lt;&lt;interface&gt;&gt;</c> (inside a block or <c>&lt;&lt;interface&gt;&gt; A</c>) → «stereotype»
///   • Relationships: <c>&lt;|--</c> inheritance, <c>*--</c> composition, <c>o--</c> aggregation,
///     <c>--&gt;</c> association, <c>--</c>/<c>..</c> links, <c>..&gt;</c> dependency, <c>..|&gt;</c> realization,
///     <c>()--</c> lollipop, in either direction, with multiplicity (<c>A "1" --&gt; "*" B</c>) and a <c>: label</c>
///   • Namespaces <c>namespace N { … }</c> → a subgraph box
///   • Notes <c>note "text"</c> and <c>note for A "text"</c>
///   • <c>direction</c>, <c>%%</c> comments, <c>accTitle</c>/<c>accDescr</c>
///   • Styling: <c>classDef</c>, <c>cssClass "a,b" name</c>, <c>style A fill:…</c>, inline <c>A:::name</c>
/// </summary>
public sealed class MermaidClassParser : IGraphParser
{
    public bool CanParse(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public Graph Parse(string source)
    {
        var graph = new Graph();
        try { ParseInto(source, graph); }
        catch { /* never throw; return the partial graph */ }
        return graph;
    }

    // ── Regexes ───────────────────────────────────────────────────────────────

    // Relationship operators, longest alternatives first so the engine prefers them at a given index
    // (two-way forms must precede their one-way prefixes).
    private static readonly Regex RxRelOp = new(
        @"<\|--\|>|<\|\.\.\|>|<-->|<\.\.>|\*--\*|o--o" +                       // two-way
        @"|<\|--|--\|>|<\|\.\.|\.\.\|>|\(\)--|--\(\)|\*--|--\*|o--|--o" +      // one-way (4 / 3 char heads)
        @"|<\.\.|\.\.>|<--|-->|--|\.\.",                                       // remaining one-way + plain links
        RegexOptions.Compiled);

    private static readonly Regex RxAnnotation = new(
        @"^<<\s*(?<kind>[^>]+?)\s*>>(?:\s+(?<id>\S+))?$", RegexOptions.Compiled);

    private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Parser ─────────────────────────────────────────────────────────────────

    /// <summary>An open <c>{ … }</c> scope — either a class body (members) or a namespace (classes).</summary>
    private sealed class Scope
    {
        public required bool   IsClass { get; init; }
        public required string Id      { get; init; }
        public Subgraph?       Box     { get; init; }   // set only for namespaces
    }

    private static void ParseInto(string source, Graph graph)
    {
        var classDefs = new Dictionary<string, (string? fill, string? stroke, string? color)>(StringComparer.OrdinalIgnoreCase);
        var classApps = new List<(string id, string cls)>();
        var nsById    = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        var scopes    = new Stack<Scope>();
        int gen       = 0;

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string firstTok = line.Split(' ', '\t')[0];

            // Header — "classDiagram" / "classDiagram-v2"
            if (firstTok.StartsWith("classdiagram", StringComparison.OrdinalIgnoreCase)) continue;

            // Close the innermost scope. (Namespace boxes are registered when opened, not here, so a
            // dotted ancestor shared by several blocks is added exactly once.)
            if (line == "}")
            {
                if (scopes.Count > 0) scopes.Pop();
                continue;
            }

            // Inside a class body: every line is a member or an annotation.
            var openClass = scopes.Count > 0 && scopes.Peek().IsClass ? scopes.Peek().Id : null;
            if (openClass is not null)
            {
                var am = RxAnnotation.Match(line);
                if (am.Success && !am.Groups["id"].Success)
                    graph.FindNode(openClass)!.Class!.Stereotype = am.Groups["kind"].Value.Trim();
                else
                    AddMember(graph.FindNode(openClass)!.Class!, line);
                continue;
            }

            // direction (top level only)
            if (line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
            {
                if (scopes.Count == 0) graph.Direction = ParseDirection(line["direction ".Length..].Trim());
                continue;
            }

            // accessibility metadata — skip
            if (line.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)) continue;

            // namespace N {   (a dotted name N.M.O nests as hierarchical namespaces)
            if (firstTok.Equals("namespace", StringComparison.OrdinalIgnoreCase))
            {
                var full = line["namespace".Length..].Trim().TrimEnd('{').Trim();
                var leaf = OpenNamespace(full, graph, nsById);
                scopes.Push(new Scope { IsClass = false, Id = full, Box = leaf });
                continue;
            }

            // note / note for X
            if (firstTok.Equals("note", StringComparison.OrdinalIgnoreCase))
            {
                HandleNote(line["note".Length..].Trim(), graph, ref gen);
                continue;
            }

            // Annotation on its own line at top level:  <<interface>> Shape
            var topAnn = RxAnnotation.Match(line);
            if (topAnn.Success && topAnn.Groups["id"].Success)
            {
                ClassNode(graph, topAnn.Groups["id"].Value.Trim()).Class!.Stereotype = topAnn.Groups["kind"].Value.Trim();
                continue;
            }

            // Styling
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase)) { ParseClassDef(line, classDefs); continue; }
            if (line.StartsWith("cssClass ", StringComparison.OrdinalIgnoreCase)) { ParseCssClass(line, classApps); continue; }
            if (line.StartsWith("style ",    StringComparison.OrdinalIgnoreCase)) { ParseStyle(line, graph); continue; }
            if (line.StartsWith("callback ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("link ",     StringComparison.OrdinalIgnoreCase)) continue;

            // class declaration
            if (firstTok.Equals("class", StringComparison.OrdinalIgnoreCase) &&
                (line.Length == 5 || line[5] is ' ' or '\t'))
            {
                HandleClassDecl(line["class".Length..].Trim(), graph, scopes, classApps);
                continue;
            }

            // relationship
            if (TryParseRelationship(line, graph, classApps)) continue;

            // "Id : member" shorthand
            int colon = FindLabelColon(line);
            if (colon > 0)
            {
                var id = StripInlineClass(line[..colon].Trim(), classApps);
                AddMember(ClassNode(graph, id).Class!, line[(colon + 1)..].Trim());
                continue;
            }

            // bare id → an empty class (optionally with :::class)
            var bare = StripInlineClass(firstTok, classApps);
            if (IsIdent(bare)) ClassNode(graph, bare);
        }

        ApplyClasses(graph, classDefs, classApps);
    }

    // ── Class declaration ────────────────────────────────────────────────────

    private static void HandleClassDecl(string rest, Graph graph, Stack<Scope> scopes, List<(string, string)> apps)
    {
        bool opensBlock = rest.EndsWith('{');
        if (opensBlock) rest = rest[..^1].Trim();

        // Inline one-liner body:  class Shape{ <<interface>> }
        string? inlineBody = null;
        if (!opensBlock && rest.EndsWith('}'))
        {
            int br = rest.IndexOf('{');
            if (br > 0) { inlineBody = rest[(br + 1)..^1].Trim(); rest = rest[..br].Trim(); }
        }

        rest = StripInlineClass(rest, apps);

        // Label override:  class A["Pretty Name"]
        string? label = null;
        int lb = rest.IndexOf('[');
        if (lb > 0 && rest.EndsWith(']'))
        {
            label = rest[(lb + 1)..^1].Trim().Trim('"');
            rest  = rest[..lb].Trim();
        }

        string display = ClassBoxMetrics.Generics(rest);
        int t = rest.IndexOf('~');
        string id = t >= 0 ? rest[..t] : rest;
        if (!IsIdent(id)) return;

        var node = ClassNode(graph, id);
        node.Label = label ?? display;

        AddToNamespace(scopes, id);

        if (inlineBody is { Length: > 0 })
        {
            var am = RxAnnotation.Match(inlineBody);
            if (am.Success && !am.Groups["id"].Success) node.Class!.Stereotype = am.Groups["kind"].Value.Trim();
            else AddMember(node.Class!, inlineBody);
        }

        if (opensBlock) scopes.Push(new Scope { IsClass = true, Id = id });
    }

    private static Node ClassNode(Graph graph, string id)
    {
        var n = graph.GetOrAdd(id);
        n.Shape   = NodeShape.ClassBox;
        n.Class ??= new ClassInfo();
        return n;
    }

    /// <summary>Materialises a (possibly dotted) namespace path as a chain of nested subgraph boxes —
    /// hierarchical namespaces — reusing any ancestor already created. Returns the innermost (leaf) box.</summary>
    private static Subgraph OpenNamespace(string full, Graph graph, Dictionary<string, Subgraph> nsById)
    {
        var segs = full.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string path = "";
        Subgraph? leaf = null;
        for (int i = 0; i < segs.Length; i++)
        {
            string parent = path;
            path = i == 0 ? segs[0] : $"{path}.{segs[i]}";
            if (!nsById.TryGetValue(path, out leaf))
            {
                leaf = new Subgraph { Id = path, Label = segs[i], ParentId = parent.Length > 0 ? parent : null };
                nsById[path] = leaf;
                graph.Subgraphs.Add(leaf);
            }
        }
        return leaf ?? new Subgraph { Id = full, Label = full };
    }

    private static void AddToNamespace(Stack<Scope> scopes, string id)
    {
        foreach (var s in scopes)               // top → bottom; the nearest namespace owns it
            if (!s.IsClass && s.Box is not null)
            {
                if (!s.Box.NodeIds.Contains(id)) s.Box.NodeIds.Add(id);
                return;
            }
    }

    // ── Members ───────────────────────────────────────────────────────────────

    private static void AddMember(ClassInfo info, string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return;

        bool isStatic = false, isAbstract = false;
        if (raw.EndsWith('*'))      { isAbstract = true; raw = raw[..^1].TrimEnd(); }
        else if (raw.EndsWith('$')) { isStatic   = true; raw = raw[..^1].TrimEnd(); }
        if (raw.Length == 0) return;

        // Peel a leading visibility marker so a '~' there isn't mistaken for a generic delimiter.
        string vis = "";
        if (raw[0] is '+' or '-' or '#' or '~') { vis = raw[..1]; raw = raw[1..]; }

        bool   isMethod = raw.Contains('(');
        string text     = ClassBoxMetrics.Generics(raw);

        // A method's return type (anything after its closing paren) is shown after a colon:
        // "getId() int" → "getId() : int".
        if (isMethod)
        {
            int rp = text.LastIndexOf(')');
            if (rp >= 0 && rp < text.Length - 1)
            {
                string ret = text[(rp + 1)..].Trim();
                if (ret.Length > 0) text = $"{text[..(rp + 1)]} : {ret}";
            }
        }

        var member = new ClassMember { Text = vis + text, IsStatic = isStatic, IsAbstract = isAbstract };
        (isMethod ? info.Methods : info.Attributes).Add(member);
    }

    // ── Relationships ───────────────────────────────────────────────────────────

    private static bool TryParseRelationship(string line, Graph graph, List<(string, string)> apps)
    {
        var m = RxRelOp.Match(MaskQuotes(line));
        if (!m.Success) return false;

        string op    = line.Substring(m.Index, m.Length);
        var (leftId,  lmult) = ParseLeftEndpoint (line[..m.Index], apps);
        var (rightId, rmult, label) = ParseRightEndpoint(line[(m.Index + m.Length)..], apps);
        if (!IsIdent(leftId) || !IsIdent(rightId)) return false;

        // Lollipop notation ( ()-- / --() ): the '()' end is an interface drawn as a small circle on a
        // short stub off the class box — a decoration on the class, not a node or routed edge.
        bool startLollipop = op.StartsWith("()", StringComparison.Ordinal);
        bool endLollipop   = op.EndsWith("()",   StringComparison.Ordinal);
        if (startLollipop ^ endLollipop)
        {
            // The non-'()' side is the class; the '()' side names the interface. A trailing '()' (the
            // target side) hangs the lollipop below; a leading '()' (the source side) sits above.
            string clsId = endLollipop ? leftId  : rightId;
            string iface = endLollipop ? rightId : leftId;
            ClassNode(graph, clsId).Class!.Lollipops.Add(new Lollipop(iface, Below: endLollipop));
            return true;
        }

        var (startHead, endHead, style) = MapOperator(op);
        ClassNode(graph, leftId);
        ClassNode(graph, rightId);
        var e = graph.AddEdge(leftId, rightId, label, style, endHead);
        e.StartArrow = startHead;
        e.StartLabel = lmult;
        e.EndLabel   = rmult;
        return true;
    }

    /// <summary>Left endpoint reads <c>Id ["mult"]</c> (multiplicity nearest the operator → trailing).</summary>
    private static (string id, string mult) ParseLeftEndpoint(string s, List<(string, string)> apps)
    {
        s = s.Trim();
        string mult = "";
        int q = s.IndexOf('"');
        if (q >= 0)
        {
            int q2 = s.IndexOf('"', q + 1);
            if (q2 > q) { mult = s[(q + 1)..q2]; s = s[..q].Trim(); }
        }
        return (BareId(StripInlineClass(s, apps)), mult);
    }

    /// <summary>Right endpoint reads <c>["mult"] Id [: label]</c>.</summary>
    private static (string id, string mult, string label) ParseRightEndpoint(string s, List<(string, string)> apps)
    {
        s = s.Trim();
        string label = "";
        int lc = FindLabelColon(s);
        if (lc >= 0) { label = s[(lc + 1)..].Trim(); s = s[..lc].Trim(); }

        string mult = "";
        if (s.StartsWith('"'))
        {
            int q2 = s.IndexOf('"', 1);
            if (q2 > 0) { mult = s[1..q2]; s = s[(q2 + 1)..].Trim(); }
        }
        return (BareId(StripInlineClass(s, apps)), mult, label);
    }

    /// <summary>Maps a relationship operator to (head at the left/source end, head at the right/target end, line style).</summary>
    private static (EdgeArrow start, EdgeArrow end, EdgeStyle style) MapOperator(string op)
    {
        EdgeStyle style = op.Contains('.') ? EdgeStyle.Dashed : EdgeStyle.Solid;

        EdgeArrow start = op.StartsWith("<|") ? EdgeArrow.TriangleHollow
                        : op.StartsWith("*")  ? EdgeArrow.DiamondFilled
                        : op.StartsWith("o")  ? EdgeArrow.DiamondHollow
                        : op.StartsWith("()") ? EdgeArrow.Circle
                        : op.StartsWith("<")  ? EdgeArrow.Open
                        : EdgeArrow.None;

        EdgeArrow end = op.EndsWith("|>") ? EdgeArrow.TriangleHollow
                      : op.EndsWith("*")  ? EdgeArrow.DiamondFilled
                      : op.EndsWith("o")  ? EdgeArrow.DiamondHollow
                      : op.EndsWith("()") ? EdgeArrow.Circle
                      : op.EndsWith(">")  ? EdgeArrow.Open
                      : EdgeArrow.None;

        return (start, end, style);
    }

    // ── Notes ───────────────────────────────────────────────────────────────────

    private static void HandleNote(string rest, Graph graph, ref int gen)
    {
        string? forId = null;
        if (rest.StartsWith("for ", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[4..].Trim();
            int q = rest.IndexOf('"');
            if (q > 0) { forId = rest[..q].Trim(); rest = rest[q..]; }
        }

        string text = RxBr.Replace(rest.Trim().Trim('"').Replace("\\n", "\n"), "\n");
        string noteId = $"__note_{gen++}";
        graph.GetOrAdd(noteId, text).Shape = NodeShape.Note;

        if (forId is { Length: > 0 } && IsIdent(forId))
        {
            ClassNode(graph, forId);
            graph.AddEdge(forId, noteId, "", EdgeStyle.Dotted, EdgeArrow.None);
        }
    }

    // ── Styling ───────────────────────────────────────────────────────────────

    private static void ParseClassDef(string line, Dictionary<string, (string?, string?, string?)> defs)
    {
        var rest = line["classDef ".Length..].Trim().TrimEnd(';');
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;

        string cls = rest[..sp];
        var (fill, stroke, color) = ReadStyleProps(rest[(sp + 1)..]);
        defs[cls] = (fill, stroke, color);
    }

    private static void ParseCssClass(string line, List<(string, string)> apps)
    {
        // cssClass "a,b" className
        var rest = line["cssClass ".Length..].Trim();
        int q1 = rest.IndexOf('"');
        int q2 = q1 >= 0 ? rest.IndexOf('"', q1 + 1) : -1;
        if (q2 <= q1) return;
        string cls = rest[(q2 + 1)..].Trim();
        if (cls.Length == 0) return;
        foreach (var id in rest[(q1 + 1)..q2].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            apps.Add((id.Trim(), cls));
    }

    private static void ParseStyle(string line, Graph graph)
    {
        // style Id fill:…,stroke:…,color:…
        var rest = line["style ".Length..].Trim().TrimEnd(';');
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;
        string id = rest[..sp].Trim();
        if (!IsIdent(id)) return;
        var n = ClassNode(graph, id);
        var (fill, stroke, color) = ReadStyleProps(rest[(sp + 1)..]);
        if (fill   is not null) n.FillColor   = fill;
        if (stroke is not null) n.StrokeColor = stroke;
        if (color  is not null) n.TextColor   = color;
    }

    private static (string? fill, string? stroke, string? color) ReadStyleProps(string props)
    {
        string? fill = null, stroke = null, color = null;
        foreach (var prop in props.Split(','))
        {
            var kv = prop.Split(':', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();
            if (key == "fill"   && val != "none") fill   = val;
            if (key == "stroke" && val != "none") stroke = val;
            if (key == "color")                    color  = val;
        }
        return (fill, stroke, color);
    }

    private static void ApplyClasses(
        Graph graph,
        Dictionary<string, (string? fill, string? stroke, string? color)> defs,
        List<(string id, string cls)> apps)
    {
        // A "default" classDef styles every class node, before any explicit application overrides it.
        if (defs.TryGetValue("default", out var dflt))
            foreach (var n in graph.Nodes.Where(n => n.Shape == NodeShape.ClassBox))
                ApplyDef(n, dflt);

        foreach (var (id, cls) in apps)
        {
            if (!defs.TryGetValue(cls, out var d)) continue;
            if (graph.FindNode(id.Trim()) is { } node) ApplyDef(node, d);
        }

        static void ApplyDef(Node n, (string? fill, string? stroke, string? color) d)
        {
            if (d.fill   is string f) n.FillColor   = f;
            if (d.stroke is string s) n.StrokeColor = s;
            if (d.color  is string c) n.TextColor   = c;
        }
    }

    /// <summary>Splits a trailing <c>:::class</c> off a token, recording the application; returns the bare id.</summary>
    private static string StripInlineClass(string token, List<(string, string)> apps)
    {
        int idx = token.IndexOf(":::", StringComparison.Ordinal);
        if (idx < 0) return token;
        string id = token[..idx].Trim();
        string cls = token[(idx + 3)..].Trim();
        if (id.Length > 0 && cls.Length > 0) apps.Add((id, cls));
        return id;
    }

    // ── Small helpers ───────────────────────────────────────────────────────────

    /// <summary>The bare class id from an endpoint token — drops any generic suffix (<c>List~T~</c> → <c>List</c>).</summary>
    private static string BareId(string s)
    {
        s = s.Trim();
        int t = s.IndexOf('~');
        return t >= 0 ? s[..t] : s;
    }

    /// <summary>Copies <paramref name="s"/> with every double-quoted span blanked, so an operator search
    /// never matches a <c>".."</c> inside a multiplicity like <c>"0..*"</c>.</summary>
    private static string MaskQuotes(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool inQuote = false;
        foreach (char c in s)
        {
            if (c == '"') { inQuote = !inQuote; sb.Append(c); }
            else sb.Append(inQuote ? ' ' : c);
        }
        return sb.ToString();
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    /// <summary>Index of the first label-colon (a single <c>:</c> not part of <c>:::</c>), or -1.</summary>
    private static int FindLabelColon(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != ':') continue;
            if (i + 2 < s.Length && s[i + 1] == ':' && s[i + 2] == ':') { i += 2; continue; }
            if (i + 1 < s.Length && s[i + 1] == ':') { i++; continue; }   // the back half of a ':::'
            return i;
        }
        return -1;
    }

    private static bool IsIdent(string s) =>
        s.Length > 0 && s.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.');

    private static GraphDirection ParseDirection(string d) =>
        d.ToUpperInvariant() switch
        {
            "LR"         => GraphDirection.LeftRight,
            "RL"         => GraphDirection.RightLeft,
            "BT"         => GraphDirection.BottomUp,
            "TD" or "TB" => GraphDirection.TopDown,
            _            => GraphDirection.TopDown,
        };
}
