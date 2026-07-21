using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Git.ClientTools;
using Nexaflow.Features.Git.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace Nexaflow.Features.Git.ViewModels;

/// <summary>
/// Backs the Git folder viewlet: the branch picker, working-tree status, last commit, pull, the linked-worktree
/// badge/removal, and the read-only git tools fed to the AI. Everything the user can observe or trigger is
/// state or a command here — the view only renders it (and owns the one thing that isn't state: the result
/// line's fade animation).
/// </summary>
/// <remarks>
/// The status line is exposed as <see cref="StatusSegments"/> — text plus a semantic <see cref="GitTone"/> —
/// rather than pre-coloured runs, so the wording and severity can be asserted without WPF and the view stays
/// the only place a theme brush is resolved.
/// </remarks>
public sealed partial class GitViewletViewModel : ObservableObject
{
    private readonly GitOptions          _options;
    private readonly IShellServices      _shell;
    private readonly string              _folderPath;
    private readonly IViewletController? _controller;
    private readonly GitService          _git;
    private readonly GitWorktreeService  _worktreeService;
    private readonly GitCredentialHelper _credHelper;

    /// <summary>How a pull ended — <see cref="PullOutcome.AuthFailed"/> is the one the token fallback retries.</summary>
    public enum PullOutcome { Ok, UpToDate, Failed, AuthFailed }

    /// <summary>Local branches offered by the branch picker; empty until the first refresh completes.</summary>
    public ObservableCollection<string> LocalBranches { get; } = [];

