using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>stateDiagram</c> / <c>stateDiagram-v2</c> into a <see cref="Graph"/>,
/// reusing the shared graph model so the existing Sugiyama layout + <c>WpfGraphRenderer</c> draw it.
///
/// Supported:
///   • States + descriptions: bare <c>id</c>, <c>state "desc" as id</c>, <c>id : desc</c>
///   • Transitions <c>a --&gt; b</c> and labelled <c>a --&gt; b : text</c>
///   • Start/end pseudostates <c>[*]</c> — one shared start + one shared end per scope
///   • Composite states <c>state X { … }</c> → a nested subgraph box (arbitrarily deep via
///     <see cref="Subgraph.ParentId"/>); concurrency <c>--</c> dividers are not drawn (regions stack)
///   • Choice <c>&lt;&lt;choice&gt;&gt;</c> (diamond) and fork/join <c>&lt;&lt;fork&gt;&gt;</c>/<c>&lt;&lt;join&gt;&gt;</c> (bar)
///   • Notes <c>note left/right of X</c> (single-line <c>: text</c> or multi-line … <c>end note</c>)
///   • <c>direction</c>, <c>%%</c> comments, <c>accTitle</c>/<c>accDescr</c>
///   • Styling: <c>classDef</c>, <c>class a,b name</c>, inline <c>id:::name</c> (fill/stroke/colour)
/// </summary>
public sealed class MermaidStateParser : IGraphParser
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

    // ── Per-parse scope (root or a composite state) ───────────────────────────

    private sealed class Scope
    {
        public string?   Id      { get; init; }   // composite id; null at root
        public Subgraph? Box      { get; set; }   // the composite's drawn box (null only at root)
        public string?   StartId { get; set; }    // shared [*]-as-source node for this scope
        public string?   EndId    { get; set; }   // shared [*]-as-target node for this scope
    }

    // ── Regexes ───────────────────────────────────────────────────────────────

    private static readonly Regex RxStereotype =
        new(@"^(?<id>[^\s<]+)\s*<<\s*(?<kind>choice|fork|join|end)\s*>>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxStateQuoted =
        new("^\"(?<lbl>.*)\"\\s+as\\s+(?<id>\\S+)$", RegexOptions.Compiled);
    private static readonly Regex RxNoteHead =
        new(@"^note\s+(?:left|right)\s+of\s+(?<id>[^:]+?)\s*(?::\s*(?<text>.*))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Parser ─────────────────────────────────────────────────────────────────

    private static void ParseInto(string source, Graph graph)
    {
        var classDefs = new Dictionary<string, (string? fill, string? stroke, string? color)>(StringComparer.OrdinalIgnoreCase);
        var classApps = new List<(string id, string cls)>();

        var scopes = new Stack<Scope>();
        scopes.Push(new Scope());            // root
        int gen = 0;

        string? noteTarget = null;           // active multi-line note collection
        var     noteBuf    = new StringBuilder();

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            // Inside a multi-line note: collect until "end note".
            if (noteTarget is not null)
            {
                var body = StripComment(rawLine).Trim();
                if (body.Equals("end note", StringComparison.OrdinalIgnoreCase))
                {
                    AddNote(graph, noteTarget, noteBuf.ToString(), scopes, ref gen);
                    noteTarget = null; noteBuf.Clear();
                    continue;
                }
                if (body.Length > 0)
                {
                    if (noteBuf.Length > 0) noteBuf.Append('\n');
                    noteBuf.Append(body);
                }
                continue;
            }

            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string firstTok = line.Split(' ', '\t')[0];

            // Header — "stateDiagram" / "stateDiagram-v2"
            if (firstTok.ToLowerInvariant().StartsWith("statediagram", StringComparison.Ordinal)) continue;

            // direction (only the top-level one drives layout; per-composite direction is ignored)
            if (line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
            {
                if (scopes.Count == 1) graph.Direction = ParseDirection(line["direction ".Length..].Trim());
                continue;
            }

            // accessibility metadata — skip
            if (line.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)) continue;

            // Notes
            var nm = RxNoteHead.Match(line);
            if (nm.Success)
            {
                var id = nm.Groups["id"].Value.Trim();
                if (nm.Groups["text"].Success && nm.Groups["text"].Value.Trim().Length > 0)
                    AddNote(graph, id, nm.Groups["text"].Value.Trim(), scopes, ref gen);
                else
                    { noteTarget = id; noteBuf.Clear(); }
                continue;
            }

            // classDef / class
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase))
            {
                ParseClassDef(line, classDefs);
                continue;
            }
            if (line.StartsWith("class ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line["class ".Length..].Trim();
                int sp = rest.LastIndexOf(' ');
                if (sp > 0)
                {
                    var cls = rest[(sp + 1)..].Trim();
                    foreach (var id in rest[..sp].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
                        classApps.Add((id.Trim(), cls));
                }
                continue;
            }

            // Composite close
            if (line == "}")
            {
                if (scopes.Count > 1)
                {
                    var popped = scopes.Pop();
                    if (popped.Box is not null) graph.Subgraphs.Add(popped.Box);
                }
                continue;
            }

            // Concurrency divider — regions are stacked; the divider is not drawn.
            if (line == "--") continue;

            // state … declarations
            if (firstTok.Equals("state", StringComparison.OrdinalIgnoreCase) &&
                (line.Length == 5 || line[5] is ' ' or '\t'))
            {
                HandleStateLine(line, graph, scopes, classApps, ref gen);
                continue;
            }

            // Transition
            if (line.Contains("-->", StringComparison.Ordinal))
            {
                ParseTransition(line, graph, scopes, classApps, ref gen);
                continue;
            }

            // "id : description"
            int colon = FindLabelColon(line);
            if (colon > 0)
            {
                var id   = StripInlineClass(line[..colon].Trim(), classApps);
                var desc = line[(colon + 1)..].Trim();
                graph.GetOrAdd(id).Label = desc;
                AddMember(id, scopes);
                continue;
            }

            // Bare node declaration (optionally with :::class)
            var bare = StripInlineClass(firstTok, classApps);
            if (IsIdent(bare)) { graph.GetOrAdd(bare); AddMember(bare, scopes); }
        }

        ApplyClasses(graph, classDefs, classApps);
    }

    // ── state … line ───────────────────────────────────────────────────────────

    private static void HandleStateLine(
        string line, Graph graph, Stack<Scope> scopes, List<(string, string)> classApps, ref int gen)
    {
        var rest = line["state".Length..].Trim();

        bool composite = rest.EndsWith('{');
        if (composite) rest = rest[..^1].Trim();

        // <<choice>> / <<fork>> / <<join>>
        var stm = RxStereotype.Match(rest);
        if (stm.Success && !composite)
        {
            var id   = stm.Groups["id"].Value;
            var kind = stm.Groups["kind"].Value.ToLowerInvariant();
            var node = graph.GetOrAdd(id);
            node.Shape = kind == "choice" ? NodeShape.Diamond
                       : kind is "fork" or "join" ? NodeShape.ForkJoin
                       : NodeShape.StateEnd;
            node.Label = "";   // choice/fork/join render as label-free shapes (matches Mermaid)
            AddMember(id, scopes);
            return;
        }

        // "desc" as id   |   bare id
        string id2; string? label = null;
        var qm = RxStateQuoted.Match(rest);
        if (qm.Success) { id2 = qm.Groups["id"].Value; label = qm.Groups["lbl"].Value; }
        else            { id2 = rest.Split(' ', '\t')[0]; }

        if (composite)
            OpenComposite(id2, label, graph, scopes);
        else
        {
            var n = graph.GetOrAdd(id2);
            if (label is not null) n.Label = label;
            AddMember(id2, scopes);
        }
    }

    private static void OpenComposite(string id, string? label, Graph graph, Stack<Scope> scopes)
    {
        // Every composite owns a drawn box; the layout nests them via Subgraph.ParentId.
        var box = new Subgraph
        {
            Id       = id,
            Label    = label ?? graph.FindNode(id)?.Label ?? id,
            ParentId = ActiveBox(scopes)?.Id,
        };
        scopes.Push(new Scope { Id = id, Box = box });
    }

    // ── Transition ───────────────────────────────────────────────────────────────

    private static void ParseTransition(
        string line, Graph graph, Stack<Scope> scopes, List<(string, string)> classApps, ref int gen)
    {
        int idx = line.IndexOf("-->", StringComparison.Ordinal);
        var left  = line[..idx].Trim();
        var right = line[(idx + 3)..].Trim();

        var (rightTok, label) = SplitTargetLabel(right);

        string? srcId = ResolveEndpoint(left,     graph, scopes, isSource: true,  classApps, ref gen);
        string? dstId = ResolveEndpoint(rightTok, graph, scopes, isSource: false, classApps, ref gen);
        if (srcId is null || dstId is null) return;

        graph.AddEdge(srcId, dstId, label ?? "");
    }

    /// <summary>Resolves one transition endpoint to a node id, materialising <c>[*]</c> as the
    /// scope's shared start (source) or end (target) pseudostate.</summary>
    private static string? ResolveEndpoint(
        string token, Graph graph, Stack<Scope> scopes, bool isSource, List<(string, string)> classApps, ref int gen)
    {
        token = StripInlineClass(token.Trim(), classApps);
        if (token.Length == 0) return null;

        if (token == "[*]")
            return isSource ? ScopeStart(graph, scopes, ref gen) : ScopeEnd(graph, scopes, ref gen);

        graph.GetOrAdd(token);
        AddMember(token, scopes);
        return token;
    }

    private static string ScopeStart(Graph graph, Stack<Scope> scopes, ref int gen)
    {
        var s = scopes.Peek();
        if (s.StartId is null)
        {
            s.StartId = $"__start_{s.Id ?? "root"}_{gen++}";
            graph.GetOrAdd(s.StartId, "").Shape = NodeShape.StateStart;
            AddMember(s.StartId, scopes);
        }
        return s.StartId;
    }

    private static string ScopeEnd(Graph graph, Stack<Scope> scopes, ref int gen)
    {
        var s = scopes.Peek();
        if (s.EndId is null)
        {
            s.EndId = $"__end_{s.Id ?? "root"}_{gen++}";
            graph.GetOrAdd(s.EndId, "").Shape = NodeShape.StateEnd;
            AddMember(s.EndId, scopes);
        }
        return s.EndId;
    }

    // ── Notes ────────────────────────────────────────────────────────────────────

    private static void AddNote(Graph graph, string targetId, string text, Stack<Scope> scopes, ref int gen)
    {
        targetId = targetId.Trim();
        if (targetId.Length == 0) return;

        string noteId = $"__note_{gen++}";
        graph.GetOrAdd(noteId, text).Shape = NodeShape.Note;
        graph.GetOrAdd(targetId);
        AddMember(targetId, scopes);
        AddMember(noteId, scopes);
        graph.AddEdge(targetId, noteId, "", EdgeStyle.Dotted, EdgeArrow.None);
    }

    // ── Subgraph membership ──────────────────────────────────────────────────────

    /// <summary>The nearest enclosing composite that owns a drawn box, or null at the top level.</summary>
    private static Subgraph? ActiveBox(Stack<Scope> scopes)
    {
        foreach (var s in scopes)            // Stack enumerates top → bottom
            if (s.Box is not null) return s.Box;
        return null;
    }

    private static void AddMember(string id, Stack<Scope> scopes)
    {
        var box = ActiveBox(scopes);
        if (box is null || string.Equals(box.Id, id, StringComparison.Ordinal)) return;
        if (!box.NodeIds.Contains(id)) box.NodeIds.Add(id);
    }

    // ── Styling ──────────────────────────────────────────────────────────────────

    private static void ParseClassDef(string line, Dictionary<string, (string?, string?, string?)> defs)
    {
        var rest = line["classDef ".Length..].Trim().TrimEnd(';');
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;

        string cls = rest[..sp];
        string? fill = null, stroke = null, color = null;
        foreach (var prop in rest[(sp + 1)..].Split(','))
        {
            var kv = prop.Split(':', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();
            if (key == "fill"   && val != "none") fill   = val;
            if (key == "stroke" && val != "none") stroke = val;
            if (key == "color")                    color  = val;
        }
        defs[cls] = (fill, stroke, color);
    }

    private static void ApplyClasses(
        Graph graph,
        Dictionary<string, (string? fill, string? stroke, string? color)> defs,
        List<(string id, string cls)> apps)
    {
        foreach (var (id, cls) in apps)
        {
            if (!defs.TryGetValue(cls, out var d)) continue;
            var node = graph.FindNode(id.Trim());
            if (node is null) continue;
            if (d.fill   is string f) node.FillColor   = f;
            if (d.stroke is string s) node.StrokeColor = s;
            if (d.color  is string c) node.TextColor   = c;
        }
    }

    /// <summary>Splits a trailing <c>:::class</c> off a token, recording the application; returns the bare id.</summary>
    private static string StripInlineClass(string token, List<(string, string)> apps)
    {
        int idx = token.IndexOf(":::", StringComparison.Ordinal);
        if (idx < 0) return token;
        string id = token[..idx];
        string cls = token[(idx + 3)..].Trim();
        if (id.Length > 0 && cls.Length > 0) apps.Add((id, cls));
        return id;
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    /// <summary>Index of the first label-colon (a single <c>:</c> that is not part of <c>:::</c>), or -1.</summary>
    private static int FindLabelColon(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != ':') continue;
            if (i + 2 < s.Length && s[i + 1] == ':' && s[i + 2] == ':') { i += 2; continue; }
            return i;
        }
        return -1;
    }

    private static (string token, string? label) SplitTargetLabel(string right)
    {
        int c = FindLabelColon(right);
        return c < 0 ? (right.Trim(), null) : (right[..c].Trim(), right[(c + 1)..].Trim());
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
