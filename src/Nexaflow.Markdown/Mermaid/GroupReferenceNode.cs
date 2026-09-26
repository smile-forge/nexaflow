using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A name the stages worked out names a group rather than anything drawn in one — an ER diagram's subgraph, joined by its id.
/// Which group is <see cref="Group"/>: the one opened by that line of the block's group-opening lines, counting from nought in
/// the order they are written, which is the order a builder meets them walking down the tree (<see cref="MermaidNesting.Nest"/>).
/// It prints as the name written.
/// </summary>
internal sealed class GroupReferenceNode : ContentNode
{
    internal GroupReferenceNode(ContentNode written, int group) : base(written) => this.Group = group;

    public int Group { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new GroupReferenceNode(shape, this.Group);
}
