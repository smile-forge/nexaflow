using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// Programs run in the default distro over its helper, beside every terminal: found on a login shell's PATH, given their
/// arguments whole, started in the folder asked, their streams kept apart, and ended — with what they started — when
/// cancelled. Each test works in a folder of its own under <c>/tmp</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("io-locations-wsl")]
public sealed class WslProgramTests
{
    private WslDistro _distro = null!;
    private ILocationSession _session = null!;
    private string _folder = string.Empty;

    [TestInitialize]
    public async Task Setup()
    {
        _distro  = WslUnderTest.Distro();
        _session = await new WslLocationProvider().ConnectAsync(WslLocationProvider.ConnectionFor(_distro), null!, CancellationToken.None);
        _folder  = $"/tmp/nexaflow-programs-{Guid.NewGuid():N}";
        await _session.CreateDirectoryAsync(_folder, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_session is null) return;
        try { await _session.DeleteAsync(_folder, recursive: true, CancellationToken.None); } catch { /* best effort */ }
        await _session.DisposeAsync();
    }

    private ILocationShell Shell => _session.Shell ?? throw new AssertFailedException("a distro has a shell");

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AProgramRunsInTheFolder_WithItsArgumentsWhole_AndItsStreamsApart()
    {
        var lines = new List<string>();
        var exit  = await Shell.RunProgramAsync(
            new ProgramRun("sh", ["-c", "pwd; printf '%s\\n' \"$1\"; echo \"$NEXAFLOW_PROBE\"; echo oops >&2; exit 3", "sh", "it's $HOME \"quoted\""], _folder)
            {
                Environment = new Dictionary<string, string> { ["NEXAFLOW_PROBE"] = "laid over", ["HISTFILE"] = "/dev/null" },
            },
            new SyncLines(lines), CancellationToken.None);

        Assert.AreEqual(3, exit.ExitCode);
        Assert.AreEqual($"{_folder}\nit's $HOME \"quoted\"\nlaid over\n", exit.Output, "no shell read the argument");
        Assert.AreEqual("oops\n", exit.Errors);
        CollectionAssert.Contains(lines, "oops");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AProgramIsFoundOnALoginShellsPath_AndOneNotThereIsNotFound()
    {
        var git = await Shell.RunProgramAsync(new ProgramRun("sh", ["-c", "command -v git || true"], _folder), null, CancellationToken.None);
        if (git.Output.Trim().Length == 0) Assert.Inconclusive($"'{_distro.Name}' has no git.");

        var version = await Shell.RunProgramAsync(new ProgramRun("git", ["--version"], _folder), null, CancellationToken.None);
        StringAssert.StartsWith(version.Output, "git version");

        var missing = await Assert.ThrowsExactlyAsync<LocationException>(async () =>
            await Shell.RunProgramAsync(new ProgramRun("nexaflow-no-such-program", [], _folder), null, CancellationToken.None));
        Assert.AreEqual(LocationError.NotFound, missing.Error);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CancellingEndsTheProgramAndWhatItStarted()
    {
        var marker = $"{_folder}/still-running";
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Shell.RunProgramAsync(new ProgramRun("sh", ["-c", $"(sleep 5; touch '{marker}') & sleep 30"], _folder), null, cancel.Token));

        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "the run ends when it is cancelled");
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.IsNull(await _session.GetEntryAsync(marker, CancellationToken.None), "what the program started ended with it");
    }

    private sealed class SyncLines(List<string> lines) : IProgress<string>
    {
        public void Report(string value) { lock (lines) lines.Add(value); }
    }
}
