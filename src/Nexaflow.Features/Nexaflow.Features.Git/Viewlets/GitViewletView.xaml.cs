using LibGit2Sharp;
using Nexaflow.Features.Common.Viewlets;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexaflow.Features.Git.Viewlets;

public partial class GitViewletView : UserControl
{
    private readonly GitOptions         _options;
    private readonly string             _folderPath;
    private readonly IViewletController _controller;

    private string       _currentBranch = string.Empty;
    private List<string> _localBranches = [];

    // Colours used in the inline status line
    private static readonly Brush StagedBrush    = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));  // green
    private static readonly Brush ModifiedBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));  // amber
    private static readonly Brush ErrorBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0x30));  // red

    public GitViewletView(GitOptions options, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _options    = options;
        _folderPath = folderPath;
        _controller = controller;

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
            var info = await Task.Run(LoadRepoInfo);
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

    private GitRepoInfo LoadRepoInfo()
    {
        using var repo = new Repository(_folderPath);


        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked     = true,
            RecurseUntrackedDirs = false,
            IncludeIgnored       = false,
            ExcludeSubmodules    = true
        });

        const FileStatus stagedMask   = FileStatus.NewInIndex | FileStatus.ModifiedInIndex
                                      | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex
                                      | FileStatus.TypeChangeInIndex;
        const FileStatus modifiedMask = FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir
                                      | FileStatus.TypeChangeInWorkdir | FileStatus.RenamedInWorkdir;

        int staged    = status.Count(e => (e.State & stagedMask)   != 0);
        int modified  = status.Count(e => (e.State & modifiedMask) != 0 && (e.State & stagedMask) == 0);
        int untracked = status.Count(e => e.State == FileStatus.NewInWorkdir);

        var head = repo.Head;
        Branch? tracked = null;

        try
        {
            tracked = head.TrackedBranch;
        }
        catch (LibGit2Sharp.InvalidSpecificationException)
        {
            // no valid upstream configured
        }

        int? aheadBy = null;
        int? behindBy = null;
        if (tracked != null)
        {
            var details = head.TrackingDetails;


            aheadBy = head.IsTracking ? head.TrackingDetails.AheadBy : null;
            behindBy = head.IsTracking ? head.TrackingDetails.BehindBy : null;
        }

        var tip     = head.Tip;
        var hash    = tip?.Sha?[..7];
        var message = tip?.MessageShort;
        DateTimeOffset? when = tip?.Author.When;

        var branches = repo.Branches
            .Where(b => !b.IsRemote)
            .Select(b => b.FriendlyName)
            .OrderBy(n => n)
            .ToList();

        /*
        FetchOptions options = new FetchOptions
        {
            TagFetchMode = TagFetchMode.None,
            Prune = true
        };
        string log = "fetch --dry-run";
        var remote = head.RemoteName != null ? repo.Network.Remotes[head.RemoteName] : null;
        var refSpecs = remote?.FetchRefSpecs.Select(x => x.Specification) ?? Array.Empty<string>();
        Commands.Fetch(repo, head.RemoteName, refSpecs, options, log);
        */
        return new GitRepoInfo(head.FriendlyName, staged, modified, untracked,
                               aheadBy, behindBy, hash, message, when, branches);
    }

    // ── Apply loaded info to UI ───────────────────────────────────────────

    private void ApplyInfo(GitRepoInfo info)
    {
        _currentBranch = info.BranchName;
        _localBranches = info.LocalBranches;

        BranchName.Text = info.BranchName;

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

        if (info.AheadBy  is > 0 || info.BehindBy is > 0)
        {
            var ahead  = info.AheadBy  is > 0 ? $"↑{info.AheadBy}"  : null;
            var behind = info.BehindBy is > 0 ? $"↓{info.BehindBy}" : null;
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
            var msg     = info.LastCommitMessage?.Trim();
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
}
