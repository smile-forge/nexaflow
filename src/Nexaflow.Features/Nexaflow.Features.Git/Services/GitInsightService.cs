using System.IO;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace Nexaflow.Features.Git.Services;

/// <summary>Everything <c>git_compare</c> answers, gathered in one pass.</summary>
public sealed record GitComparison(
    GitDivergence                Divergence,
    string                       Stat,
    IReadOnlyList<GitCommitInfo> Commits);

/// <summary>A pull request recovered from a merge commit's message — no network involved.</summary>
public sealed record GitMergedPr(int Number, string Title, string Branch);

/// <summary>Release-note material for a range.</summary>
public sealed record GitChangelog(
    IReadOnlyList<GitCommitInfo> Commits,
    IReadOnlyList<GitMergedPr>   PullRequests,
    IReadOnlyList<string>        Contributors,
    string                       Stat);

/// <summary>One branch and everything needed to decide whether deleting it would lose work.</summary>
public sealed record GitBranchAuditRow(
    string  Name,
    bool    IsCurrent,
    string? Upstream,
    bool    UpstreamGone,
    int?    AheadOfMainline,
    bool    MergedIntoMainline,
    string? HeldByWorktree)
{
    /// <summary>Deleting is non-destructive only when the mainline already has everything on this branch.</summary>
    public bool SafeToDelete => MergedIntoMainline && HeldByWorktree is null && !IsCurrent;
}

/// <summary>Somewhere a search term turned up. <paramref name="Kind"/> is branch/tag/stash/reflog/commit.</summary>
public sealed record GitWorkHit(string Kind, string Name, string Detail);

/// <summary>One step in a file's life, with the path it had at that commit (renames change it).</summary>
public sealed record GitFileHistoryEntry(GitCommitInfo Commit, string Path);

/// <summary>
/// Composite queries — the shapes real questions actually take, each replacing four to seven separate calls.
/// Everything here is assembled from <see cref="GitService"/> primitives plus a little repository walking; it
/// holds no state and performs no mutation.
/// </summary>
public sealed class GitInsightService(string folderPath)
{
    private readonly GitService _git = new(folderPath);

    /// <summary>Names a merge commit created by a forge: "Merge pull request #12 from owner/branch".</summary>
    private static readonly Regex MergePr =
        new(@"^Merge pull request #(?<n>\d+) from (?<branch>\S+)", RegexOptions.Compiled | RegexOptions.Multiline);

    // ── git_compare ───────────────────────────────────────────────────────

    /// <summary>
    /// Divergence, changed-file stat and commit list for <paramref name="from"/>..<paramref name="to"/> in one
    /// call — the question the old tool surface could not express at all.
    /// </summary>
    /// <exception cref="ArgumentException">Either endpoint doesn't resolve.</exception>
    public GitComparison Compare(string from, string to, int maxCommits = 100)
    {
        var divergence = _git.GetDivergence(from, to);
        var stat       = _git.GetDiffBetween(from, to);
        var commits    = _git.GetLog(maxCommits, filter: new GitLogFilter(Range: new GitRange(from, to)));
        return new GitComparison(divergence, stat, commits);
    }

    // ── git_changelog ─────────────────────────────────────────────────────

