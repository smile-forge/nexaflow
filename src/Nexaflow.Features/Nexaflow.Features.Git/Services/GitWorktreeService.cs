using System.IO;
using LibGit2Sharp;

namespace Nexaflow.Features.Git.Services;

/// <summary>
/// Worktree-specific state for a folder that is a linked git worktree. Drives the viewlet's worktree
/// banner and the removal flow. <see cref="IsMerged"/> / <see cref="IsPushed"/> answer the two questions
/// the viewlet surfaces — "has this been merged into the mainline?" and "have its commits reached the
/// remote?" — and gate whether removal needs a confirmation prompt.
/// </summary>
public sealed record GitWorktreeInfo(
    string  DisplayName,          // the worktree folder's name (what the user sees)
    string  WorktreeName,         // git's internal worktree name (folder under .git/worktrees/)
    string  Branch,               // checked-out branch friendly name ("(detached)" when headless)
    bool    IsDetached,           // HEAD is detached — there is no branch to delete
    string? Upstream,             // remote-tracking branch the commits would push to, if any
    bool    HasUpstream,
    int     AheadOfRemote,        // local commits not yet on the remote (0 when fully pushed / no upstream)
    bool    IsPushed,             // a remote branch exists and contains the worktree's tip
    string? MergeTargetBranch,    // branch merged-state is tested against (e.g. "main" / "origin/main")
    bool    IsMerged,             // the worktree's tip is fully contained in the merge target
    int     StagedCount,
    int     ModifiedCount)
{
    public bool HasUncommittedChanges => StagedCount > 0 || ModifiedCount > 0;

    /// <summary>
    /// Removing a merged worktree with no uncommitted tracked changes is non-destructive (its commits live
    /// on in the mainline), so the viewlet skips the confirmation prompt in exactly that case. Anything
    /// unmerged, or with staged/modified work, prompts first.
    /// </summary>
    public bool CanRemoveWithoutConfirmation => IsMerged && !HasUncommittedChanges;
}

/// <summary>The outcome of a worktree removal: whether it fully succeeded, a user-facing message, and the
/// folder to navigate to afterwards (the removed worktree's parent).</summary>
public sealed record GitWorktreeRemovalResult(bool Success, string Message, string? NavigateTo);

/// <summary>
/// Detects whether a folder is a linked git worktree and, if so, reports its merge/push state and performs
/// a full removal (delete the working-tree folder, prune the worktree registration, delete its branch).
/// A linked worktree is identified by its <c>.git</c> being a <em>file</em> (<c>gitdir: …</c> pointer)
/// rather than a directory — the main checkout has a real <c>.git</c> directory and is not a worktree.
/// Read queries open a fresh <see cref="Repository"/> per call (like <see cref="GitService"/>); the mutating
/// <see cref="Remove"/> drives everything from the main repository so the branch is free to delete.
/// </summary>
public sealed class GitWorktreeService(string folderPath)
{
    /// <summary>True when <c>folderPath</c> is a linked worktree (its <c>.git</c> is a pointer file).</summary>
    public bool IsWorktree() => File.Exists(Path.Combine(folderPath, ".git"));

