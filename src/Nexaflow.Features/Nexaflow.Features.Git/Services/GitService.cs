using LibGit2Sharp;

namespace Nexaflow.Features.Git.Services;

/// <summary>A single changed path and a short description of how it changed (new/modified/deleted/renamed).</summary>
public sealed record GitFileChange(string Path, string Change);

/// <summary>Rich working-tree status — drives the viewlet display, the AI context line, and <c>git_status</c>.</summary>
public sealed record GitStatus(
    string                       Branch,
    string?                      Upstream,
    int?                         Ahead,
    int?                         Behind,
    IReadOnlyList<GitFileChange> Staged,
    IReadOnlyList<GitFileChange> Modified,
    IReadOnlyList<string>        Untracked,
    string?                      LastCommitHash,
    string?                      LastCommitSubject,
    DateTimeOffset?              LastCommitWhen,
    IReadOnlyList<string>        LocalBranches)
{
    public int StagedCount    => Staged.Count;
    public int ModifiedCount  => Modified.Count;
    public int UntrackedCount => Untracked.Count;
}

public sealed record GitCommitInfo(string Hash, string Author, DateTimeOffset When, string Subject);

public sealed record GitCommitDetail(string Hash, string Author, DateTimeOffset When, string Message, string Diff);

public sealed record GitBranchInfo(string Name, bool IsRemote, bool IsCurrent, string? Upstream, int? Ahead, int? Behind);

public sealed record GitRemoteInfo(string Name, string Url);

/// <summary>
/// Read-only queries over a git repository, wrapping LibGit2Sharp. Opens a fresh <see cref="Repository"/>
/// per call (matching the viewlet's existing usage) so it carries no lifetime/threading state. Mutating
/// operations (stage/commit/push…) are intentionally absent — those are a separate, approval-gated layer.
/// </summary>
public sealed class GitService(string folderPath)
{
    private const FileStatus StagedMask   = FileStatus.NewInIndex | FileStatus.ModifiedInIndex
                                          | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex
                                          | FileStatus.TypeChangeInIndex;
    private const FileStatus ModifiedMask = FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir
                                          | FileStatus.TypeChangeInWorkdir | FileStatus.RenamedInWorkdir;

    /// <summary>Branch, upstream tracking, per-file staged/modified/untracked lists, and the tip commit.</summary>
    public GitStatus GetStatus()
    {
        using var repo = new Repository(folderPath);

        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked     = true,
            RecurseUntrackedDirs = false,
            IncludeIgnored       = false,
            ExcludeSubmodules    = true
        });

        var staged = status.Where(e => (e.State & StagedMask) != 0)
                           .Select(e => new GitFileChange(e.FilePath, Describe(e.State))).ToList();
        var modified = status.Where(e => (e.State & ModifiedMask) != 0 && (e.State & StagedMask) == 0)
                             .Select(e => new GitFileChange(e.FilePath, Describe(e.State))).ToList();
        var untracked = status.Where(e => e.State == FileStatus.NewInWorkdir)
                              .Select(e => e.FilePath).ToList();

        var head = repo.Head;
        var (upstream, ahead, behind) = Tracking(head);
        var tip = head.Tip;

        var branches = repo.Branches.Where(b => !b.IsRemote)
                           .Select(b => b.FriendlyName).OrderBy(n => n).ToList();

        return new GitStatus(head.FriendlyName, upstream, ahead, behind,
                             staged, modified, untracked,
                             tip?.Sha?[..7], tip?.MessageShort, tip?.Author.When, branches);
    }

    /// <summary>Most recent commits on <paramref name="branch"/> (or HEAD), optionally filtered to a path.</summary>
    public IReadOnlyList<GitCommitInfo> GetLog(int count, string? branch = null, string? path = null)
    {
        using var repo = new Repository(folderPath);

        IEnumerable<Commit> commits;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };
            if (!string.IsNullOrWhiteSpace(branch) && repo.Branches[branch] is { } b)
                filter.IncludeReachableFrom = b;
            commits = repo.Commits.QueryBy(path, filter).Select(le => le.Commit);
        }
        else
        {
            commits = !string.IsNullOrWhiteSpace(branch) && repo.Branches[branch] is { } b
                ? b.Commits
                : repo.Commits;
        }

        return commits.Take(count)
                      .Select(c => new GitCommitInfo(c.Sha[..7], c.Author.Name, c.Author.When, c.MessageShort))
                      .ToList();
    }

    /// <summary>Unified diff of uncommitted changes — staged (HEAD↔index) or working tree (HEAD↔workdir),
    /// optionally scoped to one path. Empty string when there's nothing to show.</summary>
    public string GetDiff(string? path = null, bool staged = false)
    {
        using var repo = new Repository(folderPath);
        var oldTree = repo.Head.Tip?.Tree;
        var targets = staged ? DiffTargets.Index : DiffTargets.WorkingDirectory;

        var patch = string.IsNullOrWhiteSpace(path)
            ? repo.Diff.Compare<Patch>(oldTree, targets)
            : repo.Diff.Compare<Patch>(oldTree, targets, [path]);
        return patch.Content;
    }

    /// <summary>Local and remote branches, the current one marked, with upstream + ahead/behind.</summary>
    public IReadOnlyList<GitBranchInfo> GetBranches()
    {
        using var repo = new Repository(folderPath);
        return repo.Branches.Select(b =>
        {
            var (upstream, ahead, behind) = Tracking(b);
            return new GitBranchInfo(b.FriendlyName, b.IsRemote, b.IsCurrentRepositoryHead, upstream, ahead, behind);
        }).ToList();
    }

    /// <summary>A single commit's metadata, full message, and diff against its first parent. Null if not found.</summary>
    public GitCommitDetail? Show(string hash)
    {
        using var repo = new Repository(folderPath);
        if (repo.Lookup<Commit>(hash) is not { } c) return null;
        var parent = c.Parents.FirstOrDefault();
        var patch  = repo.Diff.Compare<Patch>(parent?.Tree, c.Tree);
        return new GitCommitDetail(c.Sha[..7], c.Author.Name, c.Author.When, c.Message.TrimEnd(), patch.Content);
    }

    /// <summary>Configured remotes (name + URL).</summary>
    public IReadOnlyList<GitRemoteInfo> GetRemotes()
    {
        using var repo = new Repository(folderPath);
        return repo.Network.Remotes.Select(r => new GitRemoteInfo(r.Name, r.Url)).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static (string? upstream, int? ahead, int? behind) Tracking(Branch branch)
    {
        try
        {
            if (branch.TrackedBranch is { } tb)
                return branch.IsTracking
                    ? (tb.FriendlyName, branch.TrackingDetails.AheadBy, branch.TrackingDetails.BehindBy)
                    : (tb.FriendlyName, null, null);
        }
        catch (InvalidSpecificationException) { /* no valid upstream configured */ }
        return (null, null, null);
    }

    private static string Describe(FileStatus s) =>
          (s & (FileStatus.NewInIndex | FileStatus.NewInWorkdir))             != 0 ? "new"
        : (s & (FileStatus.DeletedFromIndex | FileStatus.DeletedFromWorkdir)) != 0 ? "deleted"
        : (s & (FileStatus.RenamedInIndex | FileStatus.RenamedInWorkdir))     != 0 ? "renamed"
        : (s & (FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir))   != 0 ? "modified"
        : s.ToString();
}
