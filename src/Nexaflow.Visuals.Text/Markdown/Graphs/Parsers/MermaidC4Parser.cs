using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses C4 diagrams into a <see cref="C4Diagram"/>.
///
/// The header is Mermaid's (<c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>,
/// <c>C4Dynamic</c>, <c>C4Deployment</c>, plus Nexaflow's <c>C4Sequence</c>), but the body is the
/// full C4-PlantUML macro set rather than Mermaid's subset — that is deliberate: Mermaid supports a
/// fraction of what people actually write, and the two agree wherever Mermaid does have an opinion,
/// so accepting the larger language costs nothing and rejects far less.
///
/// <code>
/// C4Container
///   title Containers of the Internet Banking System
///   Person(customer, "Banking Customer", "A customer of the bank", $tags="v1")
///   System_Boundary(c1, "Internet Banking") {
///     Container(web, "Web Application", "Java, Spring MVC", "Delivers the SPA")
///     ContainerDb(db, "Database", "SQL", "Stores users")
///   }
///   Rel(customer, web, "Visits", "HTTPS", $index=Index())
///   UpdateElementStyle("person", $bgColor="#08427b")
///   SHOW_LEGEND()
/// </code>
///
/// Ignored on purpose (parsed, then dropped): <c>Lay_*</c> and the <c>_U/_D/_L/_R</c> direction
/// hints, <c>$sprite</c>, <c>UpdateLayoutConfig</c> and <c>UpdateRelStyle</c>'s pixel offsets — all
/// of them tune graphviz's placement, and placement here is the shared Sugiyama layout's to decide.
/// Never throws; returns a (possibly empty) diagram.
/// </summary>
public sealed class MermaidC4Parser
{
    public bool CanParse(string language) =>
        language.StartsWith("c4", StringComparison.OrdinalIgnoreCase);

    public C4Diagram Parse(string source)
    {
        var diagram = new C4Diagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, C4Diagram diagram)
    {
        var open = new Stack<C4Boundary>();
        var counter = new C4IndexCounter();
        bool headerSeen = false;

        foreach (var rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = C4MacroReader.StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            if (!headerSeen && TryHeader(line, diagram)) { headerSeen = true; continue; }

            // PlantUML wrapper lines a pasted diagram brings with it.
            if (line.StartsWith('@') || line.StartsWith("!")) continue;

            // A closing brace ends the innermost boundary.
            if (line == "}" || line == "})")
            {
                CloseBoundary(open, diagram);
                continue;
            }

            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                diagram.Title = C4MacroReader.Unquote(line[6..]);
                continue;
            }

            if (C4MacroReader.TryRead(line, out var macro) && Apply(macro, diagram, open, counter))
                continue;

            // Not a macro we know — keep it in order so a C4 sequence can replay it through the
            // native sequence parser (alt/loop/note/activate all arrive this way).
            diagram.Statements.Add(new C4RawLine { Line = line });
        }

