using LibGit2Sharp;
using Nexaflow.Features.Common.Viewlets;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Nexaflow.Features.Git.Viewlets;

public partial class GitViewletView : UserControl
{
    private readonly GitOptions          _options;
    private readonly string              _folderPath;
    private readonly IViewletController  _controller;

    private string       _currentBranch  = string.Empty;
    private List<string> _localBranches  = [];

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
        }
        catch
        {
            BranchName.Text = "error";
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

        var head       = repo.Head;
        var branchName = head.FriendlyName;

        var status           = repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = false });
        var uncommittedCount = status.Count(e => e.State != FileStatus.Unaltered);

        int? aheadBy  = head.IsTracking ? head.TrackingDetails.AheadBy  : null;
        int? behindBy = head.IsTracking ? head.TrackingDetails.BehindBy : null;

        var branches = repo.Branches
            .Where(b => !b.IsRemote)
            .Select(b => b.FriendlyName)
            .OrderBy(n => n)
            .ToList();

        return new GitRepoInfo(branchName, uncommittedCount, aheadBy, behindBy, branches);
    }

    private void ApplyInfo(GitRepoInfo info)
    {
        _currentBranch = info.BranchName;
        _localBranches = info.LocalBranches;

        BranchName.Text = info.BranchName;

        if (info.UncommittedCount > 0)
        {
            UncommittedText.Text          = info.UncommittedCount.ToString();
            UncommittedBadge.Visibility   = Visibility.Visible;
            UncommittedBadge.ToolTip      = $"{info.UncommittedCount} uncommitted file{(info.UncommittedCount == 1 ? "" : "s")}";
        }
        else
        {
            UncommittedBadge.Visibility = Visibility.Collapsed;
        }

        var aheadParts = new List<string>(2);
        if (info.AheadBy  is > 0) aheadParts.Add($"↑{info.AheadBy}");
        if (info.BehindBy is > 0) aheadParts.Add($"↓{info.BehindBy}");

        if (aheadParts.Count > 0)
        {
            AheadBehindText.Text       = string.Join(" ", aheadParts);
            AheadBehindText.Visibility = Visibility.Visible;
            AheadBehindText.ToolTip    = BuildTrackingTooltip(info);
        }
        else
        {
            AheadBehindText.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildTrackingTooltip(GitRepoInfo info)
    {
        var parts = new List<string>(2);
        if (info.AheadBy  is > 0) parts.Add($"{info.AheadBy} commit{(info.AheadBy  == 1 ? "" : "s")} ahead");
        if (info.BehindBy is > 0) parts.Add($"{info.BehindBy} commit{(info.BehindBy == 1 ? "" : "s")} behind");
        return string.Join(", ", parts);
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
                using var repo   = new Repository(_folderPath);
                var branch       = repo.Branches[targetBranch];
                if (branch != null) Commands.Checkout(repo, branch);
            });
        }
        catch { /* fall through to refresh which will show current state */ }

        await RefreshAsync();
    }

    // ── Pull ──────────────────────────────────────────────────────────────

    private async void PullButton_Click(object sender, RoutedEventArgs e)
    {
        PullButton.IsEnabled = false;
        var original = PullButton.Content;
        PullButton.Content = "···";

        try
        {
            await Task.Run(() =>
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
            });
        }
        catch { /* ignore launch/timeout errors */ }
        finally
        {
            PullButton.Content   = original;
            PullButton.IsEnabled = true;
        }

        await RefreshAsync();
    }

    // ── Open git manager ──────────────────────────────────────────────────

    private void GitManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var appPath = _options.GitManagerPath;
        if (string.IsNullOrWhiteSpace(appPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(appPath, $"\"{_folderPath}\"") { UseShellExecute = true });
        }
        catch { /* ignore launch errors */ }
    }
}
