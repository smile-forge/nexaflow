using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Kanban.Stages;

/// <summary>
/// Says which nodes are columns: those indented as far as the first node is. A node indented further is a card in the column
/// above it; one indented less than the first is written left of every column, which Mermaid refuses, so it says so.
/// </summary>
public sealed class ResolveColumns : IAstStage
{
    public string Name => "kanban:columns";

    public ContentNode Run(ContentNode tree)
    {
        var first = tree.SelfAndDescendants().FirstOrDefault(line => line.Kind == MermaidKinds.Line && line.Stated()?.Kind == KanbanKinds.Node);
        if (first is null) return tree;

        var column = first.Indent();

        return AstRewrite.Each(tree, line =>
        {
            if (line.Kind != MermaidKinds.Line || line.Stated() is not { Kind: KanbanKinds.Node } node) return line;

            var indent = line.Indent();
            var said = indent == column ? node.Saying(KanbanKinds.Fact, KanbanRoles.Column, "column")
                     : indent < column ? node.Saying("A card is indented further than its column, and a column as far as the first one — nothing is written left of the first column.")
                     : node;

            return ReferenceEquals(said, node) ? line : line.With([.. line.Children.Select(child => ReferenceEquals(child, node) ? said : child)]);
        });
    }
}
