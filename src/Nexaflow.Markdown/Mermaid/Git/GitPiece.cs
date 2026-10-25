namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>The pieces a git graph's layout is made of — its layers, and what is in them.</summary>
public static class GitPiece
{
    /// <summary>The branches' labels, and one — standing for the line making that branch.</summary>
    public const string Branches = "Branches";
    public const string Branch = "Branch";

    /// <summary>The lines the lanes run along, and one — standing for the line making its branch.</summary>
    public const string Lanes = "Lanes";
    public const string Lane = "Lane";

    /// <summary>What each commit follows, and one such line — standing for the commit that follows.</summary>
    public const string Follows = "Follows";
    public const string Follow = "Follow";

    /// <summary>The commits, and one — standing for the line it is written on.</summary>
    public const string Commits = "Commits";
    public const string Commit = "Commit";

    /// <summary>What a commit says about itself: the tag hung on it, and the id written for it.</summary>
    public const string Tags = "Tags";
    public const string Tag = "Tag";
    public const string Id = "Id";

    /// <summary>A branch's name, written in its label.</summary>
    public const string Name = "Name";
}
