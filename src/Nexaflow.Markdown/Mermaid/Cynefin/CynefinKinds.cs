namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>What a piece of a <c>cynefin-beta</c> diagram is.</summary>
public static class CynefinKinds
{
    /// <summary>A domain's line: the one word opening it, which every item written under it sits in.</summary>
    public const string Domain = "cynefin-domain";

    /// <summary>An item's line: what it says, in quotes or bare.</summary>
    public const string Item = "cynefin-item";

    /// <summary>A transition's line: <c>complex --&gt; complicated : "Pattern found"</c>.</summary>
    public const string Move = "cynefin-move";

    /// <summary>Text, in quotes or bare to where it ends — what an item says, what a transition is labelled.</summary>
    public const string Text = "cynefin-text";

    /// <summary>A backslash and the character it keeps, inside a string: <c>\"</c>, <c>\\</c>, <c>\t</c>.</summary>
    public const string Escape = "cynefin-escape";

    /// <summary>A line ending inside a string, which it may run across.</summary>
    public const string Break = "cynefin-break";
}

/// <summary>What a piece of a <c>cynefin-beta</c> diagram is <em>to</em> the piece holding it.</summary>
public static class CynefinRoles
{
    /// <summary>What an item says.</summary>
    public const string Says = "cynefin-says";

    /// <summary>What a transition says.</summary>
    public const string Label = "cynefin-label";
}
