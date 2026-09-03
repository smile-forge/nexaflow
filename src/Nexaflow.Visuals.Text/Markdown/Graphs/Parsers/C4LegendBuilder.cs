using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Builds the legend rows a <c>SHOW_LEGEND()</c> asks for.
///
/// Shared by both C4 projections rather than living on either: a legend describes the diagram's
/// vocabulary, which is the same question whether the diagram was laid out as a graph or as a
/// sequence. Writing it twice is how the two would drift.
/// </summary>
internal static class C4LegendBuilder
{
    /// <summary>
    /// One row per distinct element flavour actually present, plus one per tag that named its own
    /// legend text. Listing every C4 kind regardless would describe the notation rather than the
    /// diagram in front of the reader.
    /// </summary>
    internal static List<GraphLegendEntry> Build(C4Diagram d)
    {
        var entries = new List<GraphLegendEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in d.Elements)
        {
            string label = new C4ElementInfo
            {
                Kind = element.Kind, External = element.External, Shape = element.Shape,
            }.Stereotype().Trim('[', ']');

            if (element.Shape == C4ElementShape.Database) label += " (database)";
            else if (element.Shape == C4ElementShape.Queue) label += " (queue)";
            if (!seen.Add(label)) continue;

            string? fill = null;
            foreach (var key in TypeKeys(element))
                if (d.ElementStyles.TryGetValue(key, out var s) && s.BgColor is not null) fill = s.BgColor;

            entries.Add(new GraphLegendEntry(label, fill, null, element.Shape, element.Kind, element.External));
        }

        foreach (var (_, style) in d.Tags)
            if (style.LegendText is { Length: > 0 } text && seen.Add(text))
                entries.Add(new GraphLegendEntry(text, style.BgColor, style.BorderColor, null));

        foreach (var (_, style) in d.RelTags)
            if (style.LegendText is { Length: > 0 } text && seen.Add(text))
                entries.Add(new GraphLegendEntry(text, style.LineColor, style.LineColor, null));

        return entries;
    }

    /// <summary>
    /// The keys an <c>UpdateElementStyle</c> may have used for this element, in C4-PlantUML's
    /// vocabulary: <c>person</c>, <c>external_person</c>, <c>system</c>, <c>system_db</c>, …
    /// </summary>
    internal static IEnumerable<string> TypeKeys(C4Element e)
    {
        string kind = e.Kind switch
        {
            C4ElementKind.Person         => "person",
            C4ElementKind.System         => "system",
            C4ElementKind.Container      => "container",
            C4ElementKind.Component      => "component",
            C4ElementKind.DeploymentNode => "node",
            _                            => "element",
        };
        string suffix = e.Shape switch
        {
            C4ElementShape.Database => "_db",
            C4ElementShape.Queue    => "_queue",
            _                       => "",
        };

        yield return kind;
        if (suffix.Length > 0) yield return kind + suffix;
        if (e.External)
        {
            yield return "external_" + kind;
            if (suffix.Length > 0) yield return "external_" + kind + suffix;
        }
    }
}
