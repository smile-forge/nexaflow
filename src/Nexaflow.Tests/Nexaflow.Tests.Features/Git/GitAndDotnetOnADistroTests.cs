using System.IO;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dotnet.Models;
using Nexaflow.Features.Dotnet.Services;
using Nexaflow.Features.Git;
using Nexaflow.Features.Git.Services;
using Nexaflow.Features.Git.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// The Git and .NET viewlets in a real WSL distro: status, branch switching, pull and worktree removal are the distro's
/// own git, run over the location's host — so a script committed runnable reads clean and stays runnable — and a build is
/// the distro's own dotnet. Everything is the test's own, under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("git-viewlet")]
public sealed class GitAndDotnetOnADistroTests
{
    private static readonly IReadOnlyDictionary<string, string> Identity = new Dictionary<string, string>
    {
        ["GIT_AUTHOR_NAME"]     = "Nexaflow Test",
        ["GIT_AUTHOR_EMAIL"]    = "test@nexaflow.invalid",
        ["GIT_COMMITTER_NAME"]  = "Nexaflow Test",
        ["GIT_COMMITTER_EMAIL"] = "test@nexaflow.invalid",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["HISTFILE"]            = "/dev/null",
    };

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    private LocationSessionPool _pool = null!;
    private LocationSet _set = null!;
    private LocationFileSystem _files = null!;
    private IProgramHost _host = null!;
    private string _base = string.Empty;

    [TestInitialize]
    public async Task Setup()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        _pool  = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set   = _pool.Acquire([distros[0]]);
        _files = new LocationFileSystem(VirtualFileSystem.Instance);
        _files.RegisterLocation(_set.Mounts[0]);
        _host  = _files.GetProgramHost(_set.Mounts[0].Root) ?? throw new AssertFailedException("a distro runs programs");

