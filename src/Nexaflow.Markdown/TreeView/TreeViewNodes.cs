using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.TreeView;

/// <summary>
/// A node as its stages leave it: its line and everything written under it, gathered (<see cref="TreeViewKinds.Node"/>), and what it
/// is — a folder or a file, and whether it is picked out to be seen. None of that is more than its characters say, which it prints as
/// all the same.
/// </summary>
internal sealed class TreeViewNode : ContentNode
{
    internal TreeViewNode(ContentNode written) : this(written, written as TreeViewNode) { }

    private TreeViewNode(ContentNode shape, TreeViewNode? said) : base(shape)
    {
        if (said is null) return;

        this.Folder = said.Folder;
        this.Highlighted = said.Highlighted;
    }

    /// <summary>Whether it is a folder — its label written with a slash after it — rather than a file.</summary>
    public bool Folder { get; init; }

    /// <summary>Whether it takes the <c>highlight</c> class, which draws its row picked out.</summary>
    public bool Highlighted { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new TreeViewNode(shape, this);
}