        while (open.Count > 0) CloseBoundary(open, diagram);
    }

    private static bool TryHeader(string line, C4Diagram diagram)
    {
        string word = line.Split(' ', '\t')[0].ToLowerInvariant();
        C4DiagramKind? kind = word switch
        {
            "c4context"    => C4DiagramKind.Context,
            "c4container"  => C4DiagramKind.Container,
            "c4component"  => C4DiagramKind.Component,
            "c4dynamic"    => C4DiagramKind.Dynamic,
            "c4deployment" => C4DiagramKind.Deployment,
            "c4sequence"   => C4DiagramKind.Sequence,
            _              => null,
        };
        if (kind is null) return false;

        diagram.Kind = kind.Value;
        // C4 sequence numbers its messages only when asked; a dynamic diagram is *about* the order.
        diagram.ShowIndex = kind == C4DiagramKind.Dynamic;
        return true;
    }

    // ── Macro dispatch ───────────────────────────────────────────────────────

    private static bool Apply(C4Macro m, C4Diagram diagram, Stack<C4Boundary> open, C4IndexCounter counter)
    {
        string name = m.Name;

        if (TryElementKind(name, out var kind, out var shape, out bool external))
        {
            AddElement(m, diagram, open, kind, shape, external);
            return true;
        }

        switch (name.ToLowerInvariant())
        {
            // ── Boundaries ──
            case "boundary":              AddBoundary(m, diagram, open, type: m.Arg(2, "type"), deployment: false); return true;
            case "enterprise_boundary":   AddBoundary(m, diagram, open, "Enterprise", deployment: false); return true;
            case "system_boundary":       AddBoundary(m, diagram, open, "System",     deployment: false); return true;
            case "container_boundary":    AddBoundary(m, diagram, open, "Container",  deployment: false); return true;
            case "deployment_node":
            case "node":
            case "node_l":
            case "node_r":                AddBoundary(m, diagram, open, m.Arg(2, "type"), deployment: true); return true;
            case "boundary_end":          CloseBoundary(open, diagram); return true;

            // ── Relationships ──
            case "rel":
            case "rel_u": case "rel_up":
            case "rel_d": case "rel_down":
            case "rel_l": case "rel_left":
            case "rel_r": case "rel_right":
            case "rel_neighbor":
                AddRelationship(m, diagram, counter, offset: 0, back: false, bidirectional: false); return true;

            case "rel_back":
            case "rel_back_neighbor":
                AddRelationship(m, diagram, counter, offset: 0, back: true, bidirectional: false); return true;

            case "birel":
            case "birel_u": case "birel_up":
            case "birel_d": case "birel_down":
            case "birel_l": case "birel_left":
            case "birel_r": case "birel_right":
            case "birel_neighbor":
                AddRelationship(m, diagram, counter, offset: 0, back: false, bidirectional: true); return true;

            case "relindex":
                AddRelationship(m, diagram, counter, offset: 1, back: false, bidirectional: false); return true;

            // Layout-only hints: they exist to nudge graphviz, and the layout here is not graphviz.
            case "lay_u": case "lay_up":
            case "lay_d": case "lay_down":
            case "lay_l": case "lay_left":
            case "lay_r": case "lay_right":
            case "lay_distance":
            case "updatelayoutconfig":
                return true;

            // ── Styling ──
            case "updateelementstyle":
            {
                string? target = m.Arg(0, "elementName");
                if (target is not null) Merge(diagram.ElementStyles, target, ReadStyle(m, first: 1));
                return true;
            }
            case "addelementtag":
            case "addboundarytag":
            {
                string? tag = m.Arg(0, "tagStereo");
                if (tag is not null) Merge(diagram.Tags, tag, ReadStyle(m, first: 1));
                return true;
            }
            case "updateboundarystyle":
            {
                string? target = m.Arg(0, "elementName") ?? "boundary";
                Merge(diagram.Tags, target, ReadStyle(m, first: 1));
                return true;
            }
            case "addreltag":
            {
                string? tag = m.Arg(0, "tagStereo");
                if (tag is not null) diagram.RelTags[tag] = ReadRelStyle(m, first: 1);
                return true;
            }
            case "updaterelstyle":
            {
                string? from = m.Arg(0, "from"), to = m.Arg(1, "to");
                if (from is not null && to is not null) diagram.RelStyles[(from, to)] = ReadRelStyle(m, first: 2);
                return true;
            }

            // ── Whole-diagram switches ──
            case "show_legend":
            case "layout_with_legend":
                diagram.ShowLegend = true;
                if (m.Flag(0, "hideStereotype", whenAbsent: false)) diagram.HideStereotype = true;
                return true;
            case "hide_stereotype":            diagram.HideStereotype = true; return true;
            case "layout_top_down":            diagram.Direction = GraphDirection.TopDown; return true;
            case "layout_left_right":
            case "layout_landscape":           diagram.Direction = GraphDirection.LeftRight; return true;
            case "show_person_outline":        diagram.PersonStyle = C4PersonStyle.Outline; return true;
            case "show_person_portrait":       diagram.PersonStyle = C4PersonStyle.Portrait; return true;
            case "show_person_sprite":         diagram.PersonStyle = C4PersonStyle.Default; return true;
            case "hide_person_sprite":         diagram.PersonStyle = C4PersonStyle.Default; return true;
            case "show_element_descriptions":  diagram.ShowElementDescriptions = m.Flag(0, "show"); return true;
            case "show_index":                 diagram.ShowIndex = m.Flag(0, "show"); return true;
            case "show_foot_boxes":            diagram.ShowFootBoxes = m.Flag(0, "show"); return true;

            // Counter procedures — they move the index without drawing anything.
            case "increment":
                counter.Increment(int.TryParse(m.Arg(0, "offset"), out int by) ? by : 1);
                return true;
            case "setindex":
                if (int.TryParse(m.Arg(0, "new_index"), out int at)) counter.Set(at);
                return true;

            default: return false;
        }
    }

    // ── Elements ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Decomposes an element macro's name into what it means. The names are systematic —
    /// <c>{Person|System|Container|Component}{Db|Queue}?{_Ext}?</c> — so this is a decomposition
    /// rather than a table of every combination.
    /// </summary>
    private static bool TryElementKind(string name, out C4ElementKind kind, out C4ElementShape shape, out bool external)
    {
        kind = C4ElementKind.System;
        shape = C4ElementShape.Box;
        external = false;

        string n = name.ToLowerInvariant();
        if (n.EndsWith("_ext")) { external = true; n = n[..^4]; }

        string? rest = null;
        if      (n.StartsWith("person"))    { kind = C4ElementKind.Person;    rest = n["person".Length..]; }
        else if (n.StartsWith("system"))    { kind = C4ElementKind.System;    rest = n["system".Length..]; }
        else if (n.StartsWith("container")) { kind = C4ElementKind.Container; rest = n["container".Length..]; }
        else if (n.StartsWith("component")) { kind = C4ElementKind.Component; rest = n["component".Length..]; }
        if (rest is null) return false;

        // "Container_Boundary" starts with "container" but is not a container.
        switch (rest)
        {
            case "":      shape = C4ElementShape.Box;      return true;
            case "db":    shape = C4ElementShape.Database; return true;
            case "queue": shape = C4ElementShape.Queue;    return true;
            default:      return false;
        }
    }

    private static void AddElement(
        C4Macro m, C4Diagram diagram, Stack<C4Boundary> open,
        C4ElementKind kind, C4ElementShape shape, bool external)
    {
        string? alias = m.Arg(0, "alias");
        if (alias is null) return;

        // Person and System take (alias, label, descr, …); Container and Component insert the
        // technology at index 2 and push the description to 3. That asymmetry is C4-PlantUML's.
        bool hasTechn = kind is C4ElementKind.Container or C4ElementKind.Component;

        var element = new C4Element
        {
            Alias = alias,
            Label = m.Arg(1, "label") ?? alias,
            Kind = kind,
            Shape = shape,
            External = external,
            Technology = hasTechn ? m.Arg(2, "techn") : m.Named.GetValueOrDefault("techn"),
            Description = m.Arg(hasTechn ? 3 : 2, "descr"),
            Type = m.Named.GetValueOrDefault("type"),
            Link = m.Named.GetValueOrDefault("link"),
            OwnerId = open.Count > 0 ? open.Peek().Alias : null,
        };
        element.Tags.AddRange(C4MacroReader.SplitTags(m.Arg(hasTechn ? 5 : 4, "tags")));

        if (open.Count > 0) open.Peek().MemberIds.Add(alias);
        diagram.Elements.Add(element);
        diagram.Statements.Add(new C4ElementStatement { Element = element });
    }

    // ── Boundaries ───────────────────────────────────────────────────────────

    private static void AddBoundary(C4Macro m, C4Diagram diagram, Stack<C4Boundary> open, string? type, bool deployment)
    {
        string? alias = m.Arg(0, "alias");
        if (alias is null) return;

        var boundary = new C4Boundary
        {
            Alias = alias,
            Label = m.Arg(1, "label") ?? alias,
            Type = type,
            // A deployment node takes (alias, label, type, descr); a boundary has no description slot.
            Description = deployment ? m.Arg(3, "descr") : m.Named.GetValueOrDefault("descr"),
            Link = m.Named.GetValueOrDefault("link"),
            IsDeploymentNode = deployment,
            ParentId = open.Count > 0 ? open.Peek().Alias : null,
        };
        boundary.Tags.AddRange(C4MacroReader.SplitTags(m.Named.GetValueOrDefault("tags")));

        if (open.Count > 0) open.Peek().MemberIds.Add(alias);
        diagram.Boundaries.Add(boundary);
        diagram.Statements.Add(new C4BoundaryBegin { Boundary = boundary });
        open.Push(boundary);
    }

    private static void CloseBoundary(Stack<C4Boundary> open, C4Diagram diagram)
    {
        if (open.Count == 0) return;
        open.Pop();
        diagram.Statements.Add(new C4BoundaryEnd());
    }

    // ── Relationships ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a relationship. <paramref name="offset"/> is 1 for <c>RelIndex</c>, whose first
    /// positional is the index and which therefore shifts every other argument along by one.
    /// </summary>
    private static void AddRelationship(C4Macro m, C4Diagram diagram, C4IndexCounter counter, int offset, bool back, bool bidirectional)
    {
        string? from = m.Arg(offset, "from");
        string? to = m.Arg(offset + 1, "to");
        if (from is null || to is null) return;

        int? index = offset == 1
            ? counter.Resolve(m.Arg(0, "index"))
            : counter.Resolve(m.Named.GetValueOrDefault("index"));

        var rel = new C4Relationship
        {
            From = from,
            To = to,
            Label = m.Arg(offset + 2, "label") ?? string.Empty,
            Technology = m.Arg(offset + 3, "techn"),
            Description = m.Arg(offset + 4, "descr"),
            Link = m.Named.GetValueOrDefault("link"),
            Index = index,
            Bidirectional = bidirectional,
            Back = back,
        };
        rel.Tags.AddRange(C4MacroReader.SplitTags(m.Arg(offset + 6, "tags")));

        diagram.Relationships.Add(rel);
        diagram.Statements.Add(new C4RelStatement { Relationship = rel });
    }

    // ── Styles ───────────────────────────────────────────────────────────────

    private static void Merge(Dictionary<string, C4Style> into, string key, C4Style style) =>
        into[key] = into.TryGetValue(key, out var existing) ? existing.Merge(style) : style;

    private static C4Style ReadStyle(C4Macro m, int first) => new()
    {
        BgColor         = m.Arg(first,     "bgColor"),
        FontColor       = m.Arg(first + 1, "fontColor"),
        BorderColor     = m.Arg(first + 2, "borderColor"),
        Shape           = ShapeOf(m.Named.GetValueOrDefault("shape")),
        Technology      = m.Named.GetValueOrDefault("techn"),
        LegendText      = m.Named.GetValueOrDefault("legendText"),
        BorderStyle     = LineStyleOf(m.Named.GetValueOrDefault("borderStyle")),
        BorderThickness = Number(m.Named.GetValueOrDefault("borderThickness")),
    };

    private static C4RelStyle ReadRelStyle(C4Macro m, int first) => new()
    {
        TextColor  = m.Arg(first,     "textColor"),
        LineColor  = m.Arg(first + 1, "lineColor"),
        LineStyle  = LineStyleOf(m.Named.GetValueOrDefault("lineStyle")),
        LegendText = m.Named.GetValueOrDefault("legendText"),
    };

    private static C4ElementShape? ShapeOf(string? shape) => shape?.ToLowerInvariant() switch
    {
        "roundedboxshape" or "rounded" => C4ElementShape.Box,
        "eightsidedshape"              => C4ElementShape.Box,
        "database" or "db"             => C4ElementShape.Database,
        "queue"                        => C4ElementShape.Queue,
        _                              => null,
    };

    private static EdgeStyle? LineStyleOf(string? style) => style?.ToLowerInvariant() switch
    {
        "dashedline" or "dashed" => EdgeStyle.Dashed,
        "dottedline" or "dotted" => EdgeStyle.Dotted,
        "boldline"   or "bold"   => EdgeStyle.Thick,
        "solidline"  or "solid"  => EdgeStyle.Solid,
        _                        => null,
    };

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null;
}
