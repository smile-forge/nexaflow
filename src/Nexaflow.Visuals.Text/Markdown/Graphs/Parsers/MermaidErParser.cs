using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses a Mermaid <c>erDiagram</c> into a <see cref="Graph"/>.  An entity is a UML-style box, so it
/// reuses the <see cref="NodeShape.ClassBox"/> node + <see cref="ClassInfo"/> with a single attribute
/// compartment; each relationship is an <see cref="Edge"/> carrying ER crow's-foot cardinality at both
/// ends (<see cref="EdgeArrow"/> <c>Er*</c>) and a solid (identifying) or dashed (non-identifying) line.
/// The shared Sugiyama layout + <c>WpfGraphRenderer</c> then draw it.
///
/// Supported: entities (bare <c>NAME</c>, quoted <c>"name with space"</c>, aliased <c>id[Alias]</c> /
/// <c>id["Multi word"]</c>) with an optional <c>{ type name [keys] ["comment"] }</c> attribute block
/// (keys <c>PK</c>/<c>FK</c>/<c>UK</c>, optional-type <c>?</c>, array/parameterised types <c>string[]</c>/
/// <c>string(99)</c>); relationships in both the symbol form (<c>||--o{</c>, <c>}o..o{</c>, even with no
/// surrounding spaces) and the word-alias form (<c>1 to zero or more</c>, <c>many(0) optionally to 0+</c>),
/// with identifying <c>--</c>/<c>to</c> vs non-identifying <c>..</c>/<c>optionally to</c>; a relationship
/// <c>: label</c>; <c>direction</c>; and styling (<c>style</c>, <c>classDef</c>, <c>class</c>, inline <c>:::</c>).
/// <c>subgraph … end</c> grouping is flattened (the entities still render, ungrouped).
/// </summary>
public sealed class MermaidErParser : IGraphParser
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

    // The relationship cardinality token: <2-char left><-- or ..><2-char right>, e.g. ||--o{ , }o..o{ .
    private static readonly Regex RxSymbol =
        new(@"[|}{o]{2}(?:--|\.\.)[|}{o]{2}", RegexOptions.Compiled);
    private static readonly Regex RxOptionallyTo =
        new(@"(?<![^\s])optionally\s+to(?![^\s])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxTo =
        new(@"(?<![^\s])to(?![^\s])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Parser ─────────────────────────────────────────────────────────────────

    private static void ParseInto(string source, Graph graph)
    {
        var classDefs = new Dictionary<string, (string? fill, string? stroke, string? color)>(StringComparer.OrdinalIgnoreCase);
        var classApps = new List<(string id, string cls)>();
        string? openId = null;   // id of the entity block currently being filled

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string firstTok = line.Split(' ', '\t')[0];
            if (firstTok.Equals("erDiagram", StringComparison.OrdinalIgnoreCase)) continue;

            // Inside an attribute block.
            if (openId is not null)
            {
                if (line == "}") { openId = null; continue; }
                AddAttribute(graph.FindNode(openId)!.Class!, line);
                continue;
            }

            line = StripInlineClasses(line, classApps);   // record + remove :::class once per line

            if (line == "}") continue;
            if (line.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
            { graph.Direction = ParseDirection(line["direction ".Length..].Trim()); continue; }
            // Subgraph grouping is flattened — skip the wrappers so the inner entities still render.
            if (firstTok.Equals("subgraph", StringComparison.OrdinalIgnoreCase) || line.Equals("end", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("style ",    StringComparison.OrdinalIgnoreCase)) { ParseStyle(line, graph); continue; }
            if (line.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase)) { ParseClassDef(line, classDefs); continue; }
            if (line.StartsWith("class ",    StringComparison.OrdinalIgnoreCase)) { ParseClassApply(line, classApps); continue; }

            // Relationship?
            if (TryRelationship(line, graph, classApps)) continue;

            // Otherwise an entity declaration: "<entity> {" (block) or a bare "<entity>".
            bool opensBlock = line.EndsWith('{');
            string token = (opensBlock ? line[..^1] : line).Trim();
            var (id, label) = ParseEntityToken(token, classApps);
            if (id.Length == 0) continue;
            EnsureEntity(graph, id, label);
            if (opensBlock) openId = id;
        }

        ApplyClasses(graph, classDefs, classApps);
    }

    // ── Entities ─────────────────────────────────────────────────────────────────

    private static Node EnsureEntity(Graph graph, string id, string? label = null)
    {
        var n = graph.GetOrAdd(id, label);
        n.Shape   = NodeShape.ClassBox;
        n.Class ??= new ClassInfo { SingleCompartment = true };
        if (label is { Length: > 0 }) n.Label = label;
        return n;
    }

    /// <summary>Parses an entity token into (id, displayLabel): bare <c>NAME</c>, quoted <c>"name"</c>, or
    /// aliased <c>id[Alias]</c> / <c>id["Multi word"]</c>; a trailing <c>:::class</c> is stripped + recorded.</summary>
    private static (string id, string label) ParseEntityToken(string token, List<(string, string)> apps)
    {
        token = StripInlineClass(token.Trim(), apps);

        int lb = token.IndexOf('[');
        if (lb > 0 && token.EndsWith(']'))
        {
            string id = token[..lb].Trim();
            string alias = token[(lb + 1)..^1].Trim().Trim('"');
            return (id, alias.Length > 0 ? alias : id);
        }
        if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
        {
            string name = token[1..^1];
            return (name, name);
        }
        return (token, token);
    }

    // ── Attributes ───────────────────────────────────────────────────────────────

    private static void AddAttribute(ClassInfo info, string line)
    {
        // Trailing quoted comment (comments cannot contain a double-quote).
        string? comment = null;
        int q = line.IndexOf('"');
        if (q >= 0)
        {
            int q2 = line.IndexOf('"', q + 1);
            comment = q2 > q ? line[(q + 1)..q2] : line[(q + 1)..];
            line = line[..q].Trim();
        }

        var toks = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (toks.Length == 0) return;

        string type = toks[0];
        string name = toks.Length > 1 ? toks[1] : string.Empty;
        var keys = toks.Length > 2
            ? string.Join(' ', toks[2..]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        string text = name.Length > 0 ? $"{type} {name}" : type;
        if (keys.Length > 0) text += "  " + string.Join(", ", keys);
        if (comment is { Length: > 0 }) text += $"  “{comment}”";

        info.Attributes.Add(new ClassMember { Text = text });
    }

    // ── Relationships ──────────────────────────────────────────────────────────────

    private static bool TryRelationship(string line, Graph graph, List<(string, string)> apps)
    {
        // :::class tokens are already stripped by the caller; split off the relationship label.
        string body = line;
        string label = string.Empty;
        int colon = LabelColon(body);
        if (colon >= 0) { label = body[(colon + 1)..].Trim().Trim('"').Trim(); body = body[..colon].Trim(); }

        EdgeArrow left, right;
        EdgeStyle style;
        string e1, e2;

        var sym = RxSymbol.Match(body);
        if (sym.Success)
        {
            e1 = body[..sym.Index].Trim();
            e2 = body[(sym.Index + sym.Length)..].Trim();
            if (e1.Length == 0 || e2.Length == 0) return false;
            string s = sym.Value;
            left  = SymLeft(s[..2]);
            right = SymRight(s[^2..]);
            style = s.Contains("--") ? EdgeStyle.Solid : EdgeStyle.Dashed;
        }
        else
        {
            var optTo = RxOptionallyTo.Match(body);
            bool dashed = optTo.Success;
            var midM = optTo.Success ? optTo : RxTo.Match(body);
            if (!midM.Success) return false;   // not a relationship — a bare entity

            string leftSide  = body[..midM.Index].Trim();
            string rightSide = body[(midM.Index + midM.Length)..].Trim();
            var lt = Tokenize(leftSide);
            var rt = Tokenize(rightSide);
            if (lt.Count < 2 || rt.Count < 2) return false;

            e1 = lt[0];
            e2 = rt[^1];
            left  = WordsToCard(string.Join(' ', lt.Skip(1)));
            right = WordsToCard(string.Join(' ', rt.Take(rt.Count - 1)));
            style = dashed ? EdgeStyle.Dashed : EdgeStyle.Solid;
        }

        var (srcId, _) = ParseEntityToken(e1, apps);
        var (dstId, _) = ParseEntityToken(e2, apps);
        if (srcId.Length == 0 || dstId.Length == 0) return false;

        EnsureEntity(graph, srcId);
        EnsureEntity(graph, dstId);
        var edge = graph.AddEdge(srcId, dstId, label, style, EdgeArrow.None);
        edge.Arrow      = right;   // target end (right cardinality)
        edge.StartArrow = left;    // source end (left cardinality)
        return true;
    }

    private static EdgeArrow SymLeft(string s) => s switch
    {
        "|o" => EdgeArrow.ErZeroOne,
        "||" => EdgeArrow.ErExactlyOne,
        "}o" => EdgeArrow.ErZeroMany,
        "}|" => EdgeArrow.ErOneMany,
        _    => EdgeArrow.ErExactlyOne,
    };

    private static EdgeArrow SymRight(string s) => s switch
    {
        "o|" => EdgeArrow.ErZeroOne,
        "||" => EdgeArrow.ErExactlyOne,
        "o{" => EdgeArrow.ErZeroMany,
        "|{" => EdgeArrow.ErOneMany,
        _    => EdgeArrow.ErExactlyOne,
    };

    private static EdgeArrow WordsToCard(string w) => w.Trim().ToLowerInvariant() switch
    {
        "one or zero" or "zero or one"                       => EdgeArrow.ErZeroOne,
        "one or more" or "one or many" or "many(1)" or "1+"  => EdgeArrow.ErOneMany,
        "zero or more" or "zero or many" or "many(0)" or "0+"=> EdgeArrow.ErZeroMany,
        "only one" or "1"                                    => EdgeArrow.ErExactlyOne,
        _                                                    => EdgeArrow.ErExactlyOne,
    };

    // ── Styling (mirrors the requirement / class parsers) ────────────────────────

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
        var (fill, stroke, color) = ReadStyleProps(rest[(sp + 1)..]);
        foreach (var id in rest[..sp].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var n = EnsureEntity(graph, id);
            if (fill   is not null) n.FillColor   = fill;
            if (stroke is not null) n.StrokeColor = stroke;
            if (color  is not null) n.TextColor   = color;
        }
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
            if (key == "color")                   color  = val;
        }
        return (fill, stroke, color);
    }

    private static void ApplyClasses(Graph graph,
        Dictionary<string, (string? fill, string? stroke, string? color)> defs,
        List<(string id, string cls)> apps)
    {
        // A classDef named "default" applies to every node lacking a specific class.
        if (defs.TryGetValue("default", out var def))
            foreach (var n in graph.Nodes)
            {
                if (def.fill   is string f && n.FillColor   is null) n.FillColor   = f;
                if (def.stroke is string s && n.StrokeColor is null) n.StrokeColor = s;
                if (def.color  is string c && n.TextColor   is null) n.TextColor   = c;
            }

        foreach (var (id, cls) in apps)
        {
            if (!defs.TryGetValue(cls, out var d)) continue;
            if (graph.FindNode(id.Trim()) is not { } node) continue;
            if (d.fill   is string f) node.FillColor   = f;
            if (d.stroke is string s) node.StrokeColor = s;
            if (d.color  is string c) node.TextColor   = c;
        }
    }

    // ── Small helpers ──────────────────────────────────────────────────────────────

    private static string StripInlineClass(string token, List<(string, string)> apps)
    {
        int idx = token.IndexOf(":::", StringComparison.Ordinal);
        if (idx < 0) return token;
        string id  = token[..idx].Trim();
        string cls = token[(idx + 3)..].Trim();
        foreach (var c in cls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (id.Length > 0) apps.Add((id, c));
        return id;
    }

    /// <summary>Removes every <c>:::class</c> occurrence from a line (recording the applications), leaving
    /// entity ids and the relationship intact.</summary>
    private static string StripInlineClasses(string line, List<(string, string)> apps) =>
        Regex.Replace(line, @"(?<id>[\w\-.""]+):::(?<cls>[\w,]+)", m =>
        {
            string id = m.Groups["id"].Value;
            foreach (var c in m.Groups["cls"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                apps.Add((id, c));
            return id;
        });

    /// <summary>Index of the relationship label ':' (the first ':' outside double quotes), or -1.</summary>
    private static int LabelColon(string s)
    {
        bool inQuotes = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuotes = !inQuotes;
            else if (s[i] == ':' && !inQuotes) return i;
        }
        return -1;
    }

    /// <summary>Whitespace tokeniser that keeps a double-quoted span as a single token.</summary>
    private static List<string> Tokenize(string s)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char ch in s)
        {
            if (ch == '"') { inQuotes = !inQuotes; sb.Append(ch); }
            else if (char.IsWhiteSpace(ch) && !inQuotes) { if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); } }
            else sb.Append(ch);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    private static GraphDirection ParseDirection(string d) => d.ToUpperInvariant() switch
    {
        "LR"         => GraphDirection.LeftRight,
        "RL"         => GraphDirection.RightLeft,
        "BT"         => GraphDirection.BottomUp,
        _            => GraphDirection.TopDown,
    };
}
