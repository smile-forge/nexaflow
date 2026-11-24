namespace Nexaflow.Features.Git.Services;

/// <summary>How a pull ended — <see cref="AuthFailed"/> is the one a stored token can mend.</summary>
public enum GitPullOutcome { Ok, UpToDate, Failed, AuthFailed }

/// <summary>A pull's outcome and the line that reports it.</summary>
public sealed record GitPullResult(GitPullOutcome Outcome, string Message);

/// <summary>
/// The working tree of the folder a Git viewlet is on: what only git there can say about it — its status, its uncommitted
/// diff, its linked worktrees — and every change the viewlet makes to it. A folder of this PC is read and changed in
/// process (<see cref="LocalGitWorkingCopy"/>); a folder on a location by the host's own <c>git</c>
/// (<see cref="HostGitWorkingCopy"/>), so what it says is the host's view of the files and what it changes keeps the names
/// and permissions the host gives it. History — commits, tags, blame — is objects, which read the same from anywhere, and
/// stays with <see cref="GitService"/>.
/// </summary>
public interface IGitWorkingCopy
{
    /// <summary>Branch, upstream tracking, staged/modified/untracked files and the tip commit. Throws
    /// <see cref="LibGit2Sharp.RepositoryNotFoundException"/> when the folder is not a repository git can open.</summary>
    Task<GitStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>Uncommitted changes as a unified diff — the index against HEAD when <paramref name="staged"/>, else the
    /// working tree against HEAD — optionally of one path; empty when there are none.</summary>
    Task<string> GetDiffAsync(string? path, bool staged, CancellationToken ct);

    /// <summary>The diff between two revisions — branches, tags or hashes — optionally of one path, rendered per
    /// <paramref name="format"/>. Throws <see cref="ArgumentException"/> when either does not resolve.</summary>
    Task<string> GetDiffBetweenAsync(string from, string to, string? path, GitDiffFormat format, CancellationToken ct);

    /// <summary>The folder's linked-worktree state, or null when it is not a linked worktree; a remnant whose repository
    /// will not open is reported broken.</summary>
    Task<GitWorktreeInfo?> GetWorktreeAsync(CancellationToken ct);

    /// <summary>The repository's linked worktrees, each with the state that decides whether removing it loses work.</summary>
    Task<IReadOnlyList<GitWorktreeListItem>> GetWorktreesAsync(CancellationToken ct);

    /// <summary>Checks out a local branch. Throws when git refuses.</summary>
    Task SwitchBranchAsync(string branch, CancellationToken ct);

    /// <summary>Fast-forward-only pull of the current branch; never throws for a failed pull, which is an outcome.</summary>
    Task<GitPullResult> PullAsync(CancellationToken ct);

    /// <summary>Removes the linked worktree — its folder, its registration and the branch it owned — failing safe: the
    /// folder is moved aside before git is touched, so a failure leaves it as it was. Never throws for an expected failure.</summary>
    Task<GitWorktreeRemovalResult> RemoveWorktreeAsync(CancellationToken ct);

    /// <summary>The repository line the assistant reads, or null for a repository that cannot say. Answered without
    /// waiting on a host: a working copy on a location describes what it last read.</summary>
    string? DescribeForAssistant();
}
