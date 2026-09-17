namespace Nexaflow.Markdown.Mermaid.Mindmap;

/// <summary>What a piece of a <c>mindmap</c> diagram is.</summary>
public static class MindmapKinds
{
    /// <summary>A node: its id, its title in brackets, or both. Its indentation says whose child it is.</summary>
    public const string Node = "mindmap-node";

    /// <summary>An <c>::icon(name)</c> line, for the node above it.</summary>
    public const string Icon = "mindmap-icon";

    /// <summary>A <c>:::class</c> line, for the node above it.</summary>
    public const string Class = "mindmap-class";
}

/// <summary>What a piece of a <c>mindmap</c> diagram is <em>to</em> the piece holding it.</summary>
public static class MindmapRoles
{
    public const string Id = "mindmap-id";
    public const string Title = "mindmap-title";
    public const string Icon = "mindmap-icon-name";
    public const string Class = "mindmap-class-name";
}
