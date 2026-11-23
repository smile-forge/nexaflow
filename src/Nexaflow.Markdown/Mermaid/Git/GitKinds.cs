namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>What a piece of a <c>gitGraph</c> diagram is.</summary>
public static class GitKinds
{
    /// <summary>A <c>commit</c> line, with the options written after it.</summary>
    public const string Commit = "git-commit";

    /// <summary>A <c>merge</c> line: the branch merged in, and the commit's own options.</summary>
    public const string Merge = "git-merge";

    /// <summary>A <c>cherry-pick</c> line: the commit taken, and the options round it.</summary>
    public const string Pick = "git-pick";

    /// <summary>A <c>branch</c> line: the branch made, and where it goes among the lanes.</summary>
    public const string Branch = "git-branch";

    /// <summary>A <c>checkout</c> or <c>switch</c> line: the branch the commits after it go on.</summary>
    public const string Checkout = "git-checkout";

    /// <summary>Which way the graph runs, written after the keyword: <c>gitGraph LR:</c>.</summary>
    public const string Direction = "git-direction";

    /// <summary>A backslash and the character it keeps, in a name or a message in quotes: <c>\"</c>.</summary>
    public const string Escape = "git-escape";

    /// <summary>A line ending a name or a message in quotes runs across.</summary>
    public const string Break = "git-break";
}

/// <summary>What a piece of a <c>gitGraph</c> diagram is <em>to</em> the piece holding it.</summary>
public static class GitRoles
{
    /// <summary>A branch's name — where it is made, and where it is checked out or merged.</summary>
    public const string Name = "git-branch-name";

    /// <summary>Which way the graph runs.</summary>
    public const string Way = "git-way";

    /// <summary>A commit's message, written in quotes after the word — <c>commit "Initial commit"</c> — as older diagrams write one.</summary>
    public const string Message = "git-message";
}
