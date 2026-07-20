using LibGit2Sharp;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Git.ClientTools;
using Nexaflow.Features.Git.Services;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexaflow.Features.Git.Viewlets;

public partial class GitViewletView : UserControl, IViewletAiSurface
{
    private readonly GitOptions          _options;
    private readonly IShellServices      _shell;
    private readonly string              _folderPath;
    private readonly IViewletController  _controller;
    private readonly GitService          _git;
    private readonly GitWorktreeService  _worktreeService;
    private readonly GitCredentialHelper _credHelper;

    private string            _currentBranch = string.Empty;
    private List<string>      _localBranches = [];
    private GitWorktreeInfo?  _worktree;

    // Colours used in the inline status line — resolved from the active theme; throw (no silent literal
    // fallback) if a token is missing so a mis-themed reference surfaces immediately.
    private static Brush ThemeBrush(string key)
        => Application.Current?.Resources[key] as Brush
           ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");
    private static Brush StagedBrush   => ThemeBrush("SuccessBrush");
    private static Brush ModifiedBrush => ThemeBrush("WarningBrush");
    private static Brush ErrorBrush    => ThemeBrush("DangerBrush");

    public GitViewletView(GitOptions options, IShellServices shell, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _options         = options;
        _shell           = shell;
        _folderPath      = folderPath;
        _controller      = controller;
        _git             = new GitService(folderPath);
        _worktreeService = new GitWorktreeService(folderPath);
        _credHelper      = new GitCredentialHelper(folderPath);

        GitManagerButton.Visibility = string.IsNullOrWhiteSpace(options.GitManagerPath)
            ? Visibility.Collapsed
            : Visibility.Visible;

        Loaded += (_, _) => _ = RefreshAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        BranchButton.IsEnabled = false;
        PullButton.IsEnabled   = false;

        // Worktree info is fetched independently and is resilient — a remnant with a dangling .git link
        // returns a broken record rather than throwing, so the Remove button stays available to clean it up
        // even when the repo itself won't open below.
        GitWorktreeInfo? worktree = null;
        try { worktree = await Task.Run(_worktreeService.GetInfo); } catch { }

        try
        {
            var info = await Task.Run(_git.GetStatus);
            ApplyInfo(info);
        }
        catch (RepositoryNotFoundException)
        {
            StatusLine.Inlines.Clear();
            LastCommitLine.Text = string.Empty;
            BranchName.Text = worktree is { IsBroken: true } ? "broken worktree" : "not a repo";
            if (worktree is not { IsBroken: true })
                ShowActionResult("The .git file is broken", false);
        }
        catch(Exception ex)
        {
            BranchName.Text = "error";
            StatusLine.Text = string.Empty;
            ShowActionResult(ex.Message, false);
        }
        finally
        {
            ApplyWorktree(worktree);
            BranchButton.IsEnabled = true;
            PullButton.IsEnabled   = true;
        }
    }

    // ── Apply loaded info to UI ───────────────────────────────────────────

    private void ApplyInfo(GitStatus info)
    {
        _currentBranch = info.Branch;
        _localBranches = info.LocalBranches.ToList();

        BranchName.Text = info.Branch;

        // Status line — inline coloured runs
        StatusLine.Inlines.Clear();
        bool any = false;

        void AddRun(string text, Brush brush)
        {
            if (any) StatusLine.Inlines.Add(new Run("  ") { Foreground = (Brush)FindResource("TextMutedBrush") });
            StatusLine.Inlines.Add(new Run(text) { Foreground = brush });
            any = true;
        }

        if (info.StagedCount    > 0) AddRun($"{info.StagedCount} staged",    StagedBrush);
        if (info.ModifiedCount  > 0) AddRun($"{info.ModifiedCount} modified", ModifiedBrush);
        if (info.UntrackedCount > 0) AddRun($"{info.UntrackedCount} untracked", (Brush)FindResource("TextBrush"));

        if (info.Ahead is > 0 || info.Behind is > 0)
        {
            var ahead  = info.Ahead  is > 0 ? $"↑{info.Ahead}"  : null;
            var behind = info.Behind is > 0 ? $"↓{info.Behind}" : null;
            var parts  = new[] { ahead, behind }.Where(s => s != null);
            AddRun(string.Join(" ", parts), (Brush)FindResource("TextMutedBrush"));
        }

        if (!any)
        {
            StatusLine.Inlines.Add(new Run("clean") { Foreground = StagedBrush });
        }

        // Last commit line
        if (info.LastCommitHash != null)
        {
            var timeAgo = info.LastCommitWhen.HasValue ? FormatTimeAgo(info.LastCommitWhen.Value) : null;
            var msg     = info.LastCommitSubject?.Trim();
            var display = string.IsNullOrEmpty(msg)
                ? $"{info.LastCommitHash}  {timeAgo}"
                : $"{info.LastCommitHash}  {msg}  ·  {timeAgo}";
            LastCommitLine.Text = display;
        }
    }

