using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>
/// A <c>gitGraph</c> block as its stage leaves it: what its front matter asks for, and the lane of the branch everything
/// starts on — which no line makes, so nothing else can say where it goes.
/// </summary>
internal sealed class GitBlockNode : ContentNode
{
    internal GitBlockNode(ContentNode written, GitConfig config, int main) : base(written) => (this.Config, this.Main) = (config, main);

    public GitConfig Config { get; }

    /// <summary>The lane of the branch everything starts on.</summary>
    public int Main { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new GitBlockNode(shape, this.Config, this.Main);
}

/// <summary>
/// The line making a branch, the first time it is made: the lane the branch takes, which depends on how every branch in the
/// block asks to be ordered.
/// </summary>
internal sealed class GitBranchNode : ContentNode
{
    internal GitBranchNode(ContentNode written, int lane) : base(written) => this.Lane = lane;

    public int Lane { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new GitBranchNode(shape, this.Lane);
}

/// <summary>
/// A commit, a merge or a cherry-pick, as the history above it places it: the lane of the branch it is made on, where it
/// stands along the history, and the commits it follows — each by where it comes among the commits written.
/// </summary>
internal sealed class GitCommitNode : ContentNode
{
    internal GitCommitNode(ContentNode written, int lane, int position, IReadOnlyList<int> follows, int takes) : base(written) =>
        (this.Lane, this.Position, this.Follows, this.Takes) = (lane, position, follows, takes);

    public int Lane { get; }

    public int Position { get; }

    /// <summary>What it follows: the last commit on its branch where there is one, then the branch a merge brings in or the commit a cherry-pick takes.</summary>
    public IReadOnlyList<int> Follows { get; }

    /// <summary>The commit a cherry-pick takes, or -1 where it takes none there is.</summary>
    public int Takes { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new GitCommitNode(shape, this.Lane, this.Position, this.Follows, this.Takes);
}
