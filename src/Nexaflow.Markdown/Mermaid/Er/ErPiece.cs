namespace Nexaflow.Markdown.Mermaid.Er;

/// <summary>The pieces an ER diagram's layout is made of — its layers, and what is in them.</summary>
public static class ErPiece
{
    /// <summary>The diagram itself: the entities, and the subgraphs they are boxed into.</summary>
    public const string Entities = "Entities";

    /// <summary>One entity, standing for everything written for it.</summary>
    public const string Entity = "Entity";

    /// <summary>One attribute of one: what it holds, what it is called, the keys it is, and what it is for.</summary>
    public const string Attribute = "Attribute";

    /// <summary>A subgraph: the box, and the entities it holds drawn inside its piece.</summary>
    public const string Group = "Group";

    /// <summary>A subgraph's own box and its name, behind the entities it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>The relationships, drawn over the diagram.</summary>
    public const string Relations = "Relations";

    /// <inheritdoc cref="Relations"/>
    public const string Relation = "Relation";

    /// <summary>What a relationship is called, written over the middle of its line.</summary>
    public const string Label = "Label";
}
