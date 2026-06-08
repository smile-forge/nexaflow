namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Flow direction of a git graph (<c>gitGraph LR:</c> / <c>TB:</c> / <c>BT:</c>).</summary>
public enum GitOrientation { LeftRight, TopBottom, BottomTop }

/// <summary>Visual style of a commit node.</summary>
public enum GitCommitType { Normal, Reverse, Highlight }

/// <summary>One commit on a branch, with its lane (branch row) and position (time order) resolved.</summary>
public sealed class GitCommit
{
    public required string Id     { get; init; }
    public required string Branch { get; init; }
    public int Lane     { get; set; }
    public int Position { get; set; }
    public List<string> Parents { get; } = [];   // [0] = primary (same branch); [1] = merge/cherry source
    public string? Tag           { get; set; }
    public GitCommitType Type    { get; set; } = GitCommitType.Normal;
    public bool IsMerge          { get; set; }
    public bool IsCherryPick     { get; set; }
    public bool ShowLabel        { get; set; }    // true when the commit has an explicit id
}

/// <summary>A branch lane in a git graph.</summary>
public sealed class GitBranch
{
    public required string Name { get; init; }
    public int Lane  { get; set; }
    public int? Order { get; set; }   // explicit "order:" overrides creation order for lane assignment
}

/// <summary>
/// Data model for a Mermaid <c>gitGraph</c>.  The parser resolves the commit stream (commits,
/// branches, checkouts, merges, cherry-picks) into commits with explicit parents, lanes and
/// positions, so the renderer only draws nodes and connectors.
/// </summary>
public sealed class GitGraph
{
    public string Title { get; set; } = string.Empty;
    public GitOrientation Orientation { get; set; } = GitOrientation.LeftRight;
    public List<GitBranch> Branches { get; } = [];
    public List<GitCommit> Commits  { get; } = [];

    public GitBranch?  FindBranch(string name) => Branches.FirstOrDefault(b => b.Name == name);
    public GitCommit?  FindCommit(string id)    => Commits.FirstOrDefault(c => c.Id == id);
    public int MaxLane     => Branches.Count > 0 ? Branches.Max(b => b.Lane) : 0;
    public int MaxPosition => Commits.Count > 0 ? Commits.Max(c => c.Position) : 0;
}