        _base = $"/tmp/nexaflow-git-{Guid.NewGuid():N}";
        await _files.CreateDirectoryAsync(At(_base));
        if (!(await _host.RunProgramAsync(new ProgramRun("sh", ["-c", "command -v git"], At(_base)), null, CancellationToken.None)).Succeeded)
            Assert.Inconclusive($"{_host.Label} has no git.");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_files is null) return;
        try { await _files.DeleteAsync(At(_base), recursive: true); } catch { /* best effort */ }
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }

    private string At(string hostPath) => _host.PathOnLocation(hostPath);

    private async Task<ProgramExit> Run(string hostFolder, string program, params string[] args)
    {
        var exit = await _host.RunProgramAsync(new ProgramRun(program, args, At(hostFolder)) { Environment = Identity }, null, CancellationToken.None);
        Assert.IsTrue(exit.Succeeded, $"{program} {string.Join(' ', args)}: {exit.Errors}");
        return exit;
    }

    private Task<ProgramExit> Git(string hostFolder, params string[] args)
        => Run(hostFolder, "git", ["-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main", .. args]);

    /// <summary>A repository at <c>{base}/repo</c> on <c>main</c>, holding a text file and a script committed runnable.</summary>
    private async Task<string> Repository()
    {
        var repo = $"{_base}/repo";
        await _files.CreateDirectoryAsync(At(repo));
        await Git(repo, "init", "-q");
        await _files.WriteAllTextAsync(At($"{repo}/a.txt"), "alpha\n");
        await _files.WriteAllTextAsync(At($"{repo}/run.sh"), "#!/bin/sh\necho hi\n");
        await Run(repo, "chmod", "755", "run.sh");
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-q", "-m", "First");
        return repo;
    }

    private HostGitWorkingCopy Copy(string hostFolder) => new(_host, _files, At(hostFolder));

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    [CoversNode("git-status-line")]
    public async Task TheStatus_IsTheDistrosGit_SoARunnableScriptReadsClean_AndAnEditReadsModified()
    {
        var repo = await Repository();

        var clean = await Copy(repo).GetStatusAsync(CancellationToken.None);
        await _files.WriteAllTextAsync(At($"{repo}/a.txt"), "alpha, edited\n");
        var edited = await Copy(repo).GetStatusAsync(CancellationToken.None);

        Assert.AreEqual("main", clean.Branch);
        Assert.AreEqual(0, clean.StagedCount + clean.ModifiedCount + clean.UntrackedCount, "a script's mode is the distro's, so it reads clean");
        Assert.AreEqual("First", clean.LastCommitSubject);
        CollectionAssert.AreEqual(new[] { "a.txt" }, edited.Modified.Select(c => c.Path).ToArray());
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    [CoversNode("git-branch-picker")]
    public async Task SwitchingBranch_ChecksOutInTheDistro_AndTheScriptItWritesStaysRunnable()
    {
        var repo = await Repository();
        await Git(repo, "checkout", "-q", "-b", "feature");
        await _files.WriteAllTextAsync(At($"{repo}/run.sh"), "#!/bin/sh\necho feature\n");
        await Git(repo, "commit", "-q", "-am", "Feature");
        await Git(repo, "checkout", "-q", "main");

        await Copy(repo).SwitchBranchAsync("feature", CancellationToken.None);

        var status = await Copy(repo).GetStatusAsync(CancellationToken.None);
        var script = await _files.GetEntryAsync(At($"{repo}/run.sh"));
        Assert.AreEqual("feature", status.Branch);
        Assert.AreEqual("#!/bin/sh\necho feature\n", await _files.ReadAllTextAsync(At($"{repo}/run.sh"), Encoding.UTF8));
        Assert.IsTrue((script!.Mode & UnixFileMode.UserExecute) != 0, "git in the distro wrote the script with its mode");
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    [CoversNode("git-pull")]
    public async Task APull_FastForwardsFromTheDistrosRemote_ThenIsUpToDate()
    {
        var repo = await Repository();
        await Git(_base, "init", "-q", "--bare", "origin.git");
        await Git(repo, "remote", "add", "origin", $"{_base}/origin.git");
        await Git(repo, "push", "-q", "-u", "origin", "main");
        await Git(_base, "clone", "-q", "origin.git", "other");
        await _files.WriteAllTextAsync(At($"{_base}/other/b.txt"), "beta\n");
        await Git($"{_base}/other", "add", ".");
        await Git($"{_base}/other", "commit", "-q", "-m", "Second");
        await Git($"{_base}/other", "push", "-q");

        var pulled = await Copy(repo).PullAsync(CancellationToken.None);
        var again  = await Copy(repo).PullAsync(CancellationToken.None);

        Assert.AreEqual(GitPullOutcome.Ok, pulled.Outcome, pulled.Message);
        Assert.AreEqual(GitPullOutcome.UpToDate, again.Outcome, again.Message);
        Assert.IsTrue(await _files.ExistsAsync(At($"{repo}/b.txt")));
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    [CoversNode("git-worktree-remove")]
    public async Task RemovingAWorktree_RemovesItsFolderRegistrationAndBranch_InTheDistro()
    {
        var repo = await Repository();
        await Git(repo, "worktree", "add", "-q", "-b", "wt", $"{_base}/wt");

        var info   = await Copy($"{_base}/wt").GetWorktreeAsync(CancellationToken.None);
        var listed = await Copy(repo).GetWorktreesAsync(CancellationToken.None);
        var result = await Copy($"{_base}/wt").RemoveWorktreeAsync(CancellationToken.None);

        Assert.IsNotNull(info);
        Assert.AreEqual("wt", info.Branch);
        Assert.IsTrue(info.IsMerged, "a worktree branched at main's tip has nothing main lacks");
        Assert.AreEqual("wt", listed.Single().Branch);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(await _files.ExistsAsync(At($"{_base}/wt")), "the folder is gone");
        Assert.AreEqual(string.Empty, (await Git(repo, "branch", "--list", "wt")).Output.Trim(), "the branch it owned is gone");
        StringAssert.DoesNotMatch((await Git(repo, "worktree", "list", "--porcelain")).Output, new System.Text.RegularExpressions.Regex("/wt\\b"),
                                  "git no longer knows the worktree");
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    [CoversNode("git-viewlet")]
    public async Task TheViewlet_ReadsTheDistrosRepository_AndTheAssistantAsksTheDistrosGit()
    {
        var repo = await Repository();
        await _files.WriteAllTextAsync(At($"{repo}/a.txt"), "alpha, second\n");
        await Git(repo, "commit", "-q", "-am", "Second");
        var vm = new GitViewletViewModel(new GitOptions(), Substitute.For<IShellServices>(), _files, At(repo));

        await vm.RefreshAsync();
        var tools = vm.GetClientTools();
        var diff  = await tools.Single(t => t.Name == "git_diff").InvokeAsync(
            new System.Text.Json.Nodes.JsonObject { ["from"] = "HEAD~1", ["to"] = "HEAD", ["format"] = "name-only" }, CancellationToken.None);

        Assert.AreEqual("main", vm.BranchName);
        StringAssert.Contains(vm.LastCommitLine, "Second");
        StringAssert.Contains(vm.GetContext(), "main");
        CollectionAssert.AreEquivalent(new[] { "git_status", "git_diff", "git_worktrees" }, tools.Select(t => t.Name).ToArray(),
            "the in-process history tools cannot open a repository the distro's account owns");
        Assert.AreEqual("a.txt", diff.ModelText.Trim());
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    [CoversNode("dotnet-verb-build")]
    public async Task ABuild_IsTheDistrosDotnet_OrSaysTheDistroHasNone()
    {
        var project = $"{_base}/app";
        await _files.CreateDirectoryAsync(At(project));
        await _files.WriteAllTextAsync(At($"{project}/App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        await _files.WriteAllTextAsync(At($"{project}/Program.cs"), "System.Console.WriteLine(\"hi\");\n");
        var target = new DotnetTarget("App.csproj", At($"{project}/App.csproj"), IsSolution: false);
        bool hasDotnet = (await _host.RunProgramAsync(new ProgramRun("sh", ["-c", "command -v dotnet"], At(project)), null, CancellationToken.None)).Succeeded;

        var built = await DotnetCli.RunAsync("build", target, At(project), _host);

        if (!hasDotnet)
        {
            Assert.IsNotNull(built.StartError, "this PC's dotnet is never reached for");
            StringAssert.Contains(built.StartError, "dotnet");
            return;
        }
        Assert.IsTrue(built.Succeeded, built.Transcript);
        Assert.IsTrue(await _files.ExistsAsync(At($"{project}/bin")), "the build wrote its output in the distro");
        Assert.IsTrue((await DotnetCli.RunAsync("clean", target, At(project), _host)).Succeeded);
    }
}
