namespace Nexaflow.Markdown.Treemap;

/// <summary>The kinds of what a treemap writes, beyond the shapes every Mermaid block shares.</summary>
public static class TreemapKinds
{
    /// <summary>What an item's line says: its label in quotes, a value where it is a leaf, and the class it takes.</summary>
    public const string Item = "treemap-item";

    /// <summary>An item's label: the quote it opens with, its words, and the same quote closing it.</summary>
    public const string Label = "treemap-label";

    /// <summary>An item gathered with everything written under it — what a section is, with its leaves and sections inside it.</summary>
    public const string Node = "treemap-node";

    /// <summary>A <c>classDef</c> line: the class it names, and the style it is.</summary>
    public const string ClassDef = "treemap-classdef";
}

/// <summary>The roles of the pieces a treemap's lines are read into.</summary>
public static class TreemapRoles
{
    /// <summary>What an item's label says.</summary>
    public const string Label = "treemap-label";

    /// <summary>The number a leaf is worth.</summary>
    public const string Value = "treemap-value";

    /// <summary>The name of the class an item takes, or a <c>classDef</c> names.</summary>
    public const string Class = "treemap-class";
}
