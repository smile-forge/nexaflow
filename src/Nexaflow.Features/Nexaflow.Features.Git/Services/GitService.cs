using System.IO;
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

public sealed record GitTagInfo(string Name, string TargetHash, DateTimeOffset? When, string? Subject);

/// <summary>How a diff is rendered — the same choice <c>git diff</c> offers via <c>--stat</c>/<c>--name-only</c>.</summary>
public enum GitDiffFormat
{
    /// <summary>Per-file added/deleted line counts plus a total — the only readable answer at release scale.</summary>
    Stat,

    /// <summary>Changed paths only.</summary>
    NameOnly,

    /// <summary>The full unified diff.</summary>
    Patch
}

/// <summary>
/// Filters narrowing a history query, mirroring the <c>git log</c> options of the same names. Every member is
/// optional; the default instance is "no filtering".
/// </summary>
/// <param name="Range">A revision range — <c>from</c> exclusive to <c>to</c> inclusive, i.e. <c>from..to</c>.</param>
/// <param name="Since">Only commits authored on or after this instant.</param>
/// <param name="Until">Only commits authored on or before this instant.</param>
/// <param name="Author">Substring match (case-insensitive) on the author's name or email.</param>
/// <param name="Grep">Substring match (case-insensitive) on the commit message.</param>
/// <param name="NoMerges">Skip commits with more than one parent.</param>
public sealed record GitLogFilter(
    GitRange?       Range    = null,
    DateTimeOffset? Since    = null,
    DateTimeOffset? Until    = null,
    string?         Author   = null,
    string?         Grep     = null,
    bool            NoMerges = false);

/// <summary>A resolved revision range: everything reachable from <paramref name="To"/> but not from <paramref name="From"/>.</summary>
public sealed record GitRange(string From, string To);

/// <summary>
/// How two refs relate: their common ancestor, and how far each has moved since. Null counts mean the
/// histories are unrelated (no common ancestor at all), which is different from zero.
/// </summary>
public sealed record GitDivergence(string A, string B, string? MergeBaseHash, int? Ahead, int? Behind)
{
    /// <summary>True when everything in <see cref="A"/> is already reachable from <see cref="B"/>.</summary>
    public bool IsAMergedIntoB => Ahead is 0;
}

/// <summary>A branch or tag that can reach some commit.</summary>
public sealed record GitRefMention(string Name, bool IsRemote, bool IsTag);

/// <summary>One blamed line: who last touched it, in which commit.</summary>
public sealed record GitBlameLine(int Line, string Hash, string Author, DateTimeOffset When, string Text);

public sealed record GitStashInfo(int Index, string Message, string Hash);

public sealed record GitReflogEntry(string Hash, string Message, DateTimeOffset When);

/// <summary>A commit that changed how often a searched string appears — the pickaxe result.</summary>
public sealed record GitHistoryMatch(GitCommitInfo Commit, IReadOnlyList<string> Paths, int Added, int Removed);

