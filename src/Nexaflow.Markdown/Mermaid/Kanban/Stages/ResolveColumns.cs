using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Kanban.Stages;

/// <summary>
/// Says which nodes are columns and which are cards, and what each one's metadata says. A node indented as far as the first
/// node is a column; one indented further is a card in the column above it; one indented less than the first is written left
/// of every column, which Mermaid refuses, so it says so.
/// </summary>
/// <param name="config">What the front matter asks for, which says whether a card's ticket links anywhere.</param>
public sealed class ResolveColumns(KanbanConfig config) : IAstStage
{
    public string Name => "kanban:columns";

    public ContentNode Run(ContentNode tree)
    {
        var first = tree.SelfAndDescendants().FirstOrDefault(line => line.Kind == MermaidKinds.Line && line.Stated()?.Kind == KanbanKinds.Node);
        if (first is null) return new KanbanBlockNode(tree, config);

        var column = first.Indent();

        tree = AstRewrite.Each(tree, line =>
        {
            if (line.Kind != MermaidKinds.Line || line.Stated() is not { Kind: KanbanKinds.Node } node) return line;

            var indent = line.Indent();
            var said = indent == column ? new KanbanColumnNode(node, Metadata(node).Label)
                     : indent < column ? node.Saying("A card is indented further than its column, and a column as far as the first one — nothing is written left of the first column.")
                     : Card(node);

            return line.With([.. line.Children.Select(child => ReferenceEquals(child, node) ? said : child)]);
        });

        return new KanbanBlockNode(tree, config);
    }

    private KanbanCardNode Card(ContentNode node)
    {
        var (label, ticket, assigned, priority) = Metadata(node);
        return new KanbanCardNode(node)
        {
            Label = label,
            Ticket = ticket,
            Assigned = assigned,
            Priority = priority,
            Linked = ticket is not null && config.TicketBaseUrl is not null,
        };
    }

    /// <summary>What a node's metadata says that a board draws, each the last written of its key and without the quotes round it.</summary>
    private static (string? Label, string? Ticket, string? Assigned, string? Priority) Metadata(ContentNode node)
    {
        string? label = null, ticket = null, assigned = null, priority = null;

        foreach (var property in node.Inner(KanbanKinds.Data)?.SelfAndDescendants() ?? [])
        {
            if (property.Kind != MermaidKinds.Property
                || property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is not { } key
                || property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting)?.Text is not { } written
                || written.AsSpan().Trim().IsEmpty)
                continue;

            var name = key.Text.AsSpan().Trim();
            if (name.Equals("label", StringComparison.OrdinalIgnoreCase)) label = Value(written);
            else if (name.Equals("ticket", StringComparison.OrdinalIgnoreCase)) ticket = Value(written);
            else if (name.Equals("assigned", StringComparison.OrdinalIgnoreCase)) assigned = Value(written);
            else if (name.Equals("priority", StringComparison.OrdinalIgnoreCase)) priority = Value(written);
        }

        return (label, ticket, assigned, priority);
    }

    /// <summary>What a value says without the quotes round it — the very characters written, wherever nothing comes off them.</summary>
    private static string Value(string written)
    {
        var value = written.AsSpan().Trim();
        if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0]) value = value[1..^1];
        return value.Length == written.Length ? written : value.ToString();
    }
}
