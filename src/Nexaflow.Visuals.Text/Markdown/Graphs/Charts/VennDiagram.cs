namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>An item (member) that sits inside a Venn region — a <c>text</c> node.</summary>
public sealed class VennItem
{
    public required string Id { get; init; }
    public string Label { get; set; } = string.Empty;
    public string? TextColor { get; set; }
    public string Display => string.IsNullOrEmpty(Label) ? Id : Label;
}

/// <summary>One circle of a Venn diagram (a <c>set</c>).  <see cref="Size"/> is an area weight (default
/// applied by the renderer), not a displayed count.</summary>
public sealed class VennSet
{
    public required string Id { get; init; }
    public string Label { get; set; } = string.Empty;
    public double? Size { get; set; }
    public List<VennItem> Items { get; } = [];

    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public string? TextColor { get; set; }
    public double? FillOpacity { get; set; }

    public string Display => string.IsNullOrEmpty(Label) ? Id : Label;
}

/// <summary>An intersection region of two or more sets (a <c>union</c>), labelled at the overlap.</summary>
public sealed class VennUnion
{
    public required List<string> SetIds { get; init; }
    public string Label { get; set; } = string.Empty;
    public double? Size { get; set; }
    public List<VennItem> Items { get; } = [];

    public string? Fill { get; set; }
    public string? TextColor { get; set; }

    public string Display => string.IsNullOrEmpty(Label) ? string.Join(" ∩ ", SetIds) : Label;
}

/// <summary>
/// Data model for a Mermaid <c>venn-beta</c> diagram: a set of circles and the labelled intersection
/// (<c>union</c>) regions between them, each optionally holding <c>text</c> items.  Comma is the only
/// intersection operator.  Front-matter <c>config.venn</c> options live on <see cref="Config"/>.
/// </summary>
public sealed class VennDiagram
{
    public string Title { get; set; } = string.Empty;
    public List<VennSet> Sets { get; } = [];
    public List<VennUnion> Unions { get; } = [];
    public VennConfig Config { get; set; } = new();

    public VennSet? FindSet(string id) =>
        Sets.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
}
