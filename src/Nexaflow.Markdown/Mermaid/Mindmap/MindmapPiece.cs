namespace Nexaflow.Markdown.Mermaid.Mindmap;

/// <summary>The pieces a mindmap's layout is made of — its layers, and what is in them.</summary>
public static class MindmapPiece
{
    /// <summary>The lines from each node to its children, one standing for the child it reaches.</summary>
    public const string Branches = "Branches";
    public const string Branch = "Branch";

    /// <summary>The nodes, each standing for its line, with what it says in it.</summary>
    public const string Nodes = "Nodes";
    public const string Node = "Node";
    public const string Title = "Title";
}