    /// <summary>
    /// Release notes for a range: the non-merge commits, the pull requests whose merge commits fall inside it,
    /// the contributors and a file-stat summary.
    /// </summary>
    /// <remarks>
    /// Pull-request titles come from the <em>merge commits themselves</em> rather than the forge API, so this
    /// works offline and needs no credential. A forge merge commit carries "Merge pull request #N from …" on
    /// its first line and the PR title on its third; where a repository merges differently the list is simply
    /// empty and the commit subjects carry the release notes on their own.
    /// </remarks>
    public GitChangelog Changelog(string from, string to, int maxCommits = 500)
    {
        var range = new GitRange(from, to);

        var commits = _git.GetLog(maxCommits, filter: new GitLogFilter(Range: range, NoMerges: true));
        var merges  = _git.GetLog(maxCommits, filter: new GitLogFilter(Range: range));

        var prs = new List<GitMergedPr>();
        using (var repo = new Repository(folderPath))
        {
            foreach (var c in merges)
            {
                if (repo.Lookup<Commit>(c.Hash) is not { } commit) continue;
                if (MergePr.Match(commit.Message) is not { Success: true } m) continue;

                // Line 1 is the "Merge pull request" header, line 3 (after a blank) is the PR title.
                var lines = commit.Message.Replace("\r\n", "\n").Split('\n');
                var title = lines.Skip(1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? c.Subject;

                prs.Add(new GitMergedPr(int.Parse(m.Groups["n"].Value), title, m.Groups["branch"].Value));
            }
        }

        var contributors = commits.Select(c => c.Author)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        return new GitChangelog(commits, prs, contributors, _git.GetDiffBetween(from, to));
    }

    // ── git_branch_audit ──────────────────────────────────────────────────

    /// <summary>
    /// Every local branch with its upstream, whether that upstream is gone, how far it sits from the mainline,
    /// and whether a worktree holds it. The whole "is this safe to prune" review in one call.
    /// </summary>
    public IReadOnlyList<GitBranchAuditRow> AuditBranches(string? mainline = null)
    {
        using var repo = new Repository(folderPath);

        var target = mainline ?? ResolveMainline(repo);
        var worktreeBranches = WorktreeBranches();

        var rows = new List<GitBranchAuditRow>();
        foreach (var branch in repo.Branches.Where(b => !b.IsRemote))
        {
            int? ahead  = null;
            var  merged = false;

            if (branch.Tip is { } tip && target is not null
                && repo.Lookup<Commit>(target) is { } targetTip)
            {
                var div = repo.ObjectDatabase.CalculateHistoryDivergence(tip, targetTip);
                ahead  = div.AheadBy;
                merged = div.AheadBy is 0;
            }

            // "Gone" = the branch remembers an upstream that no longer exists on the remote.
            var upstreamName = SafeUpstreamName(branch);
            var gone = upstreamName is not null && repo.Branches[upstreamName] is null;

            rows.Add(new GitBranchAuditRow(
                branch.FriendlyName, branch.IsCurrentRepositoryHead, upstreamName, gone,
                ahead, merged,
                worktreeBranches.GetValueOrDefault(branch.FriendlyName)));
        }
        return rows;
    }

    /// <summary>Branch name → worktree path, for the branches a linked worktree currently holds.</summary>
    private Dictionary<string, string> WorktreeBranches()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in _git.GetWorktrees())
            if (!w.IsBroken && w.Branch is { Length: > 0 } b && b != "(detached)")
                map[b] = w.Path;
        return map;
    }

    /// <summary>The mainline to measure against: origin/main, origin/master, then a local main/master.</summary>
    private static string? ResolveMainline(Repository repo)
    {
        foreach (var candidate in new[] { "origin/main", "origin/master", "main", "master" })
            if (repo.Branches[candidate] is not null)
                return candidate;
        return repo.Head.FriendlyName;
    }

    private static string? SafeUpstreamName(Branch branch)
    {
        try { return branch.TrackedBranch?.FriendlyName; }
        catch (LibGit2SharpException) { return null; }   // upstream configured but unparseable
    }

    // ── git_find_work ─────────────────────────────────────────────────────

    /// <summary>
    /// Hunts a term across branches, tags, stashes, the reflog and recent commit subjects — the "where did
    /// that change go" question. Work whose worktree was removed still shows up here, because the branch and
    /// its commits outlive the folder.
    /// </summary>
    public IReadOnlyList<GitWorkHit> FindWork(string query, int maxCommits = 300)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var hits = new List<GitWorkHit>();
        bool Match(string? s) => s is not null && s.Contains(query, StringComparison.OrdinalIgnoreCase);

        using (var repo = new Repository(folderPath))
        {
            foreach (var b in repo.Branches.Where(b => Match(b.FriendlyName)))
                hits.Add(new GitWorkHit(b.IsRemote ? "remote-branch" : "branch", b.FriendlyName,
                                        b.Tip is { } t ? $"{t.Sha[..7]}  {t.MessageShort}" : "(no commits)"));

            foreach (var t in repo.Tags.Where(t => Match(t.FriendlyName)))
                hits.Add(new GitWorkHit("tag", t.FriendlyName, t.Target.Sha[..7]));
        }

        foreach (var s in _git.GetStashes().Where(s => Match(s.Message)))
            hits.Add(new GitWorkHit("stash", $"stash@{{{s.Index}}}", s.Message));

        foreach (var e in _git.GetReflog(200).Where(e => Match(e.Message)))
            hits.Add(new GitWorkHit("reflog", e.Hash, e.Message));

        foreach (var c in _git.GetLog(maxCommits, filter: new GitLogFilter(Grep: query)))
            hits.Add(new GitWorkHit("commit", c.Hash, $"{c.When:yyyy-MM-dd}  {c.Subject}"));

        return hits;
    }

    // ── git_file_history ──────────────────────────────────────────────────

    /// <summary>
    /// Commits touching a path, following renames, so a file's whole life is one call. Pair with
    /// <see cref="GitService.GetFileAt"/> to read the contents at any of the returned commits.
    /// </summary>
    public IReadOnlyList<GitFileHistoryEntry> FileHistory(string path, int count = 50)
    {
        using var repo = new Repository(folderPath);

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var filter = new CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
        };

        return repo.Commits.QueryBy(normalized, filter)
            .Take(count)
            .Select(entry => new GitFileHistoryEntry(
                new GitCommitInfo(entry.Commit.Sha[..7], entry.Commit.Author.Name,
                                  entry.Commit.Author.When, entry.Commit.MessageShort),
                entry.Path))
            .ToList();
    }
}
