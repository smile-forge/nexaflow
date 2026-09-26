namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>What a piece of a <c>block-beta</c> diagram is.</summary>
public static class BlockKinds
{
    /// <summary>A <c>columns 3</c> or <c>columns auto</c> line, saying how many columns the grid it is in is laid out in.</summary>
    public const string Columns = "block-columns";

    /// <summary>A <c>block</c> or <c>block:ID</c> line, opening a composite — a grid of its own, in a cell of the grid it is in.</summary>
    public const string Opens = "block-opens";

    /// <summary>An <c>end</c> line, ending the composite it is in.</summary>
    public const string Ends = "block-ends";

    /// <summary>A line of blocks, spaces, block arrows and links — as many of each as are written on it.</summary>
    public const string Items = "block-items";

    /// <summary>One block: its id, its label in the brackets that say its shape, and the columns it takes.</summary>
    public const string Item = "block-item";

    /// <summary>A <c>space</c> or <c>space:3</c>: cells left empty.</summary>
    public const string Space = "block-space";

    /// <summary>A block arrow — <c>id&lt;["Label"]&gt;(right, down)</c> — an arrow drawn as a block of its own.</summary>
    public const string Arrow = "block-arrow";

    /// <summary>A link between the blocks written either side of it, with its label where it has one.</summary>
    public const string Link = "block-link";

    /// <summary>A <c>classDef</c> line: the class it declares, and the style that class is.</summary>
    public const string ClassDef = "block-class-def";

    /// <summary>A <c>class</c> line: the blocks it names, and the class they take.</summary>
    public const string Class = "block-class";

    /// <summary>A <c>style</c> line: the blocks it names, and the style they take.</summary>
    public const string Style = "block-style";
}

/// <summary>What a piece of a <c>block-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class BlockRoles
{
    /// <summary>What a block is called — which is what a link, a <c>class</c> and a <c>style</c> name it by.</summary>
    public const string Id = "block-id";

    /// <summary>What is written on a block, or on a link.</summary>
    public const string Label = "block-label";

    /// <summary>How many columns a block takes: the <c>:2</c> after it.</summary>
    public const string Width = "block-width";

    /// <summary>How many columns a grid is laid out in — a number, or <c>auto</c>.</summary>
    public const string Count = "block-count";

    /// <summary>The token that says what a link draws: how thick it is, whether it is dotted, and the heads at its ends.</summary>
    public const string Arrow = "block-drawn";

    /// <summary>One direction a block arrow points.</summary>
    public const string Direction = "block-direction";

    /// <summary>The class a <c>classDef</c> declares, or that a <c>class</c> line applies.</summary>
    public const string Class = "block-class-name";
}
