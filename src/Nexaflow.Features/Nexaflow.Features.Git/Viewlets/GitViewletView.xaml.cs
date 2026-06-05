using LibGit2Sharp;
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
    private readonly GitOptions         _options;
    private readonly string             _folderPath;
    private readonly IViewletController _controller;
    private readonly GitService         _git;

    private string       _currentBranch = string.Empty;
    private List<string> _localBranches = [];

    // Colours used in the inline status line — resolved from the active theme; throw (no silent literal
    // fallback) if a token is missing so a mis-themed reference surfaces immediately.
    private static Brush ThemeBrush(string key)
        => Application.Current?.Resources[key] as Brush
           ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");
    private static Brush StagedBrush   => ThemeBrush("SuccessBrush");
    private static Brush ModifiedBrush => ThemeBrush("WarningBrush");
    private static Brush ErrorBrush    => ThemeBrush("DangerBrush");

    public GitViewletView(GitOptions options, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _options    = options;
        _folderPath = folderPath;
        _controller = controller;
        _git        = new GitService(folderPath);

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
        try
        {
            var info = await Task.Run(_git.GetStatus);
            ApplyInfo(info);
        }
        catch (RepositoryNotFoundException)
        {
            BranchName.Text = "not a repo";
            StatusLine.Text = string.Empty;
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

    private async void PullButton_Click(object sender, RoutedEventArgs e)
    {
        PullButton.IsEnabled = false;
        PullButton.Content   = "···";
        using var repo = new Repository(_folderPath);

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now) ?? new Signature(
        Environment.UserName,
        $"{Environment.UserName}@local",
        DateTimeOffset.Now);
        var options = new PullOptions
        {
            MergeOptions = new MergeOptions
            {
                FastForwardStrategy = FastForwardStrategy.FastForwardOnly
            }
        };

        try
        {
            MergeResult result = Commands.Pull(repo, signature, options);
            switch (result.Status)
            {
                case MergeStatus.UpToDate:
                    ShowActionResult("Already up to date", true);
                    break;
                case MergeStatus.FastForward:
                case MergeStatus.NonFastForward:
                    ShowActionResult("Pull complete", true);
                    break;
                default:
                    ShowActionResult($"Pull failed: {result.Status}", false);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowActionResult($"Pull failed: {ex.Message}", false);
        }

        PullButton.Content = "Pull";
        PullButton.IsEnabled = true;

        /*
        bool success;
        try
        {
            int exitCode = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo("git", "pull")
                {
                    WorkingDirectory       = _folderPath,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };
                using var proc = Process.Start(psi)!;
                proc.WaitForExit(30_000);
                return proc.ExitCode;
            });
            success = exitCode == 0;
        }
        catch
        {
            success = false;
        }
        finally
        {
            PullButton.Content   = "Pull";
            PullButton.IsEnabled = true;
        }

        ShowPullResult(success ? "Pull complete" : "Pull failed", success);
        */

        await RefreshAsync();
    }

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
            var s  = _git.GetStatus();
            var sb = new StringBuilder($"Git: on '{s.Branch}'");
            if (s.Upstream is not null && (s.Ahead is > 0 || s.Behind is > 0))
            {
                var ahead  = s.Ahead  is > 0 ? $"{s.Ahead}↑"  : null;
                var behind = s.Behind is > 0 ? $"{s.Behind}↓" : null;
                sb.Append($" ({string.Join(" ", new[] { ahead, behind }.Where(x => x != null))} vs {s.Upstream})");
            }
            sb.Append('.');

            var parts = new List<string>();
            if (s.StagedCount    > 0) parts.Add($"{s.StagedCount} staged");
            if (s.ModifiedCount  > 0) parts.Add($"{s.ModifiedCount} modified");
            if (s.UntrackedCount > 0) parts.Add($"{s.UntrackedCount} untracked");
            sb.Append(parts.Count == 0 ? " Working tree clean." : " " + string.Join(", ", parts) + ".");

            if (s.LastCommitHash is not null)
                sb.Append($" Last commit {s.LastCommitHash} \"{s.LastCommitSubject}\".");
            return sb.ToString();
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
