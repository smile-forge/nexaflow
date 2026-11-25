using System.IO;
using System.Text;
using LibGit2Sharp;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Localization;

namespace Nexaflow.Features.Git.Services;

/// <summary>
/// A working copy on a location, read and changed by the host's own <c>git</c> (<see cref="IProgramHost"/>): its status is
/// what git there says of the files — their modes and links as Linux has them — and what a checkout or a pull writes keeps
/// the names and permissions the host gives it. What git does not change itself when a worktree is removed — its folder,
/// its registration — is moved and deleted through the location. git never asks for anything here: a pull that needs a
/// credential the host's git cannot find fails, saying so.
/// </summary>
public sealed class HostGitWorkingCopy(IProgramHost host, IVirtualFileSystem files, string folderPath) : IGitWorkingCopy
{
    // A read takes no lock a person's own git could be waiting on, and speaks the C locale, whose wording a missing
    // repository is recognised by.
    private static readonly IReadOnlyDictionary<string, string> Reading = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_OPTIONAL_LOCKS"]  = "0",
        ["LC_ALL"]              = "C",
    };

    // A change speaks the person's language, since what it says is shown to them.
    private static readonly IReadOnlyDictionary<string, string> Changing = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    private volatile GitStatus?       _lastStatus;
    private volatile GitWorktreeInfo? _lastWorktree;

    public async Task<GitStatus> GetStatusAsync(CancellationToken ct)
    {
        var status   = ReadAsync(folderPath, ct, "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal", "--ignore-submodules=all");
        var tip      = ReadAsync(folderPath, ct, "log", "-1", "--format=%H%x00%s%x00%aI");
        var branches = ReadAsync(folderPath, ct, "for-each-ref", "--format=%(refname:short)", "refs/heads/");
        await Task.WhenAll(status, tip, branches).ConfigureAwait(false);

        var report = GitPorcelain.ParseStatus(Require(await status));
        var last   = (await tip).Succeeded ? GitPorcelain.ParseLastCommit((await tip).Output) : null;
        var locals = (await branches).Succeeded
            ? (await branches).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(n => n).ToList()
            : [];

        var read = new GitStatus(report.Branch, report.Upstream, report.Ahead, report.Behind,
                                 report.Staged, report.Modified, report.Untracked,
                                 last?.Hash, last?.Subject, last?.When, locals, report.IsDetached);
        _lastStatus = read;
        return read;
    }

    public async Task<string> GetDiffAsync(string? path, bool staged, CancellationToken ct)
    {
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff", staged ? "--cached" : "HEAD" };
        if (!string.IsNullOrWhiteSpace(path)) args.AddRange(["--", path.Replace('\\', '/').TrimStart('/')]);
        return Require(await ReadAsync(folderPath, ct, [.. args]).ConfigureAwait(false));
    }

    public async Task<string> GetDiffBetweenAsync(string from, string to, string? path, GitDiffFormat format, CancellationToken ct)
    {
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff" };
        if (format == GitDiffFormat.Stat)     args.Add("--stat=200");
        if (format == GitDiffFormat.NameOnly) args.Add("--name-only");
        args.AddRange([from, to]);
        if (!string.IsNullOrWhiteSpace(path)) args.AddRange(["--", path.Replace('\\', '/').TrimStart('/')]);

        var exit = await ReadAsync(folderPath, ct, [.. args]).ConfigureAwait(false);
        return exit.Succeeded ? exit.Output.TrimEnd('\n') : throw new ArgumentException(GitPorcelain.Said(exit));
    }

    public async Task<GitWorktreeInfo?> GetWorktreeAsync(CancellationToken ct)
    {
        var info = await ReadWorktreeAsync(folderPath, ct).ConfigureAwait(false);
        _lastWorktree = info;
        return info;
    }

    public async Task<IReadOnlyList<GitWorktreeListItem>> GetWorktreesAsync(CancellationToken ct)
    {
        var listed = GitPorcelain.ParseWorktreeList(Require(await ReadAsync(folderPath, ct, "worktree", "list", "--porcelain").ConfigureAwait(false)));
        var items  = new List<GitWorktreeListItem>();
        foreach (var worktree in listed)
        {
            var name = GitPorcelain.NameOf(worktree.Path);
            var info = worktree.IsPrunable ? null : await ReadWorktreeAsync(host.PathOnLocation(worktree.Path), ct).ConfigureAwait(false);
            items.Add(info is null or { IsBroken: true }
                ? new GitWorktreeListItem(name, worktree.Path, "(unknown)", false, false, 0, 0, IsBroken: true)
                : new GitWorktreeListItem(name, worktree.Path, info.Branch, info.IsMerged, info.IsPushed,
                                          info.StagedCount, info.ModifiedCount, info.IsBroken));
        }
        return items;
    }

    public async Task SwitchBranchAsync(string branch, CancellationToken ct)
    {
        var exit = await RunAsync(folderPath, Changing, ct, "checkout", branch, "--").ConfigureAwait(false);
        if (!exit.Succeeded) throw new InvalidOperationException(GitPorcelain.Said(exit));
    }

    public async Task<GitPullResult> PullAsync(CancellationToken ct)
    {
        try
        {
            var before = await HeadAsync(folderPath, ct).ConfigureAwait(false);
            var pull   = await RunAsync(folderPath, Changing, ct, "pull", "--ff-only").ConfigureAwait(false);
            if (!pull.Succeeded) return new(GitPullOutcome.Failed, Str.Format("Git.Pull.FailedFormat", GitPorcelain.Said(pull)));

            return await HeadAsync(folderPath, ct).ConfigureAwait(false) == before
                ? new(GitPullOutcome.UpToDate, Str.Get("Git.Pull.UpToDate"))
                : new(GitPullOutcome.Ok, Str.Get("Git.Pull.Complete"));
        }
        catch (Exception ex) when (ex is LocationException or NotSupportedException or IOException)
        {
            return new(GitPullOutcome.Failed, Str.Format("Git.Pull.FailedFormat", ex.Message));
        }
    }

    /// <summary>
    /// Removes the worktree as <see cref="GitWorktreeService.Remove"/> does, each step through the location: the folder is
    /// first renamed aside, so nothing of git is touched unless the worktree is out of the way; then its registration is
    /// deleted, which frees the branch it owned for the host's git to delete; then the renamed folder goes.
    /// </summary>
    public async Task<GitWorktreeRemovalResult> RemoveWorktreeAsync(CancellationToken ct)
    {
        if (await ReadPointerAsync(folderPath, ct).ConfigureAwait(false) is not { } pointer)
            return new(false, Str.Get("Git.Worktree.Removal.NotWorktree"));

        var hostFolder = host.HostPathOf(folderPath) ?? throw new ArgumentException($"'{folderPath}' is not on {host.Label}.");
        var name       = GitPorcelain.NameOf(hostFolder);
        var info       = await ReadWorktreeAsync(folderPath, ct).ConfigureAwait(false);
        var branch     = info is { IsBroken: false, IsDetached: false } ? info.Branch : null;
        var admin      = GitPorcelain.AdminFolderOf(pointer, hostFolder);
        var common     = admin is null ? null : await CommonFolderOfAsync(admin, ct).ConfigureAwait(false);

        // 1. Out of the way first, whole or not at all: a failure leaves the worktree as it was.
        var parent = files.GetParentPath(folderPath) ?? throw new ArgumentException($"'{folderPath}' has no parent folder.");
        var stash  = files.ChildPath(parent, $"{name}.removing-{Guid.NewGuid():N}");
        try { await files.MoveAsync(folderPath, stash, replace: false, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is LocationException or IOException or UnauthorizedAccessException)
        {
            return new(false, Str.Format("Git.Worktree.Removal.InUseFormat", ex.Message));
        }

        var problems = new List<string>();

        // 2. The registration — what `git worktree prune` would delete for this worktree alone.
        if (admin is not null && common is not null)
        {
            var registration = host.PathOnLocation(admin);
            try
            {
                if (await files.ExistsAsync(registration, ct).ConfigureAwait(false))
                    await files.DeleteAsync(registration, recursive: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is LocationException or IOException)
            {
                problems.Add(Str.Format("Git.Worktree.Removal.RegistrationProblemFormat", ex.Message));
            }
        }

        // 3. The branch it owned, by the main repository's git.
        if (branch is not null && common is not null)
        {
            try
            {
                var deleted = await RunAsync(parent, Changing, ct, "--git-dir", common, "branch", "-D", branch).ConfigureAwait(false);
                if (!deleted.Succeeded) problems.Add(Str.Format("Git.Worktree.Removal.BranchProblemFormat", branch, GitPorcelain.Said(deleted)));
            }
            catch (Exception ex) when (ex is LocationException or IOException)
            {
                problems.Add(Str.Format("Git.Worktree.Removal.BranchProblemFormat", branch, ex.Message));
            }
        }

        // 4. The folder moved aside; a leftover is a clearly named remnant, not a broken worktree.
        try { await files.DeleteAsync(stash, recursive: true, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is LocationException or IOException) { }

        if (problems.Count == 0)
            return new(true, branch is not null
                ? Str.Format("Git.Worktree.Removal.RemovedWithBranchFormat", name, branch)
                : Str.Format("Git.Worktree.Removal.RemovedFormat", name));

        var failures = string.Join(", ", problems);
        return new(true, branch is not null
            ? Str.Format("Git.Worktree.Removal.RemovedWithBranchPartialFormat", name, branch, failures)
            : Str.Format("Git.Worktree.Removal.RemovedPartialFormat", name, failures));
    }

    public string? DescribeForAssistant()
        => _lastStatus is { } status ? GitContextFormatter.Describe(status, _lastWorktree) : null;

    // ── Reading a worktree ────────────────────────────────────────────────

    /// <summary>The linked-worktree state of a folder on the location, or null when its <c>.git</c> is not a pointer file.</summary>
    private async Task<GitWorktreeInfo?> ReadWorktreeAsync(string folder, CancellationToken ct)
    {
        if (await ReadPointerAsync(folder, ct).ConfigureAwait(false) is not { } pointer) return null;

        var hostFolder   = host.HostPathOf(folder) ?? throw new ArgumentException($"'{folder}' is not on {host.Label}.");
        var name         = GitPorcelain.NameOf(hostFolder);
        var worktreeName = GitPorcelain.AdminFolderOf(pointer, hostFolder) is { } admin ? GitPorcelain.NameOf(admin) : name;

        var head = await ReadAsync(folder, ct, "symbolic-ref", "-q", "--short", "HEAD").ConfigureAwait(false);
        if (head.ExitCode is not (0 or 1)) return Broken(name, worktreeName);   // the repository will not open

        var tip   = ReadAsync(folder, ct, "rev-parse", "-q", "--verify", "HEAD^{commit}");
        var refs  = ReadAsync(folder, ct, "for-each-ref", "--format=%(refname)%00%(objectname)%00%(upstream)", "refs/heads/", "refs/remotes/");
        var state = ReadAsync(folder, ct, "status", "--porcelain=v2", "-z", "--untracked-files=no", "--ignore-submodules=all");
        await Task.WhenAll(tip, refs, state).ConfigureAwait(false);

        string? tipSha     = (await tip).Succeeded ? (await tip).Output.Trim() : null;
        string? headBranch = head.Succeeded ? head.Output.Trim() : null;
        var (own, remote, target) = GitPorcelain.ResolveWorktreeRefs(GitPorcelain.ParseRefs(Require(await refs)), headBranch, tipSha, name);
        var (staged, modified)    = GitPorcelain.CountChanges(Require(await state));

        int aheadOfRemote = 0;
        if (remote is not null && tipSha is not null)
            aheadOfRemote = await CountAsync(folder, $"{remote.Sha}..{tipSha}", ct).ConfigureAwait(false);
        bool merged = target is not null && tipSha is not null
                      && await CountAsync(folder, $"{target.Sha}..{tipSha}", ct).ConfigureAwait(false) == 0;

        return new GitWorktreeInfo(
            DisplayName:       name,
            WorktreeName:      worktreeName,
            Branch:            own ?? "(detached)",
            IsDetached:        own is null,
            Upstream:          remote?.Name,
            HasUpstream:       remote is not null,
            AheadOfRemote:     aheadOfRemote,
            IsPushed:          remote is not null && tipSha is not null && aheadOfRemote == 0,
            MergeTargetBranch: target?.Name,
            IsMerged:          merged,
            StagedCount:       staged,
            ModifiedCount:     modified);
    }

    private static GitWorktreeInfo Broken(string name, string worktreeName) => new(
        DisplayName: name, WorktreeName: worktreeName, Branch: "(unknown)", IsDetached: false, Upstream: null,
        HasUpstream: false, AheadOfRemote: 0, IsPushed: false, MergeTargetBranch: null, IsMerged: false,
        StagedCount: 0, ModifiedCount: 0, IsBroken: true);

    /// <summary>The text of the folder's <c>.git</c> when it is a file — a linked worktree's pointer — or null.</summary>
    private async Task<string?> ReadPointerAsync(string folder, CancellationToken ct)
    {
        var pointer = files.ChildPath(folder, ".git");
        return await files.GetEntryAsync(pointer, ct).ConfigureAwait(false) is { IsDirectory: false }
            ? await files.ReadAllTextAsync(pointer, Encoding.UTF8, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>The main repository's <c>.git</c> folder for a worktree's registration: what its <c>commondir</c> names,
    /// or the folder two above the registration, where git keeps every registration.</summary>
    private async Task<string> CommonFolderOfAsync(string admin, CancellationToken ct)
    {
        var commondir = host.PathOnLocation(admin.TrimEnd('/') + "/commondir");
        try
        {
            if (await files.ExistsAsync(commondir, ct).ConfigureAwait(false))
                return GitPorcelain.ResolvePosix(admin, (await files.ReadAllTextAsync(commondir, Encoding.UTF8, ct).ConfigureAwait(false)).Trim());
        }
        catch (Exception ex) when (ex is LocationException or IOException) { /* a remnant's registration may be gone */ }
        return GitPorcelain.ResolvePosix(admin, "../..");
    }

    private async Task<string?> HeadAsync(string folder, CancellationToken ct)
    {
        var head = await ReadAsync(folder, ct, "rev-parse", "-q", "--verify", "HEAD").ConfigureAwait(false);
        return head.Succeeded ? head.Output.Trim() : null;
    }

    private async Task<int> CountAsync(string folder, string range, CancellationToken ct)
    {
        var counted = await ReadAsync(folder, ct, "rev-list", "--count", range).ConfigureAwait(false);
        return counted.Succeeded && int.TryParse(counted.Output.Trim(), out var count) ? count : 0;
    }

    // ── Running git ───────────────────────────────────────────────────────

    private Task<ProgramExit> ReadAsync(string folder, CancellationToken ct, params string[] args)
        => RunAsync(folder, Reading, ct, args);

    private async Task<ProgramExit> RunAsync(string folder, IReadOnlyDictionary<string, string> environment, CancellationToken ct, params string[] args)
        => await host.RunProgramAsync(new ProgramRun("git", args, folder) { Environment = environment }, lines: null, ct).ConfigureAwait(false);

    /// <summary>What a read printed; a folder that is not a repository is <see cref="RepositoryNotFoundException"/>, as the
    /// in-process status reports it, and any other failure says what git said.</summary>
    private static string Require(ProgramExit exit)
    {
        if (exit.Succeeded) return exit.Output;
        if (exit.Errors.Contains("not a git repository", StringComparison.Ordinal))
            throw new RepositoryNotFoundException(GitPorcelain.Said(exit));
        throw new InvalidOperationException(GitPorcelain.Said(exit));
    }
}
