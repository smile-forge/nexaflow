namespace Nexaflow.Markdown.TreeView;

/// <summary>
/// The pieces a tree view's layout is made of: a row for each node, top to bottom as they are written, and apart from them the lines
/// joining each folder to what is in it.
/// </summary>
public static class TreeViewPiece
{
    /// <summary>The lines joining each folder to the rows of what is in it. They stand for nothing written, and are never picked out.</summary>
    public const string Lines = "Lines";

    /// <summary>The rows, top to bottom. They stand for nothing written.</summary>
    public const string Rows = "Rows";

    /// <summary>
    /// A node's row, across the whole width of the tree: its icon, its label and what is said beside it. It stands for the node with
    /// everything under it, so choosing it chooses all of that, though it holds only its own row.
    /// </summary>
    public const string Row = "Row";

    /// <summary>The icon a node is drawn with — pressed as the node, never a place for the caret.</summary>
    public const string Icon = "Icon";

    /// <summary>A node's label, a folder's in bold — typed into where it is drawn.</summary>
    public const string Label = "Label";

    /// <summary>What is said beside a node, in italics — typed into where it is drawn.</summary>
    public const string Description = "Description";
}
