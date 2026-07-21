using System.IO;
using LibGit2Sharp;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git.UI;

/// <summary>
/// The one UI journey for the Git viewlet: opens a real repository in the file browser and drives every
/// interactive control of the bar in a single pass, so the ~20 s app launch is paid once. Per-control
/// assertions live in <c>GitViewletViewModelTests</c>; this proves the wiring holds end to end.
/// <para>
/// Checks are soft (<see cref="UiJourneyTestBase.CheckPresent"/> / <see cref="UiJourneyTestBase.CheckInvoke"/>),
/// so one broken control still reports the rest.
/// </para>
/// Requires an interactive desktop session — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("git-ui")]
public class GitJourneyTests : UiJourneyTestBase
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);

    private string _folder = string.Empty;

    /// <summary>A real repository with one commit on a second branch, so the branch picker has a choice and
    /// the status/last-commit lines have something to render.</summary>
    private string RepoFolder()
    {
        if (_folder.Length > 0) return _folder;

        _folder = Path.Combine(Path.GetTempPath(), "nexagitui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        Repository.Init(_folder);
        File.WriteAllText(Path.Combine(_folder, "readme.md"), "# journey");

        using (var repo = new Repository(_folder))
        {
            Commands.Stage(repo, "*");
            repo.Commit("initial", Sig, Sig);
            repo.CreateBranch("feature/journey");
        }

        // An uncommitted change so the status line renders counts rather than "clean".
        File.WriteAllText(Path.Combine(_folder, "scratch.txt"), "untracked");
        return _folder;
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try
        {
            if (!Directory.Exists(_folder)) return;
            foreach (var f in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);   // .git/objects are read-only
            Directory.Delete(_folder, recursive: true);
        }
        catch { /* best-effort temp cleanup */ }
    }

    [TestMethod]
    [TestCategory("UI")]
    public void Git_Controls_RespondInOnePass()
    {
        NavigateFileBrowserTo(RepoFolder());

        Assert.IsNotNull(WaitForId("Git_Viewlet", 15),
            "The Git viewlet did not appear for a folder holding a repository.");

        // Displays — present and populated by the first refresh.
        CheckPresent("Status line", "Git_StatusLine");
        CheckPresent("Last commit line", "Git_LastCommitLine");

        // The branch picker opens a themed menu; invoking it must not tear the app down.
        CheckInvoke("Branch picker", "Git_BranchButton");
        Check("Branch picker stays interactive", () => WaitForId("Git_BranchButton", 3) is { IsEnabled: true });

        // Pull against a repo with no remote fails cleanly and reports in the result line rather than throwing.
        CheckInvoke("Pull", "Git_PullButton", seconds: 8);
        Check("Pull reported an outcome", () => WaitForId("Git_ActionResult", 6) is not null);
        Check("Pull re-enables", () => WaitForId("Git_PullButton", 8) is { IsEnabled: true });

        // A main checkout is not a worktree: the badge and Remove control are Collapsed, so they are absent
        // from the automation tree entirely — a direct lookup is the right probe, not a wait.
        Check("Worktree badge hidden on a main checkout",
              () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Git_WorktreeBadge")) is null);
        Check("Remove-worktree hidden on a main checkout",
              () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Git_RemoveWorktreeButton")) is null);

        // No git manager is configured in a fresh profile, so the Open button is hidden too.
        Check("Open-in-git-manager hidden without a configured application",
              () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Git_ManagerButton")) is null);

        AssertJourney();
    }
}
