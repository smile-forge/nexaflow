using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>requirementDiagram</c> into a <see cref="Graph"/>. A requirement / element is
/// structurally a UML box, so it reuses the <see cref="NodeShape.ClassBox"/> node + <see cref="ClassInfo"/>
/// (a «type» stereotype + the name over a single compartment of <c>key: value</c> fields); each
/// relationship is a dashed, open-arrow <see cref="Edge"/> whose label is the relationship type. The
/// shared Sugiyama layout + <c>WpfGraphRenderer</c> then draw it.
///
/// Supported:
///   • Requirements: <c>&lt;type&gt; name { id: … text: … risk: … verifymethod: … }</c> for every type
///     (requirement, functionalRequirement, interfaceRequirement, performanceRequirement,
///     physicalRequirement, designConstraint)
///   • Elements: <c>element name { type: … docref: … }</c>
///   • Relationships <c>src - &lt;type&gt; -&gt; dst</c> and the reverse <c>dst &lt;- &lt;type&gt; - src</c>
///     (contains, copies, derives, satisfies, verifies, refines, traces)
///   • <c>direction</c>, <c>%%</c> comments, <c>accTitle</c>/<c>accDescr</c>
///   • Styling: <c>style id fill:…</c>, <c>classDef name …</c>, <c>class a,b name</c>, inline <c>id:::name</c>
/// </summary>
public sealed class MermaidRequirementParser : IGraphParser
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

    private static readonly HashSet<string> ReqTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "requirement", "functionalRequirement", "interfaceRequirement",
        "performanceRequirement", "physicalRequirement", "designConstraint",
    };

    // Relationship lines: "src - type -> dst"  and the reverse "dst <- type - src".
    private static readonly Regex RxRelFwd =
        new(@"^(?<src>.+?)\s+-\s+(?<type>\w+)\s+->\s+(?<dst>.+?)$", RegexOptions.Compiled);
    private static readonly Regex RxRelRev =
        new(@"^(?<dst>.+?)\s+<-\s+(?<type>\w+)\s+-\s+(?<src>.+?)$", RegexOptions.Compiled);

    // ── Parser ─────────────────────────────────────────────────────────────────

    private static void ParseInto(string source, Graph graph)
    {
        var classDefs = new Dictionary<string, (string? fill, string? stroke, string? color)>(StringComparer.OrdinalIgnoreCase);
        var classApps = new List<(string id, string cls)>();
        string? openId = null;   // id of the requirement/element block currently being filled

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string firstTok = line.Split(' ', '\t')[0];

            // Header
            if (firstTok.StartsWith("requirementdiagram", StringComparison.OrdinalIgnoreCase)) continue;

            // Inside a block: "key: value" fields until the closing brace.
            if (openId is not null)
            {
                if (line == "}") { openId = null; continue; }
                int c = line.IndexOf(':');
                if (c > 0) AddField(graph.FindNode(openId)!.Class!, line[..c], line[(c + 1)..]);
                continue;
            }

            if (line == "}") continue;

            if (line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
            {
                graph.Direction = ParseDirection(line["direction ".Length..].Trim());
                continue;
            }
            if (line.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)) continue;

            // Styling
            if (line.StartsWith("style ",    StringComparison.OrdinalIgnoreCase)) { ParseStyle(line, graph); continue; }
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase)) { ParseClassDef(line, classDefs); continue; }
            if (line.StartsWith("class ",    StringComparison.OrdinalIgnoreCase)) { ParseClassApply(line, classApps); continue; }

            // Block opener: "<type> name {"
            if (line.EndsWith('{'))
            {
                var head  = line[..^1].Trim();
                var parts = head.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                string type = parts.Length > 0 ? parts[0] : "";
                if (parts.Length >= 1 && (ReqTypes.Contains(type) || type.Equals("element", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = StripInlineClass(parts.Length > 1 ? parts[1].Trim() : type, classApps);
                    if (IsIdent(name)) { ReqNode(graph, name, type); openId = name; }
                    continue;
                }
            }

            // Relationship
            if (TryParseRelationship(line, graph)) continue;
        }

        ApplyClasses(graph, classDefs, classApps);
    }

    // ── Nodes / fields ───────────────────────────────────────────────────────────

    /// <summary>Ensures <paramref name="id"/> is a requirement/element box; sets its «type» stereotype.</summary>
    private static Node ReqNode(Graph graph, string id, string type)
    {
        var n = EnsureNode(graph, id);
        n.Class!.Stereotype = PrettyType(type);
        return n;
    }

    /// <summary>Ensures <paramref name="id"/> exists as a single-compartment box (for a relationship endpoint
    /// that may not have its own block).</summary>
    private static Node EnsureNode(Graph graph, string id)
    {
        var n = graph.GetOrAdd(id);
        n.Shape   = NodeShape.ClassBox;
        n.Class ??= new ClassInfo { SingleCompartment = true };
        return n;
    }

    private static void AddField(ClassInfo info, string keyRaw, string valueRaw)
    {
        string key   = keyRaw.Trim();
        string value = valueRaw.Trim().Trim('"').Trim();
        if (key.Length == 0) return;

        string label = key.ToLowerInvariant() switch
        {
            "id"           => "Id",
            "text"         => "Text",
            "risk"         => "Risk",
            "verifymethod" => "Verification",
            "type"         => "Type",
            "docref"       => "Doc Ref",
            _              => key,
        };
        // Enum-ish values (risk / method) read nicer title-cased.
        if (key.Equals("risk", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("verifymethod", StringComparison.OrdinalIgnoreCase))
            value = TitleCase(value);

        info.Attributes.Add(new ClassMember { Text = $"{label}: {value}" });
    }

    // ── Relationships ────────────────────────────────────────────────────────────

    private static bool TryParseRelationship(string line, Graph graph)
    {
        Match m = RxRelFwd.Match(line);
        if (!m.Success) m = RxRelRev.Match(line);
        if (!m.Success) return false;

        string src  = m.Groups["src"].Value.Trim();
        string dst  = m.Groups["dst"].Value.Trim();
        string type = m.Groups["type"].Value.Trim();
        if (!IsIdent(src) || !IsIdent(dst)) return false;

        EnsureNode(graph, src);
        EnsureNode(graph, dst);

        // `contains` is a composite-containment (SysML): a solid line with a crosshair circle at the
        // container (source) end and no target arrowhead. Every other relationship is a dashed open arrow.
        if (type.Equals("contains", StringComparison.OrdinalIgnoreCase))
        {
            var e = graph.AddEdge(src, dst, $"«{type}»", EdgeStyle.Solid, EdgeArrow.None);
            e.StartArrow = EdgeArrow.CrossCircle;
        }
        else
        {
            graph.AddEdge(src, dst, $"«{type}»", EdgeStyle.Dashed, EdgeArrow.Open);
        }
        return true;
    }

    // ── Styling (mirrors the class parser) ──────────────────────────────────────

    private static void ParseClassDef(string line, Dictionary<string, (string?, string?, string?)> defs)
    {
        var rest = line["classDef ".Length..].Trim().TrimEnd(';');
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;
        defs[rest[..sp]] = ReadStyleProps(rest[(sp + 1)..]);
    }

    private static void ParseClassApply(string line, List<(string, string)> apps)
    {
        var rest = line["class ".Length..].Trim().TrimEnd(';');
        int sp = rest.LastIndexOf(' ');
        if (sp <= 0) return;
        string cls = rest[(sp + 1)..].Trim();
        foreach (var id in rest[..sp].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            apps.Add((id.Trim(), cls));
    }

    private static void ParseStyle(string line, Graph graph)
    {
        var rest = line["style ".Length..].Trim().TrimEnd(';');
        int sp = rest.IndexOf(' ');
        if (sp < 0) return;
        string id = rest[..sp].Trim();
        if (!IsIdent(id)) return;
        var n = EnsureNode(graph, id);
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
        foreach (var (id, cls) in apps)
        {
            if (!defs.TryGetValue(cls, out var d)) continue;
            if (graph.FindNode(id.Trim()) is not { } n) continue;
            if (d.fill   is string f) n.FillColor   = f;
            if (d.stroke is string s) n.StrokeColor = s;
            if (d.color  is string c) n.TextColor   = c;
        }
    }

    private static string StripInlineClass(string token, List<(string, string)> apps)
    {
        int idx = token.IndexOf(":::", StringComparison.Ordinal);
        if (idx < 0) return token;
        string id = token[..idx].Trim();
        string cls = token[(idx + 3)..].Trim();
        if (id.Length > 0 && cls.Length > 0) apps.Add((id, cls));
        return id;
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    /// <summary>"functionalRequirement" → "Functional Requirement"; "element" → "Element".</summary>
    private static string PrettyType(string type)
    {
        if (type.Length == 0) return type;
        var sb = new System.Text.StringBuilder();
        sb.Append(char.ToUpperInvariant(type[0]));
        for (int i = 1; i < type.Length; i++)
        {
            if (char.IsUpper(type[i])) sb.Append(' ');
            sb.Append(type[i]);
        }
        return sb.ToString();
    }

    private static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
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
