using LibGit2Sharp;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Git.Services;

/// <summary>A working copy on this PC, read and changed in process with LibGit2Sharp; a pull authenticates with the
/// credential manager's stored token.</summary>
public sealed class LocalGitWorkingCopy(string folderPath, GitCredentialHelper credentials) : IGitWorkingCopy
{
    private readonly GitService         _git       = new(folderPath);
    private readonly GitWorktreeService _worktrees = new(folderPath);

    public Task<GitStatus> GetStatusAsync(CancellationToken ct) => Task.Run(_git.GetStatus, ct);

    public Task<string> GetDiffAsync(string? path, bool staged, CancellationToken ct) => Task.Run(() => _git.GetDiff(path, staged), ct);

    public Task<string> GetDiffBetweenAsync(string from, string to, string? path, GitDiffFormat format, CancellationToken ct)
        => Task.Run(() => _git.GetDiffBetween(from, to, path, format), ct);

    public Task<GitWorktreeInfo?> GetWorktreeAsync(CancellationToken ct) => Task.Run(_worktrees.GetInfo, ct);

    public Task<IReadOnlyList<GitWorktreeListItem>> GetWorktreesAsync(CancellationToken ct) => Task.Run(_git.GetWorktrees, ct);

    public Task SwitchBranchAsync(string branch, CancellationToken ct) => Task.Run(() =>
    {
        using var repo = new Repository(folderPath);
        if (repo.Branches[branch] is { } local) Commands.Checkout(repo, local);
    }, ct);

    /// <summary>Fetch and merge run off the calling thread: LibGit2Sharp invokes the credentials provider synchronously on
    /// the fetching thread, and that spawns <c>git credential fill</c>.</summary>
    public Task<GitPullResult> PullAsync(CancellationToken ct) => Task.Run(Pull, ct);

    public Task<GitWorktreeRemovalResult> RemoveWorktreeAsync(CancellationToken ct) => Task.Run(_worktrees.Remove, ct);

    public string? DescribeForAssistant()
    {
        // Wording lives in GitContextFormatter so it's unit-testable without a repo.
        try { return GitContextFormatter.Describe(_git.GetStatus(), _worktrees.GetInfo()); }
        catch { return null; }
    }

    private GitPullResult Pull()
    {
        try
        {
            using var repo = new Repository(folderPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now)
                ?? new Signature(Environment.UserName, $"{Environment.UserName}@local", DateTimeOffset.Now);

            var options = new PullOptions
            {
                FetchOptions = new FetchOptions { CredentialsProvider = credentials.Provider },
                MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly }
            };

            return Commands.Pull(repo, signature, options).Status switch
            {
                MergeStatus.UpToDate                                  => new(GitPullOutcome.UpToDate, Str.Get("Git.Pull.UpToDate")),
                MergeStatus.FastForward or MergeStatus.NonFastForward => new(GitPullOutcome.Ok, Str.Get("Git.Pull.Complete")),
                MergeStatus.Conflicts                                 => new(GitPullOutcome.Failed, Str.Get("Git.Pull.FailedConflicts")),
                var status                                            => new(GitPullOutcome.Failed, Str.Format("Git.Pull.FailedFormat", status))
            };
        }
        catch (Exception ex) when (IsAuthFailure(ex)) { return new(GitPullOutcome.AuthFailed, Str.Format("Git.Pull.FailedFormat", ex.Message)); }
        catch (Exception ex)                          { return new(GitPullOutcome.Failed,     Str.Format("Git.Pull.FailedFormat", ex.Message)); }
    }

    /// <summary>True for a fetch/pull failure caused by authentication (missing/invalid credentials), so the
    /// caller can offer the token-capture fallback rather than treating it as a hard error.</summary>
    internal static bool IsAuthFailure(Exception ex)
        => ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("credential",     StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("401")
        || ex.Message.Contains("403");
}
