namespace Nexaflow.Markdown.Code;

/// <summary>What a piece of code is — to the tree it is read into, and to the layout it is set in.</summary>
public static class CodeKinds
{
    /// <summary>A whole stretch of code.</summary>
    public const string Code = "code";

    /// <summary>Where one line of it ends and the next begins: a line feed, and the return before it where there is one.</summary>
    public const string LineEnd = "line-end";
}
