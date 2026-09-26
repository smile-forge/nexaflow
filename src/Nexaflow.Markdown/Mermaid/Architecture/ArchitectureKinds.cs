namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>What a piece of an <c>architecture-beta</c> diagram is.</summary>
public static class ArchitectureKinds
{
    /// <summary>A <c>group</c> line: a box holding the services put in it.</summary>
    public const string Group = "architecture-group";

    /// <summary>A <c>service</c> line: something the architecture is made of.</summary>
    public const string Service = "architecture-service";

    /// <summary>A <c>junction</c> line: a place edges meet, drawn as a dot rather than a box.</summary>
    public const string Junction = "architecture-junction";

    /// <summary>An edge line: the two ends it joins, the side of each it leaves by, and what it draws there.</summary>
    public const string Edge = "architecture-edge";

    /// <summary>An <c>align row</c> or <c>align column</c> line: the services that share a row or a column.</summary>
    public const string Align = "architecture-align";
}

/// <summary>What a piece of an <c>architecture-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class ArchitectureRoles
{
    /// <summary>What something is called — which is what an edge, an <c>in</c> and an <c>align</c> name it by.</summary>
    public const string Id = "architecture-id";

    /// <summary>The group something is put in.</summary>
    public const string In = "architecture-in";

    /// <summary>The icon drawn for it, in the brackets after its id.</summary>
    public const string Icon = "architecture-icon";

    /// <summary>What is written under it, or on an edge.</summary>
    public const string Title = "architecture-title";

    /// <summary>The side of a service an edge leaves by, or arrives at: L, R, T or B.</summary>
    public const string Side = "architecture-side";

    /// <summary>The <c>{group}</c> saying an edge goes to the group its service is in rather than to the service.</summary>
    public const string Group = "architecture-group-modifier";

    /// <summary>An arrowhead at one end of an edge.</summary>
    public const string Head = "architecture-head";

    /// <summary>Whether an <c>align</c> line shares a row or a column.</summary>
    public const string Axis = "architecture-axis";
}
