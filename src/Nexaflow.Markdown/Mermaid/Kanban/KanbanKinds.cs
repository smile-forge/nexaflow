namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>What a piece of a <c>kanban</c> diagram is.</summary>
public static class KanbanKinds
{
    /// <summary>A column or a card — which, its indentation says: its id, its title in brackets, and its metadata.</summary>
    public const string Node = "kanban-node";

    /// <summary>A card's metadata: <c>@{ assigned: knsv, priority: 'High' }</c>.</summary>
    public const string Data = "kanban-data";

    /// <summary>An <c>::icon(name)</c> line, for the node above it.</summary>
    public const string Icon = "kanban-icon";

    /// <summary>A <c>:::class</c> line, for the node above it.</summary>
    public const string Class = "kanban-class";
}

/// <summary>What a piece of a <c>kanban</c> diagram is <em>to</em> the piece holding it.</summary>
public static class KanbanRoles
{
    public const string Id = "kanban-id";
    public const string Title = "kanban-title";
    public const string Icon = "kanban-icon-name";
    public const string Class = "kanban-class-name";

    /// <summary>Metadata written so it cannot be read.</summary>
    public const string Data = "kanban-data-text";
}
