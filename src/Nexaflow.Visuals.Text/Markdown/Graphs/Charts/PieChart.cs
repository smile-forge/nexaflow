namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>One segment in a pie chart.</summary>
public sealed class PieSlice
{
    public required string Label { get; init; }
    public double Value          { get; init; }
}

/// <summary>
/// Data model for a pie chart.  Independent of the graph/Sugiyama pipeline —
/// pie charts require no layout algorithm.
/// </summary>
public sealed class PieChart
{
    public string Title    { get; set; } = string.Empty;
    public bool ShowData   { get; set; }
    public List<PieSlice> Slices { get; } = [];
    public double Total => Slices.Sum(s => s.Value);
}
