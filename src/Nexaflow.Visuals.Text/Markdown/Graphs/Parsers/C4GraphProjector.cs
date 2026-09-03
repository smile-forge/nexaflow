using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Projects a parsed <see cref="C4Diagram"/> onto the shared <see cref="Graph"/> model, so C4
/// Context/Container/Component/Dynamic/Deployment diagrams go through the same Sugiyama layout,
/// renderer, viewport, selection and expansion as flowcharts and class diagrams.
///
/// This is the whole reason C4 needed no layout engine of its own: a C4 diagram *is* a node-and-edge
/// graph with richer boxes. Elements become <see cref="NodeShape.C4Element"/> nodes, boundaries and
/// deployment nodes become nested <see cref="Subgraph"/>s, and relationships become edges whose
/// second label line carries the technology.
///
/// WPF-free — colours travel as the strings the author wrote, and the renderer resolves them.
/// </summary>
public static class C4GraphProjector
{
    public static Graph ToGraph(C4Diagram d)
    {
        var graph = new Graph { Title = d.Title };
        if (d.Direction is GraphDirection dir) graph.Direction = dir;

        foreach (var element in d.Elements) AddNode(graph, d, element);
        foreach (var boundary in d.Boundaries) AddSubgraph(graph, d, boundary);
        foreach (var rel in d.Relationships) AddEdge(graph, d, rel);

        if (d.ShowLegend)
        {
            graph.Legend = C4LegendBuilder.Build(d);
            graph.LegendDetails = d.LegendDetails;
        }
        return graph;
    }

    // ── Elements ─────────────────────────────────────────────────────────────

    private static void AddNode(Graph graph, C4Diagram d, C4Element element)
    {
        var node = graph.GetOrAdd(element.Alias, element.Label);
        node.Shape = NodeShape.C4Element;
        node.Href = element.Link;
        node.Tooltip = element.Description;

        var info = new C4ElementInfo
        {
            Kind = element.Kind,
            Shape = ShapeFor(element, d.PersonStyle),
            External = element.External,
            Technology = element.Technology,
            Description = element.Description,
            StereotypeOverride = element.Type,
            HideStereotype = d.HideStereotype,
        };
        info.Tags.AddRange(element.Tags);

        // Style resolution, weakest first: the element's type name, then its tags in order, then a
        // style naming this element's own alias (Mermaid's dialect) — the most specific wins.
        var style = new C4Style();
        foreach (var key in C4LegendBuilder.TypeKeys(element))
            if (d.ElementStyles.TryGetValue(key, out var byType)) style = style.Merge(byType);
        foreach (var tag in element.Tags)
            if (d.Tags.TryGetValue(tag, out var byTag)) style = style.Merge(byTag);
        if (d.ElementStyles.TryGetValue(element.Alias, out var byAlias)) style = style.Merge(byAlias);

        info.FillColor = style.BgColor;
        info.FontColor = style.FontColor;
        info.BorderColor = style.BorderColor;
        if (style.Shape is C4ElementShape s) info.Shape = s;
        if (style.BorderStyle is EdgeStyle bs) info.BorderStyle = bs;
        if (style.BorderThickness is double bt) info.BorderThickness = bt;
        if (style.Technology is { Length: > 0 } t && info.Technology is null) info.Technology = t;

        node.C4 = info;
    }


    private static C4ElementShape ShapeFor(C4Element element, C4PersonStyle personStyle)
    {
        if (element.Kind != C4ElementKind.Person || element.Shape != C4ElementShape.Box)
            return element.Shape;

        return personStyle switch
        {
            C4PersonStyle.Outline  => C4ElementShape.PersonOutline,
            C4PersonStyle.Portrait => C4ElementShape.PersonPortrait,
            _                      => C4ElementShape.Person,
        };
    }

    // ── Boundaries ───────────────────────────────────────────────────────────

