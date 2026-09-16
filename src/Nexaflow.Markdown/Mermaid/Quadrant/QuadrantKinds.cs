namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>What a piece of a <c>quadrantChart</c> diagram is.</summary>
public static class QuadrantKinds
{
    /// <summary>An <c>x-axis</c> or <c>y-axis</c> line: what its low end and its high end say.</summary>
    public const string Axis = "quadrant-axis";

    /// <summary>A <c>quadrant-1</c>…<c>quadrant-4</c> line: what that quadrant says.</summary>
    public const string Region = "quadrant-region";

    /// <summary>One point: its name, its class, where it stands, and its style.</summary>
    public const string Point = "quadrant-point";

    /// <summary>Where a point stands: <c>[0.3, 0.6]</c>.</summary>
    public const string Position = "quadrant-position";

    /// <summary>A <c>classDef</c> line: a class's name and its style.</summary>
    public const string Class = "quadrant-class";

    /// <summary>Text, in quotes or bare to where it ends — an axis end, a quadrant's caption, a point's name.</summary>
    public const string Text = "quadrant-text";
}

/// <summary>What a piece of a <c>quadrantChart</c> diagram is <em>to</em> the piece holding it.</summary>
public static class QuadrantRoles
{
    public const string Low = "quadrant-low";
    public const string High = "quadrant-high";

    /// <summary>What a quadrant says.</summary>
    public const string Caption = "quadrant-caption";

    /// <summary>A point's name.</summary>
    public const string Name = "quadrant-name";

    /// <summary>A class's name — where a <c>classDef</c> declares it, and where a point takes it after <c>:::</c>.</summary>
    public const string Class = "quadrant-class-name";

    public const string X = "quadrant-x";
    public const string Y = "quadrant-y";
}