/// <summary>One linked worktree of the repository, with the state that decides whether it is safe to remove.</summary>
public sealed record GitWorktreeListItem(
    string Name,
    string Path,
    string Branch,
    bool   IsMerged,
    bool   IsPushed,
    int    StagedCount,
    int    ModifiedCount,
    bool   IsBroken)
{
    public bool HasUncommittedChanges => StagedCount > 0 || ModifiedCount > 0;
}

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

    /// <summary>
    /// Most recent commits on <paramref name="branch"/> (or HEAD), optionally filtered to a path and narrowed
    /// by <paramref name="filter"/>. A <see cref="GitLogFilter.Range"/> wins over <paramref name="branch"/> —
    /// asking for <c>v1.3.0..main</c> already names both ends, so a separate branch would be ambiguous.
    /// </summary>
    /// <exception cref="ArgumentException">A range endpoint doesn't resolve to a commit.</exception>
    public IReadOnlyList<GitCommitInfo> GetLog(int count, string? branch = null, string? path = null,
                                               GitLogFilter? filter = null)
    {
        using var repo = new Repository(folderPath);
        filter ??= new GitLogFilter();

        var commitFilter = new CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
        };

        if (filter.Range is { } range)
        {
            commitFilter.IncludeReachableFrom = ResolveCommit(repo, range.To)
                ?? throw new ArgumentException($"Revision '{range.To}' not found.");
            commitFilter.ExcludeReachableFrom = ResolveCommit(repo, range.From)
                ?? throw new ArgumentException($"Revision '{range.From}' not found.");
        }
        else if (!string.IsNullOrWhiteSpace(branch) && repo.Branches[branch] is { } b)
        {
            commitFilter.IncludeReachableFrom = b;
        }

        var commits = string.IsNullOrWhiteSpace(path)
            ? repo.Commits.QueryBy(commitFilter)
            : repo.Commits.QueryBy(path, commitFilter).Select(le => le.Commit);

        return Narrow(commits, filter)
              .Take(count)
              .Select(c => new GitCommitInfo(c.Sha[..7], c.Author.Name, c.Author.When, c.MessageShort))
              .ToList();
    }

    /// <summary>Applies the non-range filters, which libgit2's walker doesn't express, in walk order.</summary>
    private static IEnumerable<Commit> Narrow(IEnumerable<Commit> commits, GitLogFilter f)
    {
        if (f.NoMerges)                                 commits = commits.Where(c => c.Parents.Count() <= 1);
        if (f.Since is { } since)                       commits = commits.Where(c => c.Author.When >= since);
        if (f.Until is { } until)                       commits = commits.Where(c => c.Author.When <= until);
        if (!string.IsNullOrWhiteSpace(f.Author))       commits = commits.Where(c => Has(c.Author.Name, f.Author) || Has(c.Author.Email, f.Author));
        if (!string.IsNullOrWhiteSpace(f.Grep))         commits = commits.Where(c => Has(c.Message, f.Grep));
        return commits;

        static bool Has(string? haystack, string? needle) =>
            haystack is not null && needle is not null
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Diff between two revisions (branches, tags or hashes), rendered per <paramref name="format"/>. This is
    /// the "what changed between these two releases" query — distinct from <see cref="GetDiff"/>, which only
    /// ever looks at uncommitted work.
    /// </summary>
    /// <exception cref="ArgumentException">Either endpoint doesn't resolve to a commit.</exception>
    public string GetDiffBetween(string from, string to, string? path = null,
                                 GitDiffFormat format = GitDiffFormat.Stat)
    {
        using var repo = new Repository(folderPath);

        var a = ResolveCommit(repo, from) ?? throw new ArgumentException($"Revision '{from}' not found.");
        var b = ResolveCommit(repo, to)   ?? throw new ArgumentException($"Revision '{to}' not found.");

        string[]? paths = string.IsNullOrWhiteSpace(path) ? null : [path];
        var patch = paths is null
            ? repo.Diff.Compare<Patch>(a.Tree, b.Tree)
            : repo.Diff.Compare<Patch>(a.Tree, b.Tree, paths);

        return format switch
        {
            GitDiffFormat.Patch    => patch.Content,
            GitDiffFormat.NameOnly => string.Join('\n', patch.Select(e => e.Path)),
            _                      => FormatStat(patch)
        };
    }

    /// <summary>Renders a patch the way <c>git diff --stat</c> does: per-file counts, then a total line.</summary>
    private static string FormatStat(Patch patch)
    {
        var entries = patch.ToList();
        if (entries.Count == 0) return string.Empty;

        var width = entries.Max(e => e.Path.Length);
        var lines = entries.Select(e =>
            $"{e.Path.PadRight(width)} | {e.LinesAdded + e.LinesDeleted,5} +{e.LinesAdded} -{e.LinesDeleted}");

        return string.Join('\n', lines)
             + $"\n {entries.Count} file(s) changed, {patch.LinesAdded} insertion(s), {patch.LinesDeleted} deletion(s)";
    }

    /// <summary>Tags with their target commit and date, newest first; optionally name-filtered by substring.</summary>
    public IReadOnlyList<GitTagInfo> GetTags(string? pattern = null)
    {
        using var repo = new Repository(folderPath);
        return repo.Tags
            .Select(t =>
            {
                var c = t.PeeledTarget as Commit;
                return new GitTagInfo(t.FriendlyName, (c?.Sha ?? t.Target.Sha)[..7], c?.Author.When, c?.MessageShort);
            })
            .Where(t => string.IsNullOrWhiteSpace(pattern)
                     || t.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.When ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// The contents of <paramref name="path"/> as of <paramref name="revision"/>. Null when the revision or
    /// the path doesn't exist there; the caller distinguishes those via <see cref="RevisionExists"/>.
    /// </summary>
    public string? GetFileAt(string revision, string path)
    {
        using var repo = new Repository(folderPath);
        if (ResolveCommit(repo, revision) is not { } commit) return null;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        return commit[normalized]?.Target is Blob blob ? blob.GetContentText() : null;
    }

    /// <summary>Whether a revision string resolves to a commit — lets a caller tell "bad rev" from "no such file".</summary>
    public bool RevisionExists(string revision)
    {
        using var repo = new Repository(folderPath);
        return ResolveCommit(repo, revision) is not null;
    }

    /// <summary>
    /// Resolves a revision string — branch, tag, full or abbreviated hash, or anything else
    /// <c>rev-parse</c> understands (<c>HEAD~2</c>, <c>main@{u}</c>) — to a commit, or null.
    /// </summary>
    private static Commit? ResolveCommit(Repository repo, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision)) return null;
        try { return repo.Lookup<GitObject>(revision)?.Peel<Commit>(); }
        catch (LibGit2SharpException) { return null; }   // unparseable / not a commit-ish
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

    // ── Divergence & containment ──────────────────────────────────────────

    /// <summary>
    /// How <paramref name="a"/> and <paramref name="b"/> relate: common ancestor plus ahead/behind counts.
    /// Unlike <see cref="GetStatus"/>'s ahead/behind — which only ever compares the current branch to its
    /// upstream — this answers the question for any two refs.
    /// </summary>
    /// <exception cref="ArgumentException">Either ref doesn't resolve to a commit.</exception>
    public GitDivergence GetDivergence(string a, string b)
    {
        using var repo = new Repository(folderPath);

        var ca = ResolveCommit(repo, a) ?? throw new ArgumentException($"Revision '{a}' not found.");
        var cb = ResolveCommit(repo, b) ?? throw new ArgumentException($"Revision '{b}' not found.");

        var div = repo.ObjectDatabase.CalculateHistoryDivergence(ca, cb);
        return new GitDivergence(a, b, div.CommonAncestor?.Sha[..7], div.AheadBy, div.BehindBy);
    }

    /// <summary>
    /// Branches and tags that can reach <paramref name="revision"/> — git's <c>--contains</c>. The honest
    /// answer to "is this commit already somewhere safe" before deleting anything.
    /// </summary>
    /// <exception cref="ArgumentException">The revision doesn't resolve to a commit.</exception>
    public IReadOnlyList<GitRefMention> GetRefsContaining(string revision)
    {
        using var repo = new Repository(folderPath);
        var target = ResolveCommit(repo, revision) ?? throw new ArgumentException($"Revision '{revision}' not found.");

        var mentions = new List<GitRefMention>();

        foreach (var branch in repo.Branches)
            if (branch.Tip is { } tip && Reaches(repo, tip, target))
                mentions.Add(new GitRefMention(branch.FriendlyName, branch.IsRemote, IsTag: false));

        foreach (var tag in repo.Tags)
            if (tag.PeeledTarget is Commit tip && Reaches(repo, tip, target))
                mentions.Add(new GitRefMention(tag.FriendlyName, IsRemote: false, IsTag: true));

        return mentions;
    }

    /// <summary>Whether <paramref name="from"/> can reach <paramref name="target"/> — i.e. contains it.</summary>
    private static bool Reaches(Repository repo, Commit from, Commit target) =>
        from.Sha == target.Sha
        || repo.ObjectDatabase.CalculateHistoryDivergence(target, from).AheadBy is 0;

    // ── Blame ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-line authorship for a file, optionally limited to a 1-based line range. Reads the line text from
    /// the blamed revision itself (default HEAD) so text and attribution can never disagree.
    /// </summary>
    /// <exception cref="ArgumentException">The revision or the path doesn't resolve.</exception>
    public IReadOnlyList<GitBlameLine> GetBlame(string path, int? startLine = null, int? endLine = null,
                                                string revision = "HEAD")
    {
        using var repo = new Repository(folderPath);

        var commit = ResolveCommit(repo, revision) ?? throw new ArgumentException($"Revision '{revision}' not found.");
        var normalized = path.Replace('\\', '/').TrimStart('/');

        if (commit[normalized]?.Target is not Blob blob)
            throw new ArgumentException($"'{path}' does not exist at {revision}.");

        var text  = blob.GetContentText().Replace("\r\n", "\n").Split('\n');
        var blame = repo.Blame(normalized, new BlameOptions { StartingAt = commit });

        var from = Math.Max(1, startLine ?? 1);
        var to   = Math.Min(text.Length, endLine ?? text.Length);

        var lines = new List<GitBlameLine>();
        for (var line = from; line <= to; line++)
        {
            // Blame indexes by final line number; a line past the end of the blamed range has no hunk.
            var hunk = blame.FirstOrDefault(h => line >= h.FinalStartLineNumber + 1
                                              && line <= h.FinalStartLineNumber + h.LineCount);
            if (hunk?.FinalCommit is not { } c) continue;

            lines.Add(new GitBlameLine(line, c.Sha[..7], c.Author.Name, c.Author.When, text[line - 1]));
        }
        return lines;
    }

    // ── History search (pickaxe) ──────────────────────────────────────────

    /// <summary>
    /// Finds commits whose diff adds or removes lines containing <paramref name="pattern"/> — git's
    /// <c>-S</c>/<c>-G</c>. libgit2 has no pickaxe, so this walks and diffs; <paramref name="maxCommits"/>
    /// bounds that cost and <paramref name="path"/> narrows it further. Merge commits are skipped: their
    /// diff against the first parent restates work already attributed to the branch it came from.
    /// </summary>
    public IReadOnlyList<GitHistoryMatch> SearchHistory(string pattern, string? path = null,
                                                        int maxCommits = 200, int maxMatches = 50)
    {
        if (string.IsNullOrEmpty(pattern)) return [];

        using var repo = new Repository(folderPath);
        var matches = new List<GitHistoryMatch>();

        foreach (var commit in repo.Commits.Take(maxCommits))
        {
            if (matches.Count >= maxMatches) break;
            if (commit.Parents.Count() > 1) continue;

            var parent = commit.Parents.FirstOrDefault();
            var patch  = string.IsNullOrWhiteSpace(path)
                ? repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree)
                : repo.Diff.Compare<Patch>(parent?.Tree, commit.Tree, [path]);

            var (added, removed) = (0, 0);
            var paths = new List<string>();

            foreach (var entry in patch)
            {
                var (a, r) = CountPickaxe(entry.Patch, pattern);
                if (a + r == 0) continue;
                added += a; removed += r;
                paths.Add(entry.Path);
            }

            if (paths.Count > 0)
                matches.Add(new GitHistoryMatch(
                    new GitCommitInfo(commit.Sha[..7], commit.Author.Name, commit.Author.When, commit.MessageShort),
                    paths, added, removed));
        }
        return matches;
    }

    /// <summary>Counts added/removed diff lines mentioning <paramref name="pattern"/> (case-insensitive).</summary>
    private static (int Added, int Removed) CountPickaxe(string patchText, string pattern)
    {
        var (added, removed) = (0, 0);
        foreach (var line in patchText.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("+++", StringComparison.Ordinal)
                                 || line.StartsWith("---", StringComparison.Ordinal)) continue;
            if (!line.Contains(pattern, StringComparison.OrdinalIgnoreCase)) continue;

            if      (line[0] == '+') added++;
            else if (line[0] == '-') removed++;
        }
        return (added, removed);
    }

    // ── Recovery surface: stashes & reflog ────────────────────────────────

    /// <summary>Saved stashes, most recent first.</summary>
    public IReadOnlyList<GitStashInfo> GetStashes()
    {
        using var repo = new Repository(folderPath);
        return repo.Stashes
            .Select((s, i) => new GitStashInfo(i, s.Message, s.WorkTree.Sha[..7]))
            .ToList();
    }

    /// <summary>
    /// HEAD's reflog, most recent first — where a commit still shows up after the branch naming it is gone.
    /// Empty for a repository with no reflog (a fresh clone, or one that has never moved HEAD).
    /// </summary>
    public IReadOnlyList<GitReflogEntry> GetReflog(int count = 50)
    {
        using var repo = new Repository(folderPath);
        try
        {
            return repo.Refs.Log(repo.Refs.Head)
                .Take(count)
                .Select(e => new GitReflogEntry(e.To.Sha[..7], e.Message, e.Committer.When))
                .ToList();
        }
        catch (LibGit2SharpException) { return []; }   // no reflog for this ref
    }

    // ── Worktrees (repo-wide) ─────────────────────────────────────────────

    /// <summary>
    /// Every linked worktree of this repository with its branch and merge/push/dirty state. Reuses
    /// <see cref="GitWorktreeService"/> per worktree, so "is it safe to remove" is answered the same way
    /// here as in the viewlet banner; a worktree whose folder is gone reads as broken rather than throwing.
    /// </summary>
    public IReadOnlyList<GitWorktreeListItem> GetWorktrees()
    {
        using var repo = new Repository(folderPath);

        var items = new List<GitWorktreeListItem>();
        foreach (var wt in repo.Worktrees)
        {
            string? dir = null;
            try { dir = wt.WorktreeRepository?.Info.WorkingDirectory?.TrimEnd('\\', '/'); }
            catch { /* dangling registration — reported as broken below */ }

            if (dir is null || !Directory.Exists(dir))
            {
                items.Add(new GitWorktreeListItem(wt.Name, dir ?? "(missing)", "(unknown)",
                                                  false, false, 0, 0, IsBroken: true));
                continue;
            }

            var info = new GitWorktreeService(dir).GetInfo();
            items.Add(info is null
                ? new GitWorktreeListItem(wt.Name, dir, "(unknown)", false, false, 0, 0, IsBroken: true)
                : new GitWorktreeListItem(wt.Name, dir, info.Branch, info.IsMerged, info.IsPushed,
                                          info.StagedCount, info.ModifiedCount, info.IsBroken));
        }
        return items;
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