    /// <summary>The inline status line, in render order. Rebuilt by every refresh.</summary>
    public ObservableCollection<GitStatusSegment> StatusSegments { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchBranchCommand))]
    private string _branchName = "···";

    /// <summary>The tip commit line — short hash, subject and a humanised age — or empty when there is none.</summary>
    [ObservableProperty] private string _lastCommitLine = string.Empty;

    /// <summary>True while a refresh / branch switch / pull is in flight; gates the interactive controls.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInteractive))]
    [NotifyCanExecuteChangedFor(nameof(PullCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveWorktreeCommand))]
    private bool _isBusy;

    /// <summary>The inverse of <see cref="IsBusy"/> — what the click-driven controls (the branch picker, which
    /// opens a menu rather than running a command) bind their enablement to.</summary>
    public bool IsInteractive => !IsBusy;

    /// <summary>Caption of the pull control — "···" while pulling, so the button itself reports progress.</summary>
    [ObservableProperty] private string _pullCaption = "Pull";

    /// <summary>Set when the open folder is a linked worktree: drives the badge and the Remove control.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorktree))]
    [NotifyCanExecuteChangedFor(nameof(RemoveWorktreeCommand))]
    private GitWorktreeInfo? _worktree;

    public bool IsWorktree => Worktree is not null;

    /// <summary>Multi-line explanation of the worktree's merge/push state, shown on the badge.</summary>
    [ObservableProperty] private string _worktreeTooltip = string.Empty;

    /// <summary>The transient outcome line under the bar (pull result, load failure); empty when there's none.</summary>
    [ObservableProperty] private string _actionResult = string.Empty;

    /// <summary>True when <see cref="ActionResult"/> reports a failure rather than a success.</summary>
    [ObservableProperty] private bool _actionResultIsError;

    /// <summary>Only shown when the user has configured an external git GUI in options.</summary>
    public bool ShowGitManager => !string.IsNullOrWhiteSpace(_options.GitManagerPath);

    public GitViewletViewModel(
        GitOptions options, IShellServices shell, string folderPath, IViewletController? controller = null)
    {
        _options         = options;
        _shell           = shell;
        _folderPath      = folderPath;
        _controller      = controller;
        _git             = new GitService(folderPath);
        _worktreeService = new GitWorktreeService(folderPath);
        _credHelper      = new GitCredentialHelper(folderPath);
    }

    // ── Loading ───────────────────────────────────────────────────────────

    /// <summary>Re-reads the repo and rebuilds every displayed field. Worktree info is fetched independently
    /// and is resilient: a remnant with a dangling <c>.git</c> link yields a broken record instead of throwing,
    /// so Remove stays available to clean it up even when the repository itself won't open.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;

        GitWorktreeInfo? worktree = null;
        try { worktree = await Task.Run(_worktreeService.GetInfo); } catch { }

        try
        {
            ApplyStatus(await Task.Run(_git.GetStatus));
        }
        catch (RepositoryNotFoundException)
        {
            StatusSegments.Clear();
            LastCommitLine = string.Empty;
            BranchName     = worktree is { IsBroken: true } ? "broken worktree" : "not a repo";
            if (worktree is not { IsBroken: true }) SetActionResult("The .git file is broken", success: false);
        }
        catch (Exception ex)
        {
            BranchName = "error";
            StatusSegments.Clear();
            SetActionResult(ex.Message, success: false);
        }
        finally
        {
            ApplyWorktree(worktree);
            IsBusy = false;
        }
    }

    /// <summary>Projects a <see cref="GitStatus"/> onto the displayed fields — the branch, the picker's
    /// branch list, the coloured counts (or "clean") and the last-commit line.</summary>
    internal void ApplyStatus(GitStatus info)
    {
        BranchName = info.Branch;

        LocalBranches.Clear();
        foreach (var b in info.LocalBranches) LocalBranches.Add(b);

        StatusSegments.Clear();
        if (info.StagedCount    > 0) StatusSegments.Add(new($"{info.StagedCount} staged",       GitTone.Good));
        if (info.ModifiedCount  > 0) StatusSegments.Add(new($"{info.ModifiedCount} modified",   GitTone.Caution));
        if (info.UntrackedCount > 0) StatusSegments.Add(new($"{info.UntrackedCount} untracked", GitTone.Normal));

        if (info.Ahead is > 0 || info.Behind is > 0)
        {
            var parts = new[] { info.Ahead is > 0 ? $"↑{info.Ahead}" : null, info.Behind is > 0 ? $"↓{info.Behind}" : null };
            StatusSegments.Add(new(string.Join(" ", parts.Where(p => p is not null)), GitTone.Muted));
        }

        if (StatusSegments.Count == 0) StatusSegments.Add(new("clean", GitTone.Good));

        LastCommitLine = info.LastCommitHash is null ? string.Empty : FormatLastCommit(info);
    }

    private static string FormatLastCommit(GitStatus info)
    {
        var timeAgo = info.LastCommitWhen.HasValue ? FormatTimeAgo(info.LastCommitWhen.Value) : null;
        var subject = info.LastCommitSubject?.Trim();
        return string.IsNullOrEmpty(subject)
            ? $"{info.LastCommitHash}  {timeAgo}"
            : $"{info.LastCommitHash}  {subject}  ·  {timeAgo}";
    }

    // ── Worktree state ────────────────────────────────────────────────────

    /// <summary>Appends the linked-worktree state (merged / pushed, or a broken-remnant warning) to the status
    /// line already built by <see cref="ApplyStatus"/>, and builds the badge tooltip. A null worktree simply
    /// hides the badge and Remove control.</summary>
    internal void ApplyWorktree(GitWorktreeInfo? wt)
    {
        Worktree = wt;
        if (wt is null) { WorktreeTooltip = string.Empty; return; }

        // A remnant's repo won't open, so there is no merge/push state to report — just flag it for cleanup.
        if (wt.IsBroken)
        {
            StatusSegments.Add(new("broken remnant — Remove to clean up", GitTone.Bad));
            WorktreeTooltip = "Broken git worktree remnant\nIts .git link is dangling — the linked "
                            + "repository is gone.\nUse Remove to delete the leftover folder.";
            return;
        }

        if (wt.MergeTargetBranch is { } target)
            StatusSegments.Add(new(wt.IsMerged ? $"merged into {target}" : $"unmerged vs {target}",
                                   wt.IsMerged ? GitTone.Good : GitTone.Caution));

        if (!wt.HasUpstream)   StatusSegments.Add(new("no upstream", GitTone.Muted));
        else if (wt.IsPushed)  StatusSegments.Add(new("pushed", GitTone.Muted));
        else                   StatusSegments.Add(new(wt.AheadOfRemote > 0 ? $"unpushed ↑{wt.AheadOfRemote}" : "unpushed",
                                                      GitTone.Caution));

        WorktreeTooltip = BuildWorktreeTooltip(wt);
    }

    internal static string BuildWorktreeTooltip(GitWorktreeInfo wt)
    {
        var sb = new StringBuilder("Linked git worktree\n");
        sb.Append("Branch: ").Append(wt.Branch).Append('\n');
        sb.Append(wt.MergeTargetBranch is { } t
            ? (wt.IsMerged ? $"Merged into {t}" : $"Not merged into {t}")
            : "Merge target unknown").Append('\n');
        sb.Append(!wt.HasUpstream ? "No upstream configured"
            : wt.IsPushed ? $"Pushed to {wt.Upstream}"
            : $"{wt.AheadOfRemote} commit(s) not on {wt.Upstream}");
        if (wt.HasUncommittedChanges)
            sb.Append($"\n{wt.StagedCount + wt.ModifiedCount} uncommitted change(s)");
        return sb.ToString();
    }

    // ── Worktree removal ──────────────────────────────────────────────────

    private bool CanRemoveWorktree() => Worktree is not null && !IsBusy;

    /// <summary>Removes the linked worktree. A merged worktree with no uncommitted work is safe to drop (its
    /// commits live on in the mainline) so it goes without a prompt; anything unmerged, dirty or broken
    /// confirms first, spelling out exactly what is lost.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveWorktree))]
    private async Task RemoveWorktreeAsync()
    {
        if (Worktree is not { } wt) return;

        if (!wt.CanRemoveWithoutConfirmation
            && !await _shell.ConfirmAsync("Remove worktree?", BuildRemovalPrompt(wt), "Remove", "Cancel"))
            return;

        // Removal (quiescing the folder's other viewlets, deleting the folder) can take a while, so it runs off
        // the UI thread behind a busy mark — the browser shows "please wait" and re-homes when it clears.
        var busy = _shell.MarkFolderBusy(_folderPath, $"Removing worktree '{wt.DisplayName}'…");
        _ = RunRemovalAsync(busy);
    }

    private async Task RunRemovalAsync(IDisposable busy)
    {
        try
        {
            // Quiesce every viewlet on this folder first — e.g. the .NET viewlet's NuGet scan runs `dotnet`
            // with the folder as its working directory and would otherwise lock it against deletion.
            if (_controller is not null)
                try { await _controller.QuiesceFolderAsync(); } catch { /* best-effort; removal is fail-safe */ }

            var result = await Task.Run(_worktreeService.Remove);
            if (result.Success) _shell.ShowNotification(result.Message);
            else                _shell.ShowError(result.Message);
        }
        catch (Exception ex) { _shell.ShowError($"Worktree removal failed: {ex.Message}"); }
        finally { busy.Dispose(); }   // clears the busy mark → the browser refreshes / re-homes
    }

    /// <summary>The confirmation body shown for an unmerged / dirty / broken worktree — spells out exactly
    /// what is lost so the user can make an informed call.</summary>
    internal static string BuildRemovalPrompt(GitWorktreeInfo wt)
    {
        if (wt.IsBroken)
            return $"Remove broken worktree remnant '{wt.DisplayName}'?\n\n" +
                   "Its .git link is dangling — the linked repository is gone, so this can't be opened as a " +
                   "worktree. Removing deletes the leftover folder. This cannot be undone.";

        var sb = new StringBuilder($"Permanently remove worktree '{wt.DisplayName}'?\n\n");

        if (!wt.IsMerged)
            sb.Append(wt.MergeTargetBranch is { } t
                ? $"• Branch '{wt.Branch}' is not merged into {t}.\n"
                : $"• Branch '{wt.Branch}' has no known merge target.\n");

        if (!wt.HasUpstream)
            sb.Append("• The branch has never been pushed — its commits exist only here.\n");
        else if (!wt.IsPushed)
            sb.Append($"• {wt.AheadOfRemote} commit(s) are not on the remote and will be lost.\n");

        if (wt.HasUncommittedChanges)
            sb.Append($"• {wt.StagedCount + wt.ModifiedCount} uncommitted change(s) will be lost.\n");

        sb.Append("\nThis deletes the folder");
        sb.Append(wt.IsDetached ? "." : $" and the branch '{wt.Branch}'.");
        sb.Append(" This cannot be undone.");
        return sb.ToString();
    }

    // ── Branch switching ──────────────────────────────────────────────────

    private bool CanSwitchBranch(string? target) =>
        !IsBusy && !string.IsNullOrEmpty(target) && !string.Equals(target, BranchName, StringComparison.Ordinal);

    /// <summary>Checks out a local branch, then refreshes. A failed checkout is swallowed — the refresh puts
    /// the displayed branch back to whatever the repo actually has.</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private async Task SwitchBranchAsync(string? targetBranch)
    {
        if (targetBranch is null || !CanSwitchBranch(targetBranch)) return;

        IsBusy     = true;
        BranchName = targetBranch;

        try
        {
            await Task.Run(() =>
            {
                using var repo = new Repository(_folderPath);
                if (repo.Branches[targetBranch] is { } branch) Commands.Checkout(repo, branch);
            });
        }
        catch { /* the refresh below reverts the display on failure */ }
        finally { IsBusy = false; }

        await RefreshAsync();
    }

    // ── Pull ──────────────────────────────────────────────────────────────

    private bool CanPull() => !IsBusy;

    /// <summary>Fast-forward-only pull of the current branch, reporting the outcome in the result line. When
    /// the credential manager has nothing stored (or the token is stale) the user is offered a one-shot token
    /// capture and the pull is retried.</summary>
    [RelayCommand(CanExecute = nameof(CanPull))]
    private async Task PullAsync()
    {
        IsBusy      = true;
        PullCaption = "···";

        // Fetch + merge run off the UI thread: LibGit2Sharp invokes the credentials provider synchronously on
        // the fetching thread and that spawns `git credential fill`, which would freeze the window.
        var (outcome, message) = await Task.Run(RunPull);

        if (outcome == PullOutcome.AuthFailed && await TryCredentialFallbackAsync() is { } retried)
            (outcome, message) = retried;

        PullCaption = "Pull";
        IsBusy      = false;
        SetActionResult(message, outcome is PullOutcome.Ok or PullOutcome.UpToDate);

        await RefreshAsync();
    }

    /// <summary>Opens the repo and pulls with the credential-manager-backed provider. Pure background work —
    /// no UI access — so it is safe inside <see cref="Task.Run(Action)"/>.</summary>
    private (PullOutcome Outcome, string Message) RunPull()
    {
        try
        {
            using var repo = new Repository(_folderPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now)
                ?? new Signature(Environment.UserName, $"{Environment.UserName}@local", DateTimeOffset.Now);

            var options = new PullOptions
            {
                FetchOptions = new FetchOptions { CredentialsProvider = _credHelper.Provider },
                MergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly }
            };

            return Commands.Pull(repo, signature, options).Status switch
            {
                MergeStatus.UpToDate                                  => (PullOutcome.UpToDate, "Already up to date"),
                MergeStatus.FastForward or MergeStatus.NonFastForward => (PullOutcome.Ok, "Pull complete"),
                var status                                            => (PullOutcome.Failed, $"Pull failed: {status}")
            };
        }
        catch (Exception ex) when (IsAuthFailure(ex)) { return (PullOutcome.AuthFailed, $"Pull failed: {ex.Message}"); }
        catch (Exception ex)                          { return (PullOutcome.Failed,     $"Pull failed: {ex.Message}"); }
    }

    /// <summary>Fallback when no credential is stored: prompt for a token, write it into the system store
    /// keyed by the remote URL, then retry the pull once. Null when there's nothing to retry (no remote, or
    /// the user cancelled).</summary>
    private async Task<(PullOutcome, string)?> TryCredentialFallbackAsync()
    {
        var remotes   = _git.GetRemotes();
        var remoteUrl = (remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.FirstOrDefault())?.Url;
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var host  = Uri.TryCreate(remoteUrl, UriKind.Absolute, out var u) ? u.Host : remoteUrl;
        var token = await PromptAsync("Git access token", $"Access token for {host}");
        if (string.IsNullOrWhiteSpace(token)) return null;

        // Bitbucket repository/project/workspace access tokens authenticate with the fixed username
        // "x-token-auth"; the token is the password. Store it so later pulls resolve natively.
        await Task.Run(() => _credHelper.Approve(remoteUrl, new GitCredential("x-token-auth", token.Trim())));

        return await Task.Run(RunPull);
    }

    private Task<string?> PromptAsync(string title, string label)
    {
        var tcs = new TaskCompletionSource<string?>();
        _shell.ShowPrompt(title, label, string.Empty,
            onConfirm: value => tcs.TrySetResult(value),
            onCancel:  ()    => tcs.TrySetResult(null));
        return tcs.Task;
    }

    /// <summary>True for a fetch/pull failure caused by authentication (missing/invalid credentials), so the
    /// caller can offer the token-capture fallback rather than treating it as a hard error.</summary>
    internal static bool IsAuthFailure(Exception ex)
        => ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("credential",     StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("401")
        || ex.Message.Contains("403");

    /// <summary>Publishes the transient outcome line. The view owns its fade-out (an animation, not state).</summary>
    internal void SetActionResult(string message, bool success)
    {
        ActionResult        = message;
        ActionResultIsError = !success;
    }

    // ── Open in an external git manager ───────────────────────────────────

    /// <summary>Launches the external git GUI configured in options on this folder. Hidden entirely when none
    /// is configured, so an unset path is a no-op rather than an error.</summary>
    [RelayCommand]
    private void OpenGitManager()
    {
        if (_options.GitManagerPath is not { Length: > 0 } appPath || string.IsNullOrWhiteSpace(appPath)) return;
        try { Process.Start(new ProcessStartInfo(appPath, $"\"{_folderPath}\"") { UseShellExecute = true }); }
        catch { /* an unlaunchable path is the user's to fix in options */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Humanised age of a commit — "just now", "3h ago", "2mo ago".</summary>
    internal static string FormatTimeAgo(DateTimeOffset when)
    {
        var d = DateTimeOffset.Now - when;
        if (d.TotalMinutes < 1)   return "just now";
        if (d.TotalMinutes < 60)  return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours   < 24)  return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays    < 30)  return $"{(int)d.TotalDays}d ago";
        if (d.TotalDays    < 365) return $"{(int)(d.TotalDays / 30)}mo ago";
        return $"{(int)(d.TotalDays / 365)}y ago";
    }

    // ── AI surface (merged into the file browser's context + tools by IViewletAiSurface) ──────────
    // Read-only: the user can pull, switch branch and remove a worktree; the AI can only read. Mutating git
    // is a separate approval-gated layer, not yet built.

    /// <summary>The live repo line shown in the AI's context. Null (rather than a throw) for a broken or
    /// mid-operation repo, so one bad folder can't break the whole context.</summary>
    public string? GetContext()
    {
        // Wording lives in GitContextFormatter so it's unit-testable without a repo or this view-model.
        try { return GitContextFormatter.Describe(_git.GetStatus(), _worktreeService.GetInfo()); }
        catch { return null; }
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new GitStatusTool(_git),
        new GitLogTool(_git),
        new GitDiffTool(_git),
        new GitBranchesTool(_git),
        new GitShowTool(_git),
        new GitRemotesTool(_git),
    ];
}
