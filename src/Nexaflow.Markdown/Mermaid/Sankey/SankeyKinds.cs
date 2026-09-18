namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>What a piece of a <c>sankey-beta</c> diagram is.</summary>
public static class SankeyKinds
{
    /// <summary>One flow: where it comes from, where it goes, and what it is worth.</summary>
    public const string Flow = "sankey-flow";
}

/// <summary>What a piece of a <c>sankey-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class SankeyRoles
{
    /// <summary>Where a flow comes from.</summary>
    public const string Source = "sankey-source";

    /// <summary>Where it goes.</summary>
    public const string Target = "sankey-target";

    /// <summary>What it is worth.</summary>
    public const string Value = "sankey-value";
}
