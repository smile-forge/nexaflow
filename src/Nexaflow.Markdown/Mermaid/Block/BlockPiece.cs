namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>The pieces a block diagram's layout is made of — its layers, and what is in them.</summary>
public static class BlockPiece
{
    /// <summary>The grid, which is the diagram itself.</summary>
    public const string Blocks = "Blocks";

    /// <summary>One block, standing for what was written for it.</summary>
    public const string Block = "Block";

    /// <summary>A composite: a block holding the grid its own blocks are laid out in, which are the pieces inside it.</summary>
    public const string Composite = "Composite";

    /// <summary>A composite's own box and what is written on it, behind the blocks it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>A block arrow, standing for what was written for it.</summary>
    public const string Arrow = "Arrow";

    /// <summary>The links, drawn over the grid.</summary>
    public const string Links = "Links";

    /// <inheritdoc cref="Links"/>
    public const string Link = "Link";

    /// <summary>What is written on a link, over the middle of it.</summary>
    public const string Label = "Label";
}
