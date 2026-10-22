using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Git.ClientTools;
using Nexaflow.Features.Git.Services;
using Nexaflow.Features.Git.Services.Forge;
using Nexaflow.Visuals.Common.Localization;
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
    private readonly GitInsightService _insights;
    private readonly GitForgeClient _forge;

    /// <summary>How a pull ended — <see cref="PullOutcome.AuthFailed"/> is the one the token fallback retries.</summary>
    public enum PullOutcome { Ok, UpToDate, Failed, AuthFailed }

    /// <summary>Local branches offered by the branch picker; empty until the first refresh completes.</summary>
    public ObservableCollection<string> LocalBranches { get; } = [];

    /// <summary>The inline status line, in render order. Rebuilt by every refresh.</summary>
    public ObservableCollection<GitStatusSegment> StatusSegments { get; } = [];

    /// <summary>What the branch button reads: the checked-out branch, or a word for a detached HEAD or an
    /// unreadable repo.</summary>
    [ObservableProperty] private string _branchName = "···";

    /// <summary>The branch HEAD is on, as git names it; null while unknown, detached or unreadable. The picker
    /// compares against this, never <see cref="BranchName"/>, whose status words are display text.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchBranchCommand))]
    private string? _checkedOutBranch;

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
    [ObservableProperty] private string _pullCaption = Str.Get("Git.Viewlet.Pull");

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
        _insights        = new GitInsightService(folderPath);

        // The forge reuses the same stored credential as push/fetch — nothing extra for the user to configure.
        _forge = new GitForgeClient(url => _credHelper.Fill(url));
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
            LastCommitLine   = string.Empty;
            CheckedOutBranch = null;
            BranchName       = worktree is { IsBroken: true }
                ? Str.Get("Git.Viewlet.Branch.BrokenWorktree")
                : Str.Get("Git.Viewlet.Branch.NotARepo");
            if (worktree is not { IsBroken: true }) SetActionResult(Str.Get("Git.Viewlet.Result.GitFileBroken"), success: false);
        }
        catch (Exception ex)
        {
            BranchName       = Str.Get("Git.Viewlet.Branch.Error");
            CheckedOutBranch = null;
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
        BranchName       = info.IsHeadDetached ? Str.Get("Git.Viewlet.Branch.Detached") : info.Branch;
        CheckedOutBranch = info.IsHeadDetached ? null : info.Branch;

        LocalBranches.Clear();
        foreach (var b in info.LocalBranches) LocalBranches.Add(b);

        StatusSegments.Clear();
        if (info.StagedCount    > 0) StatusSegments.Add(new(Str.Format("Git.Status.StagedFormat",    info.StagedCount),    GitTone.Good));
        if (info.ModifiedCount  > 0) StatusSegments.Add(new(Str.Format("Git.Status.ModifiedFormat",  info.ModifiedCount),  GitTone.Caution));
        if (info.UntrackedCount > 0) StatusSegments.Add(new(Str.Format("Git.Status.UntrackedFormat", info.UntrackedCount), GitTone.Normal));

        if (info.Ahead is > 0 || info.Behind is > 0)
        {
            var parts = new[] { info.Ahead is > 0 ? $"↑{info.Ahead}" : null, info.Behind is > 0 ? $"↓{info.Behind}" : null };
            StatusSegments.Add(new(string.Join(" ", parts.Where(p => p is not null)), GitTone.Muted));
        }

        if (StatusSegments.Count == 0) StatusSegments.Add(new(Str.Get("Git.Status.Clean"), GitTone.Good));

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
            StatusSegments.Add(new(Str.Get("Git.Status.BrokenRemnant"), GitTone.Bad));
            WorktreeTooltip = Str.Get("Git.Worktree.Tip.Broken");
            return;
        }

        if (wt.MergeTargetBranch is { } target)
            StatusSegments.Add(new(wt.IsMerged ? Str.Format("Git.Status.MergedIntoFormat", target)
                                               : Str.Format("Git.Status.UnmergedVsFormat", target),
                                   wt.IsMerged ? GitTone.Good : GitTone.Caution));

        if (!wt.HasUpstream)   StatusSegments.Add(new(Str.Get("Git.Status.NoUpstream"), GitTone.Muted));
        else if (wt.IsPushed)  StatusSegments.Add(new(Str.Get("Git.Status.Pushed"), GitTone.Muted));
        else                   StatusSegments.Add(new(wt.AheadOfRemote > 0
                                                          ? Str.Format("Git.Status.UnpushedAheadFormat", wt.AheadOfRemote)
                                                          : Str.Get("Git.Status.Unpushed"),
                                                      GitTone.Caution));

        WorktreeTooltip = BuildWorktreeTooltip(wt);
    }

    internal static string BuildWorktreeTooltip(GitWorktreeInfo wt)
    {
        var sb = new StringBuilder(Str.Get("Git.Worktree.Tip.Linked")).Append('\n');
        sb.Append(Str.Format("Git.Worktree.Tip.BranchFormat", WorktreeBranchLabel(wt))).Append('\n');
        sb.Append(wt.MergeTargetBranch is { } t
            ? (wt.IsMerged ? Str.Format("Git.Worktree.Tip.MergedIntoFormat", t)
                           : Str.Format("Git.Worktree.Tip.NotMergedIntoFormat", t))
            : Str.Get("Git.Worktree.Tip.MergeTargetUnknown")).Append('\n');
        sb.Append(!wt.HasUpstream ? Str.Get("Git.Worktree.Tip.NoUpstream")
            : wt.IsPushed ? Str.Format("Git.Worktree.Tip.PushedToFormat", wt.Upstream)
            : Str.Format("Git.Worktree.Tip.CommitsNotOnFormat", wt.AheadOfRemote, wt.Upstream));
        if (wt.HasUncommittedChanges)
            sb.Append('\n').Append(Str.Format("Git.Worktree.Tip.UncommittedFormat", wt.StagedCount + wt.ModifiedCount));
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
            && !await _shell.ConfirmAsync(Str.Get("Git.Worktree.Remove.Title"), BuildRemovalPrompt(wt),
                                Str.Get("Git.Worktree.Remove.Confirm"), Str.Get("Git.Worktree.Remove.Cancel")))
            return;

        // Removal (quiescing the folder's other viewlets, deleting the folder) can take a while, so it runs off
        // the UI thread behind a busy mark — the browser shows "please wait" and re-homes when it clears.
        var busy = _shell.MarkFolderBusy(_folderPath, Str.Format("Git.Worktree.Remove.BusyFormat", wt.DisplayName));
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
        catch (Exception ex) { _shell.ShowError(Str.Format("Git.Worktree.Remove.FailedFormat", ex.Message)); }
        finally { busy.Dispose(); }   // clears the busy mark → the browser refreshes / re-homes
    }

    /// <summary>The confirmation body shown for an unmerged / dirty / broken worktree — spells out exactly
    /// what is lost so the user can make an informed call.</summary>
    internal static string BuildRemovalPrompt(GitWorktreeInfo wt)
    {
        if (wt.IsBroken)
            return Str.Format("Git.Worktree.Remove.BrokenPromptFormat", wt.DisplayName);

        var branch = WorktreeBranchLabel(wt);
        var sb = new StringBuilder(Str.Format("Git.Worktree.Remove.PromptFormat", wt.DisplayName)).Append("\n\n");

        if (!wt.IsMerged)
            sb.Append(wt.MergeTargetBranch is { } t
                ? Str.Format("Git.Worktree.Remove.NotMergedFormat", branch, t)
                : Str.Format("Git.Worktree.Remove.NoMergeTargetFormat", branch)).Append('\n');

        if (!wt.HasUpstream)
            sb.Append(Str.Get("Git.Worktree.Remove.NeverPushed")).Append('\n');
        else if (!wt.IsPushed)
            sb.Append(Str.Format("Git.Worktree.Remove.CommitsLostFormat", wt.AheadOfRemote)).Append('\n');

        if (wt.HasUncommittedChanges)
            sb.Append(Str.Format("Git.Worktree.Remove.ChangesLostFormat", wt.StagedCount + wt.ModifiedCount)).Append('\n');

        sb.Append('\n').Append(wt.IsDetached
            ? Str.Get("Git.Worktree.Remove.DeletesFolder")
            : Str.Format("Git.Worktree.Remove.DeletesFolderAndBranchFormat", branch));
        return sb.ToString();
    }

    /// <summary>The worktree's branch as a person reads it. <see cref="GitWorktreeInfo.Branch"/> carries git's
    /// invariant markers for a detached or unreadable worktree; the flags beside it decide which words to show.</summary>
    private static string WorktreeBranchLabel(GitWorktreeInfo wt) =>
        wt.IsBroken     ? Str.Get("Git.Worktree.UnknownBranch")
        : wt.IsDetached ? Str.Get("Git.Worktree.DetachedBranch")
        : wt.Branch;

    // ── Branch switching ──────────────────────────────────────────────────

    private bool CanSwitchBranch(string? target) =>
        !IsBusy && !string.IsNullOrEmpty(target) && !string.Equals(target, CheckedOutBranch, StringComparison.Ordinal);

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

        PullCaption = Str.Get("Git.Viewlet.Pull");
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
                MergeStatus.UpToDate                                  => (PullOutcome.UpToDate, Str.Get("Git.Pull.UpToDate")),
                MergeStatus.FastForward or MergeStatus.NonFastForward => (PullOutcome.Ok, Str.Get("Git.Pull.Complete")),
                MergeStatus.Conflicts                                 => (PullOutcome.Failed, Str.Get("Git.Pull.FailedConflicts")),
                var status                                            => (PullOutcome.Failed, Str.Format("Git.Pull.FailedFormat", status))
            };
        }
        catch (Exception ex) when (IsAuthFailure(ex)) { return (PullOutcome.AuthFailed, Str.Format("Git.Pull.FailedFormat", ex.Message)); }
        catch (Exception ex)                          { return (PullOutcome.Failed,     Str.Format("Git.Pull.FailedFormat", ex.Message)); }
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
        var token = await PromptAsync(Str.Get("Git.Pull.TokenPrompt.Title"), Str.Format("Git.Pull.TokenPrompt.LabelFormat", host));
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
        if (d.TotalMinutes < 1)   return Str.Get("Git.Commit.JustNow");
        if (d.TotalMinutes < 60)  return Str.Format("Git.Commit.MinutesAgoFormat", (int)d.TotalMinutes);
        if (d.TotalHours   < 24)  return Str.Format("Git.Commit.HoursAgoFormat", (int)d.TotalHours);
        if (d.TotalDays    < 30)  return Str.Format("Git.Commit.DaysAgoFormat", (int)d.TotalDays);
        if (d.TotalDays    < 365) return Str.Format("Git.Commit.MonthsAgoFormat", (int)(d.TotalDays / 30));
        return Str.Format("Git.Commit.YearsAgoFormat", (int)(d.TotalDays / 365));
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
        new GitTagsTool(_git),
        new GitFileAtTool(_git),
        new GitMergeBaseTool(_git),
        new GitContainsTool(_git),
        new GitBlameTool(_git),
        new GitSearchHistoryTool(_git),
        new GitRecoveryTool(_git),
        new GitWorktreesTool(_git),
        new GitCompareTool(_insights),
        new GitChangelogTool(_insights),
        new GitBranchAuditTool(_insights),
        new GitFindWorkTool(_insights),
        new GitFileHistoryTool(_insights),
        new GitPullRequestsTool(_git, _forge),
        new GitIssuesTool(_git, _forge),
    ];
}
