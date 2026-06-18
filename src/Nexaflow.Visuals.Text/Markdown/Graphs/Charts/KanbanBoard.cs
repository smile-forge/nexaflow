namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Task urgency from a kanban card's <c>@{ priority: … }</c> metadata.</summary>
public enum KanbanPriority { None, VeryLow, Low, High, VeryHigh }

/// <summary>One card in a kanban column, with its optional <c>@{ … }</c> metadata.</summary>
public sealed class KanbanItem
{
    public required string Id   { get; init; }
    public required string Text { get; init; }
    public string? Assigned     { get; set; }
    public string? Ticket       { get; set; }
    public KanbanPriority Priority { get; set; } = KanbanPriority.None;
}

/// <summary>A kanban column (workflow stage) holding an ordered list of cards.</summary>
public sealed class KanbanColumn
{
    public required string Id    { get; init; }
    public required string Title { get; init; }
    public List<KanbanItem> Items { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>kanban</c> board: an ordered list of columns, each holding an
/// ordered list of cards.  Hierarchy comes from source indentation — columns sit at the
/// shallowest indent, cards are indented under their column.  The renderer lays the columns
/// out left-to-right, each a header over a vertical stack of cards.
/// </summary>
public sealed class KanbanBoard
{
    public string Title { get; set; } = string.Empty;
    public List<KanbanColumn> Columns { get; } = [];
}
