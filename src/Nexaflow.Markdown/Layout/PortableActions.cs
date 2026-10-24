namespace Nexaflow.Markdown.Layout;

/// <summary>What a press on a piece means — a verb, what it acts on, and what resting on it says.</summary>
public sealed record PortableIntent(string Verb, string? Target = null, string? Tip = null);

/// <summary>What the few pieces that answer to a gesture answer to.</summary>
public sealed record PortableActions
{
    /// <summary>What one press means.</summary>
    public PortableIntent? Click { get; init; }
}

/// <summary>The verbs a layout resolves itself where its host does not take them first.</summary>
public static class PortableVerbs
{
    public const string Navigate = "navigate";

    public const string Expand = "expand";
}
