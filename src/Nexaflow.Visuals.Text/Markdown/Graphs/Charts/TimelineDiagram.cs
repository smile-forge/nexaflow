// LEGACY — frozen. Diagrams move off this code onto the shared layout tree one at a time, built from the Mermaid kit
// (docs/mermaid-diagrams.md); this file goes when the last diagram using it has moved. Read it for what a diagram draws —
// never copy from it, add to it, or use it from code on the shared tree (MermaidDiagramRulesTests).
namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Which way a timeline flows: periods left→right (Mermaid's default) or top→down.</summary>
public enum TimelineDirection { LeftToRight, TopDown }

/// <summary>One time period (a <c>2004 : Facebook : Google</c> line) and the events stacked under it.</summary>
public sealed class TimelinePeriod
{
    public required string Title { get; init; }
    /// <summary>Events in declaration order. A <c>&lt;br&gt;</c> in the source is a <c>\n</c> here.</summary>
    public List<string> Events { get; } = [];
}

/// <summary>A named group of periods (a timeline <c>section</c>); periods declared before any
/// <c>section</c> live in one section whose <see cref="Name"/> is empty.</summary>
public sealed class TimelineSection
{
    public required string Name { get; init; }
    public List<TimelinePeriod> Periods { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>timeline</c> — an ordered chain of periods, each with its events,
/// optionally grouped into sections.  Colour follows Mermaid's rule: with sections every period in a
/// section shares that section's colour; without them each period takes the next colour (unless
/// <see cref="TimelineConfig.DisableMulticolor"/>).  Independent of the graph/Sugiyama pipeline.
/// </summary>
public sealed class TimelineDiagram
{
    public string Title { get; set; } = string.Empty;
    public TimelineDirection Direction { get; set; } = TimelineDirection.LeftToRight;
    public List<TimelineSection> Sections { get; } = [];
    public TimelineConfig Config { get; set; } = new();

    /// <summary>True when the source declared at least one named <c>section</c>.</summary>
    public bool HasSections => Sections.Any(s => s.Name.Length > 0);

    public IEnumerable<TimelinePeriod> Periods => Sections.SelectMany(s => s.Periods);
    public int PeriodCount => Sections.Sum(s => s.Periods.Count);
}