    // ── Worktree state ────────────────────────────────────────────────────
    // Shown only when the folder is a linked worktree: a "worktree" badge, the merge/push state appended
    // to the status line, and the Remove control. ApplyInfo has already rebuilt StatusLine, so we append.

    private void ApplyWorktree(GitWorktreeInfo? wt)
    {
        _worktree = wt;

        var visible = wt is not null ? Visibility.Visible : Visibility.Collapsed;
        WorktreeBadge.Visibility        = visible;
        RemoveWorktreeButton.Visibility = visible;

        if (wt is null) return;

        void AddRun(string text, Brush brush)
        {
            StatusLine.Inlines.Add(new Run("  ") { Foreground = (Brush)FindResource("TextMutedBrush") });
            StatusLine.Inlines.Add(new Run(text) { Foreground = brush });
        }

        // A remnant: the repo won't open, so there's no merge/push state to show — just flag it for cleanup.
        if (wt.IsBroken)
        {
            AddRun("broken remnant — Remove to clean up", ErrorBrush);
            WorktreeBadge.ToolTip = $"Broken git worktree remnant\nIts .git link is dangling — the linked " +
                                    $"repository is gone.\nUse Remove to delete the leftover folder.";
            return;
        }

        // Merge state vs the mainline target.
        if (wt.MergeTargetBranch is { } target)
            AddRun(wt.IsMerged ? $"merged into {target}" : $"unmerged vs {target}",
                   wt.IsMerged ? StagedBrush : ModifiedBrush);

        // Remote (pushed) state.
        if (!wt.HasUpstream)
            AddRun("no upstream", (Brush)FindResource("TextMutedBrush"));
        else if (wt.IsPushed)
            AddRun("pushed", (Brush)FindResource("TextMutedBrush"));
        else
            AddRun(wt.AheadOfRemote > 0 ? $"unpushed ↑{wt.AheadOfRemote}" : "unpushed", ModifiedBrush);

        WorktreeBadge.ToolTip = BuildWorktreeTooltip(wt);
    }

    private static string BuildWorktreeTooltip(GitWorktreeInfo wt)
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

