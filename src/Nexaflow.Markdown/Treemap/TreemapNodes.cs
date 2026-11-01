using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// An item as its stages leave it: the item's line and everything written under it, gathered (<see cref="TreemapKinds.Node"/>), and
/// what it comes to — the value written for it, how much it and everything under it are worth together, whether it is written as a
/// section, and what it is drawn with. None of that is in the characters, which it prints as all the same.
/// </summary>
internal sealed class TreemapNode : ContentNode
{
    internal TreemapNode(ContentNode written) : this(written, written as TreemapNode) { }

    private TreemapNode(ContentNode shape, TreemapNode? said) : base(shape)
    {
        if (said is null) return;

        this.Value = said.Value;
        this.Total = said.Total;
        this.Section = said.Section;
        this.Style = said.Style;
    }

    /// <summary>The value written for it — a leaf's — or null where none is written, or what is written is no number.</summary>
    public double? Value { get; init; }

    /// <summary>What it and everything under it are worth together: its own value and the totals of the items under it.</summary>
    public double Total { get; init; }

    /// <summary>Whether it is written as a section — a label with no value after it — rather than a leaf.</summary>
    public bool Section { get; init; }

    /// <summary>What it is drawn with: the <c>default</c> class, then the class it takes.</summary>
    public MermaidStyle Style { get; init; } = MermaidStyle.None;

    protected override ContentNode Reshaped(ContentNode shape) => new TreemapNode(shape, this);
}
