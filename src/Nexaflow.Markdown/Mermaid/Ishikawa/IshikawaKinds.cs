namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>What a piece of an <c>ishikawa</c> diagram is.</summary>
public static class IshikawaKinds
{
    /// <summary>A line: the event the diagram is about, where it is the first, and otherwise a cause — what it says to the end of its line.</summary>
    public const string Cause = "ishikawa-cause";
}

/// <summary>What a piece of an <c>ishikawa</c> diagram is <em>to</em> the piece holding it.</summary>
public static class IshikawaRoles
{
    /// <summary>What a cause says.</summary>
    public const string Says = "ishikawa-says";
}