    private async void RemoveWorktreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_worktree is not { } wt) return;

        // A merged worktree with no uncommitted work is safe to drop (its commits live on in the mainline),
        // so it removes without a prompt. Anything unmerged or dirty confirms first.
        if (!wt.CanRemoveWithoutConfirmation)
        {
            var confirmed = await _shell.ConfirmAsync("Remove worktree?", BuildRemovalPrompt(wt),
                                                      "Remove", "Cancel");
            if (!confirmed) return;
        }

        RemoveWorktreeButton.IsEnabled = false;

        // Removal (quiescing the .NET scan, killing its dotnet tree, deleting the folder) can take a while, so
        // run it off the UI thread. Marking the folder busy makes the browser show a "please wait" panel and
        // refresh/re-home when it clears — the user can keep working and even queue more removals meanwhile.
        var busy = _shell.MarkFolderBusy(_folderPath, $"Removing worktree '{wt.DisplayName}'…");
        _ = RunRemovalAsync(busy);
    }

    private async Task RunRemovalAsync(IDisposable busy)
    {
        try
        {
            // Quiesce every viewlet on this folder first — e.g. the .NET viewlet's NuGet scan runs `dotnet`
            // with the folder as its working directory and would otherwise lock it against deletion.
            try { await _controller.QuiesceFolderAsync(); } catch { /* best-effort; removal is fail-safe anyway */ }

            var result = await Task.Run(_worktreeService.Remove);
            if (result.Success) _shell.ShowNotification(result.Message);
            else                _shell.ShowError(result.Message);
        }
        catch (Exception ex) { _shell.ShowError($"Worktree removal failed: {ex.Message}"); }
        finally { busy.Dispose(); }   // clears the busy mark → the browser refreshes / re-homes
    }

    /// <summary>The confirmation body shown for an unmerged / dirty / broken worktree — spells out exactly
    /// what is lost so the user can make an informed call.</summary>
    private static string BuildRemovalPrompt(GitWorktreeInfo wt)
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

        sb.Append($"\nThis deletes the folder");
        sb.Append(wt.IsDetached ? "." : $" and the branch '{wt.Branch}'.");
        sb.Append(" This cannot be undone.");
        return sb.ToString();
    }

    // ── Branch switching ──────────────────────────────────────────────────

    private void BranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_localBranches.Count == 0) return;

        var menu = new ContextMenu { PlacementTarget = BranchButton, Placement = PlacementMode.Bottom };

        foreach (var name in _localBranches)
        {
            var item = new MenuItem
            {
                Header    = name,
                IsChecked = string.Equals(name, _currentBranch, StringComparison.Ordinal)
            };
            var captured = name;
            item.Click += (_, _) => _ = SwitchBranchAsync(captured);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private async Task SwitchBranchAsync(string targetBranch)
    {
        if (string.Equals(targetBranch, _currentBranch, StringComparison.Ordinal)) return;

        BranchButton.IsEnabled = false;
        BranchName.Text        = targetBranch;

        try
        {
            await Task.Run(() =>
            {
                using var repo = new Repository(_folderPath);
                var branch     = repo.Branches[targetBranch];
                if (branch != null) Commands.Checkout(repo, branch);
            });
        }
        catch { /* refresh will revert display on failure */ }

        await RefreshAsync();
    }

    // ── Pull ──────────────────────────────────────────────────────────────

    private enum PullOutcome { Ok, UpToDate, Failed, AuthFailed }

    private async void PullButton_Click(object sender, RoutedEventArgs e)
    {
        PullButton.IsEnabled = false;
        PullButton.Content   = "···";

        // Fetch + merge run off the UI thread: LibGit2Sharp invokes the CredentialsProvider synchronously on
        // the fetching thread, and that spawns `git credential fill` — doing it on the UI thread would freeze
        // the window. Results marshal back onto the awaited (UI-thread) continuation.
        var (outcome, message) = await Task.Run(RunPull);

        // GCM had nothing stored (or the token is stale): offer to capture one, store it, and retry once.
        if (outcome == PullOutcome.AuthFailed)
        {
            var retried = await TryCredentialFallbackAsync();
            if (retried is { } r) (outcome, message) = r;
        }

        PullButton.Content   = "Pull";
        PullButton.IsEnabled = true;
        ShowActionResult(message, outcome is PullOutcome.Ok or PullOutcome.UpToDate);

        await RefreshAsync();
    }

    /// <summary>Opens the repo and pulls with the GCM-backed credentials provider. Pure background work —
    /// no UI access — so it is safe to run inside <see cref="Task.Run(Action)"/>.</summary>
    private (PullOutcome outcome, string message) RunPull()
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

            var result = Commands.Pull(repo, signature, options);
            return result.Status switch
            {
                MergeStatus.UpToDate                             => (PullOutcome.UpToDate, "Already up to date"),
                MergeStatus.FastForward or MergeStatus.NonFastForward => (PullOutcome.Ok, "Pull complete"),
                _                                                => (PullOutcome.Failed, $"Pull failed: {result.Status}")
            };
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {
            return (PullOutcome.AuthFailed, $"Pull failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (PullOutcome.Failed, $"Pull failed: {ex.Message}");
        }
    }

    /// <summary>Fallback when no credential is stored: prompt for a token, write it into the system store
    /// (GCM) keyed by the remote URL, then retry the pull once. Returns null if there's nothing to retry
    /// (no remote, or the user cancelled). Runs on the UI thread (the prompt is window-modal).</summary>
    private async Task<(PullOutcome, string)?> TryCredentialFallbackAsync()
    {
        var remotes   = _git.GetRemotes();
        var remoteUrl = (remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.FirstOrDefault())?.Url;
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var host  = Uri.TryCreate(remoteUrl, UriKind.Absolute, out var u) ? u.Host : remoteUrl;
        var token = await PromptAsync("Git access token", $"Access token for {host}");
        if (string.IsNullOrWhiteSpace(token)) return null;

        // Bitbucket repository/project/workspace access tokens authenticate with the fixed username
        // "x-token-auth"; the token is the password. Store it so subsequent pulls resolve natively via GCM.
        var cred = new GitCredential("x-token-auth", token.Trim());
        await Task.Run(() => _credHelper.Approve(remoteUrl, cred));

        return await Task.Run(RunPull);
    }

    private Task<string?> PromptAsync(string title, string label)
    {
        var tcs = new TaskCompletionSource<string?>();
        _shell.ShowPrompt(title, label, string.Empty,
            onConfirm: value => tcs.TrySetResult(value),
            onCancel:  () => tcs.TrySetResult(null));
        return tcs.Task;
    }

    /// <summary>True for a fetch/pull failure caused by authentication (missing/invalid credentials), so the
    /// caller can offer the token-capture fallback rather than treating it as a hard error.</summary>
    private static bool IsAuthFailure(Exception ex)
        => ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("credential",     StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("401")
        || ex.Message.Contains("403");

    private void ShowActionResult(string message, bool success)
    {
        // Cancel any in-progress animation
        PullResultText.BeginAnimation(OpacityProperty, null);

        PullResultText.Text       = message;
        PullResultText.Foreground = success ? (Brush)FindResource("TextMutedBrush") : ErrorBrush;
        PullResultText.Opacity    = 1.0;
        PullResultText.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation
        {
            From         = 1.0,
            To           = 0.0,
            BeginTime    = TimeSpan.FromSeconds(3.5),
            Duration     = new Duration(TimeSpan.FromSeconds(1.5)),
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) =>
        {
            PullResultText.Visibility = Visibility.Collapsed;
            PullResultText.Opacity    = 1.0;
        };
        PullResultText.BeginAnimation(OpacityProperty, fade);
    }

    // ── Open git manager ──────────────────────────────────────────────────

    private void GitManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var appPath = _options.GitManagerPath;
        if (string.IsNullOrWhiteSpace(appPath)) return;

        try { Process.Start(new ProcessStartInfo(appPath, $"\"{_folderPath}\"") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatTimeAgo(DateTimeOffset when)
    {
        var d = DateTimeOffset.Now - when;
        if (d.TotalMinutes < 1)   return "just now";
        if (d.TotalMinutes < 60)  return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours   < 24)  return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays    < 30)  return $"{(int)d.TotalDays}d ago";
        if (d.TotalDays    < 365) return $"{(int)(d.TotalDays / 30)}mo ago";
        return $"{(int)(d.TotalDays / 365)}y ago";
    }

    // ── IViewletAiSurface ─────────────────────────────────────────────────
    // Surfaces live repo state + read-only git tools to the file browser's AI context (see
    // IViewletAiSurface). Mutating git operations are a separate, approval-gated layer (not yet built).

    string? IViewletAiSurface.GetContext()
    {
        try
        {
            // Wording lives in GitContextFormatter so it's unit-testable without a repo or this UI control.
            return GitContextFormatter.Describe(_git.GetStatus(), _worktreeService.GetInfo());
        }
        catch
        {
            return null;   // a broken/mid-operation repo shouldn't break the whole AI context
        }
    }

    IReadOnlyList<IClientTool> IViewletAiSurface.GetClientTools() =>
    [
        new GitStatusTool(_git),
        new GitLogTool(_git),
        new GitDiffTool(_git),
        new GitBranchesTool(_git),
        new GitShowTool(_git),
        new GitRemotesTool(_git),
    ];
}