    /// <summary>Worktree merge/push/dirty state, or null when the folder is not a linked worktree.</summary>
    public GitWorktreeInfo? GetInfo()
    {
        if (!IsWorktree()) return null;

        using var repo = new Repository(folderPath);

        var head      = repo.Head;
        var tip       = head.Tip;
        var detached  = repo.Info.IsHeadDetached;
        var branch    = detached ? "(detached)" : head.FriendlyName;

        // ── Pushed? — a remote branch that contains the worktree's tip ──────
        var remote = ResolveRemoteBranch(repo, head, detached ? null : branch);
        int aheadOfRemote = 0;
        bool pushed = false;
        if (remote?.Tip is { } remoteTip && tip is not null)
        {
            var div        = repo.ObjectDatabase.CalculateHistoryDivergence(tip, remoteTip);
            aheadOfRemote  = div.AheadBy ?? 0;   // null = unrelated histories → treat as "not on remote"
            pushed         = div.AheadBy is 0;
        }

        // ── Merged? — tip fully contained in the mainline target ───────────
        var (targetName, targetTip) = ResolveMergeTarget(repo, detached ? null : branch);
        bool merged = false;
        if (tip is not null && targetTip is not null)
        {
            var div = repo.ObjectDatabase.CalculateHistoryDivergence(tip, targetTip);
            merged  = div.AheadBy is 0;          // no commits on the branch that the target lacks
        }

        // ── Uncommitted tracked changes ────────────────────────────────────
        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked  = false,
            IncludeIgnored    = false,
            ExcludeSubmodules = true
        });
        int staged   = status.Added.Count() + status.Staged.Count() + status.Removed.Count()
                     + status.RenamedInIndex.Count();
        int modified = status.Modified.Count() + status.Missing.Count() + status.RenamedInWorkDir.Count();

        return new GitWorktreeInfo(
            DisplayName:       new DirectoryInfo(folderPath).Name,
            WorktreeName:      ResolveWorktreeName() ?? new DirectoryInfo(folderPath).Name,
            Branch:            branch,
            IsDetached:        detached,
            Upstream:          remote?.FriendlyName,
            HasUpstream:       remote is not null,
            AheadOfRemote:     aheadOfRemote,
            IsPushed:          pushed,
            MergeTargetBranch: targetName,
            IsMerged:          merged,
            StagedCount:       staged,
            ModifiedCount:     modified);
    }

    // ── Removal ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fully removes the worktree: deletes the working-tree folder, drops the worktree registration from the
    /// main repository, and deletes the (now-unused) branch. Best-effort and self-reporting — never throws
    /// for an expected failure.
    /// <para>
    /// Ordering is deliberately fail-safe. The folder is vacated FIRST, by an <em>atomic rename</em> out of
    /// the way: if any file inside is in use (e.g. an app still running from the worktree) the rename fails
    /// as a whole and the worktree is left completely intact and recoverable — no git surgery has run, so we
    /// never leave a half-removed folder with its <c>.git</c> pointer destroyed. Only once the path is
    /// confirmed vacated do we drop the registration and branch. (An earlier design pruned via LibGit2Sharp
    /// first, which deletes the <c>.git</c> pointer + working tree up front and, on a locked file, orphaned
    /// the folder and branch — hence the rename-first approach.)
    /// </para>
    /// </summary>
    public GitWorktreeRemovalResult Remove()
    {
        if (!IsWorktree())
            return new(false, "This folder is not a git worktree.", null);

        string? branch;
        bool    detached;
        string  worktreeName;
        string  commonGitDir;
        try
        {
            using var repo = new Repository(folderPath);
            detached     = repo.Info.IsHeadDetached;
            branch       = detached ? null : repo.Head.FriendlyName;
            worktreeName = ResolveWorktreeName() ?? new DirectoryInfo(folderPath).Name;
            commonGitDir = ResolveCommonGitDir();
        }
        catch (Exception ex)
        {
            return new(false, $"Could not read the worktree: {ex.Message}", null);
        }

        var basePath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name     = new DirectoryInfo(basePath).Name;
        var parent   = Directory.GetParent(basePath)?.FullName;

        // 1. Vacate the working tree by an atomic same-volume rename. A locked/in-use file makes this fail as
        //    a unit, with the worktree still fully intact at its original path — a clean, recoverable failure.
        //    Nothing in git is touched until this succeeds.
        var stash = basePath + ".removing-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.Move(basePath, stash);
        }
        catch (Exception ex)
        {
            return new(false,
                "Couldn't remove the worktree — a file inside it is in use. Close anything open under it " +
                $"(and don't run the app from the worktree you're deleting), then try again. ({ex.Message})",
                null);
        }

        // The worktree path is now gone. From here every step is git bookkeeping the rename already made safe.
        var problems = new List<string>();

        // 2. Drop the worktree registration — deleting .git/worktrees/<name> is exactly `git worktree prune`
        //    for this worktree, and frees the branch to be deleted.
        var admin = Path.Combine(commonGitDir, "worktrees", worktreeName);
        if (Directory.Exists(admin))
            try { ForceDeleteDirectory(admin); } catch (Exception ex) { problems.Add($"registration ({ex.Message})"); }

        // 3. Delete the now-free branch (a fresh handle re-reads worktree state).
        if (!detached && branch is not null)
        {
            try
            {
                using var main = new Repository(commonGitDir);
                if (main.Branches[branch] is { IsRemote: false } b) main.Branches.Remove(b);
            }
            catch (Exception ex) { problems.Add($"branch '{branch}' ({ex.Message})"); }
        }

        // 4. Best-effort delete the stashed copy. Any leftover here is harmless throwaway — the worktree path
        //    itself is already cleanly gone and git is consistent.
        try { ForceDeleteDirectory(stash); } catch { /* clearly-named remnant; not a broken worktree */ }

        return problems.Count == 0
            ? new(true, $"Removed worktree '{name}'" + (branch is not null ? $" and branch '{branch}'." : "."), parent)
            : new(true, $"Removed worktree '{name}', but some git cleanup failed: {string.Join(", ", problems)}.", parent);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>The remote-tracking branch the worktree's commits belong on: its configured upstream, or a
    /// same-named <c>&lt;remote&gt;/&lt;branch&gt;</c> when no upstream is set. Null when nothing matches.</summary>
    private static Branch? ResolveRemoteBranch(Repository repo, Branch head, string? branchName)
    {
        try
        {
            if (head.TrackedBranch is { } tb && head.IsTracking) return tb;
        }
        catch (InvalidSpecificationException) { /* no valid upstream configured */ }

        if (branchName is null) return null;
        return repo.Branches.FirstOrDefault(b =>
            b.IsRemote && b.FriendlyName.EndsWith("/" + branchName, StringComparison.Ordinal));
    }

    /// <summary>The mainline branch to test merged-state against: a local <c>main</c>/<c>master</c>/… if
    /// present, else the matching remote-tracking branch. Never the worktree's own branch. Null when none
    /// is found (merged-state then reads as "unknown", so removal still prompts).</summary>
    private static (string? name, Commit? tip) ResolveMergeTarget(Repository repo, string? currentBranch)
    {
        string[] candidates = ["main", "master", "develop", "trunk"];

        foreach (var c in candidates)
            if (repo.Branches[c] is { IsRemote: false, Tip: not null } b &&
                !string.Equals(b.FriendlyName, currentBranch, StringComparison.Ordinal))
                return (b.FriendlyName, b.Tip);

        foreach (var c in candidates)
            if (repo.Branches.FirstOrDefault(b =>
                    b.IsRemote && b.FriendlyName.EndsWith("/" + c, StringComparison.Ordinal)) is { Tip: not null } rb &&
                !string.Equals(rb.FriendlyName, currentBranch, StringComparison.Ordinal))
                return (rb.FriendlyName, rb.Tip);

        return (null, null);
    }

    /// <summary>git's internal name for this worktree — the folder name under <c>.git/worktrees/</c>,
    /// read from the <c>.git</c> pointer file. Null if it can't be resolved.</summary>
    private string? ResolveWorktreeName()
    {
        var admin = ReadAdminDir();
        return admin is null ? null
            : new DirectoryInfo(admin.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
    }

    /// <summary>The shared <c>.git</c> directory of the main repository (the worktree's <c>commondir</c>).</summary>
    private string ResolveCommonGitDir()
    {
        var admin = ReadAdminDir()
            ?? throw new InvalidOperationException("Could not resolve the worktree's git directory.");

        var commonFile = Path.Combine(admin, "commondir");
        if (File.Exists(commonFile))
        {
            var rel = File.ReadAllText(commonFile).Trim();
            return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(admin, rel));
        }
        // Fallback: admin dir is "<common>/worktrees/<name>", so the common dir is two levels up.
        return Path.GetFullPath(Path.Combine(admin, "..", ".."));
    }

    /// <summary>Resolves the per-worktree admin directory from the <c>.git</c> pointer file
    /// (<c>gitdir: &lt;path&gt;</c>). Returns null if the file is missing or malformed.</summary>
    private string? ReadAdminDir()
    {
        var dotGit = Path.Combine(folderPath, ".git");
        if (!File.Exists(dotGit)) return null;

        const string prefix = "gitdir:";
        var line = File.ReadAllText(dotGit).Trim();
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var target = line[prefix.Length..].Trim();
        var admin  = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(folderPath, target));
        return Directory.Exists(admin) ? admin : null;
    }

    /// <summary>Recursively deletes a directory, clearing read-only attributes first (git pack/object files
    /// are read-only, which would otherwise fault the delete).</summary>
    private static void ForceDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best-effort */ }
        }
        Directory.Delete(dir, recursive: true);
    }
}
