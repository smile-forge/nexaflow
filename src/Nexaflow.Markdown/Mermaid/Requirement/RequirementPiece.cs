namespace Nexaflow.Markdown.Mermaid.Requirement;

/// <summary>The pieces a requirement diagram's layout is made of — its layers, and what is in them.</summary>
public static class RequirementPiece
{
    /// <summary>The diagram itself: the requirements, and the elements that meet them.</summary>
    public const string Boxes = "Requirements";

    /// <summary>One requirement or element, standing for everything written for it.</summary>
    public const string Box = "Requirement";

    /// <summary>One field of one: what is drawn in front of it, and what it is set to.</summary>
    public const string Fact = "Fact";

    /// <summary>The relations, drawn over the diagram.</summary>
    public const string Relations = "Relations";

    /// <inheritdoc cref="Relations"/>
    public const string Relation = "Relation";

    /// <summary>What holds between two of them, written over the middle of the line joining them.</summary>
    public const string Label = "Label";
}
