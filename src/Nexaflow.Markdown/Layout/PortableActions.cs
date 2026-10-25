namespace Nexaflow.Markdown.Layout;

/// <summary>What a press on a piece means — a verb, what it acts on, and what resting on it says.</summary>
public sealed record PortableIntent(string Verb, string? Target = null, string? Tip = null);

/// <summary>What the few pieces that answer to a gesture answer to.</summary>
public sealed record PortableActions
{
    /// <summary>What one press means.</summary>
    public PortableIntent? Click { get; init; }

    /// <summary>What choosing it means, for a host following the selection.</summary>
    public PortableIntent? Select { get; init; }
}

/// <summary>The verbs a layout resolves itself where its host does not take them first.</summary>
public static class PortableVerbs
{
    public const string Navigate = "navigate";

    /// <summary>Show what is folded behind the node the target names.</summary>
    public const string Expand = "expand";

    /// <summary>Fold away what is behind the node the target names.</summary>
    public const string Collapse = "collapse";

    /// <summary>The node the target names was chosen.</summary>
    public const string Select = "select";
}