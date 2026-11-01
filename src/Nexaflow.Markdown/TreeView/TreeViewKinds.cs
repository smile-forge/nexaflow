namespace Nexaflow.Markdown.TreeView;

/// <summary>The kinds of what a tree view writes, beyond the shapes every Mermaid block shares.</summary>
public static class TreeViewKinds
{
    /// <summary>What a node's line says: the box drawing in front of it where it has some, its label, and what is noted after it.</summary>
    public const string Item = "treeview-item";

    /// <summary>A node's label: its words, bare or in quotes, and the slash after them that makes it a folder.</summary>
    public const string Label = "treeview-label";

    /// <summary>The box drawing in front of a node — <c>│   ├── </c> — which says how deep it stands in a tree drawn with it.</summary>
    public const string Prefix = "treeview-prefix";

    /// <summary>A line drawn with nothing but the bars of a box drawing — <c>│</c> — which stands for no node.</summary>
    public const string Decoration = "treeview-decoration";

    /// <summary>The class a node takes: <c>:::</c> and its name.</summary>
    public const string Class = "treeview-class";

    /// <summary>What is said beside a node: <c>##</c> and the words after it.</summary>
    public const string Description = "treeview-description";

    /// <summary>A node gathered with everything written under it — a folder, with what is in it.</summary>
    public const string Node = "treeview-node";
}

/// <summary>The roles of the pieces a tree view's lines are read into.</summary>
public static class TreeViewRoles
{
    /// <summary>What a node's label says.</summary>
    public const string Label = "treeview-label";

    /// <summary>The slash after a label that makes the node a folder.</summary>
    public const string Folder = "treeview-folder";

    /// <summary>The name of the class a node takes.</summary>
    public const string Class = "treeview-class";

    /// <summary>The name of the icon a node takes.</summary>
    public const string Icon = "treeview-icon";

    /// <summary>What is said beside a node.</summary>
    public const string Description = "treeview-description";
}
