namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>The pieces a kanban board's layout is made of — its layers, and what is in them.</summary>
public static class KanbanPiece
{
    /// <summary>A column, standing for its line, with its title at its top and its cards in it.</summary>
    public const string Column = "Column";

    /// <summary>A card, standing for its line: its title, its ticket, its assignee and its priority's stripe.</summary>
    public const string Card = "Card";

    public const string Title = "Title";
    public const string Ticket = "Ticket";
    public const string Assigned = "Assigned";
    public const string Priority = "Priority";
}
