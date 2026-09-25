using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>A node indented as far as the first one, which makes it a column: the cards written under it are in its lane.</summary>
internal sealed class KanbanColumnNode : ContentNode
{
    internal KanbanColumnNode(ContentNode written, string? label) : base(written) => this.Label = label;

    /// <summary>A title its metadata's <c>label</c> gives it instead of the one written.</summary>
    public string? Label { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new KanbanColumnNode(shape, this.Label);
}

/// <summary>A node indented further than the first, which makes it a card in the column above it: what its metadata says.</summary>
internal sealed class KanbanCardNode : ContentNode
{
    internal KanbanCardNode(ContentNode written) : base(written) { }

    private KanbanCardNode(ContentNode shape, KanbanCardNode said) : base(shape)
    {
        this.Label = said.Label;
        this.Ticket = said.Ticket;
        this.Assigned = said.Assigned;
        this.Priority = said.Priority;
        this.Linked = said.Linked;
    }

    /// <summary>A title its metadata's <c>label</c> gives it instead of the one written.</summary>
    public string? Label { get; init; }

    public string? Ticket { get; init; }

    public string? Assigned { get; init; }

    public string? Priority { get; init; }

    /// <summary>Whether its ticket links anywhere: it has one, and the front matter's <c>ticketBaseUrl</c> says where tickets are.</summary>
    public bool Linked { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new KanbanCardNode(shape, this);
}