    private static void AddSubgraph(Graph graph, C4Diagram d, C4Boundary boundary)
    {
        var style = new SubgraphStyle
        {
            SubLabel = SubLabelFor(boundary),
            // A deployment node is a physical box and reads better solid; a logical boundary keeps
            // the dashed outline the rest of the graph family uses for grouping.
            BorderStyle = boundary.IsDeploymentNode ? EdgeStyle.Solid : EdgeStyle.Dashed,
        };

        foreach (var tag in boundary.Tags)
        {
            if (!d.Tags.TryGetValue(tag, out var tagStyle)) continue;
            style.FillColor ??= tagStyle.BgColor;
            style.StrokeColor ??= tagStyle.BorderColor ?? tagStyle.BgColor;
            style.TextColor ??= tagStyle.FontColor;
        }
        if (d.ElementStyles.TryGetValue(boundary.Alias, out var byAlias))
        {
            style.FillColor = byAlias.BgColor ?? style.FillColor;
            style.StrokeColor = byAlias.BorderColor ?? style.StrokeColor;
            style.TextColor = byAlias.FontColor ?? style.TextColor;
        }

        var subgraph = new Subgraph
        {
            Id = boundary.Alias,
            Label = boundary.Label,
            ParentId = boundary.ParentId,
            Href = boundary.Link,
            Tooltip = boundary.Description,
            Style = style,
        };

        // Only element members belong in NodeIds; a nested boundary is joined by its own ParentId.
        foreach (var memberId in boundary.MemberIds)
            if (d.FindElement(memberId) is not null)
                subgraph.NodeIds.Add(memberId);

        graph.Subgraphs.Add(subgraph);
    }

    private static string? SubLabelFor(C4Boundary boundary)
    {
        if (boundary.IsDeploymentNode)
            return boundary.Type is { Length: > 0 } t ? $"[Deployment Node: {t}]" : "[Deployment Node]";
        return boundary.Type is { Length: > 0 } type ? $"[{type}]" : null;
    }

    // ── Relationships ────────────────────────────────────────────────────────

    private static void AddEdge(Graph graph, C4Diagram d, C4Relationship rel)
    {
        // Rel_Back declares from→to but points the other way, so the edge is simply built reversed —
        // the layout then treats it as the dependency it actually is.
        string source = rel.Back ? rel.To : rel.From;
        string target = rel.Back ? rel.From : rel.To;

        // An endpoint that was never declared still has to exist, or the edge would vanish.
        EnsurePlaceholder(graph, source);
        EnsurePlaceholder(graph, target);

        var edge = graph.AddEdge(source, target);
        edge.Label = LabelFor(d, rel);
        edge.SubLabel = rel.Technology is { Length: > 0 } t ? $"[{t}]" : null;
        edge.Href = rel.Link;
        edge.Tooltip = rel.Description;
        if (rel.Bidirectional) edge.StartArrow = EdgeArrow.Normal;

        var style = new C4RelStyle();
        foreach (var tag in rel.Tags)
            if (d.RelTags.TryGetValue(tag, out var byTag))
                style = new C4RelStyle
                {
                    TextColor = byTag.TextColor ?? style.TextColor,
                    LineColor = byTag.LineColor ?? style.LineColor,
                    LineStyle = byTag.LineStyle ?? style.LineStyle,
                };
        if (d.RelStyles.TryGetValue((rel.From, rel.To), out var byPair))
            style = new C4RelStyle
            {
                TextColor = byPair.TextColor ?? style.TextColor,
                LineColor = byPair.LineColor ?? style.LineColor,
                LineStyle = byPair.LineStyle ?? style.LineStyle,
            };
        if (style.LineStyle is EdgeStyle ls) edge.Style = ls;
        edge.LineColor = style.LineColor;
        edge.TextColor = style.TextColor;
    }

    /// <summary>The edge's first line: the label, prefixed with its number when the diagram counts.</summary>
    private static string LabelFor(C4Diagram d, C4Relationship rel)
    {
        string label = rel.Label;
        if (rel.Description is { Length: > 0 } descr && descr != label)
            label = label.Length > 0 ? $"{label}\n{descr}" : descr;
        return d.ShowIndex && rel.Index is int i ? $"{i}: {label}" : label;
    }

    private static void EnsurePlaceholder(Graph graph, string id)
    {
        if (graph.FindNode(id) is not null) return;
        var node = graph.GetOrAdd(id, id);
        node.Shape = NodeShape.C4Element;
        node.C4 ??= new C4ElementInfo { Kind = C4ElementKind.System, HideStereotype = true };
    }

}
