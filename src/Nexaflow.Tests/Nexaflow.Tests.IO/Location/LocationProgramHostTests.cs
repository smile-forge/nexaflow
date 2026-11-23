using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A runtime's file system handing a program to the host holding a folder: a folder on a location with a shell gets its
/// host, which takes the file system's paths and hands the program the host's own, and runs it through the session pool;
/// a folder anywhere else gets none, so its caller runs the program on this computer.
/// </summary>
[TestClass]
[CoversNode("io-locations-routing")]
public sealed class LocationProgramHostTests
{
    private const string Root = "::memory-nas";

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    private MemoryLocationHost _host = null!;
    private LocationSessionPool _pool = null!;
    private LocationSet _set = null!;
    private LocationFileSystem _files = null!;

    private void Mount(Func<ProgramRun, ProgramExit>? programs)
    {
        _host = new MemoryLocationHost("nas", TimeSpan.Zero) { Programs = programs };
        _host.With(nodes => nodes["/home/a:b"] = nodes["/home"] = new MemoryLocationHost.Node(true, [], DateTime.Now));
        var registry = new LocationProviderRegistry();
        registry.Register(new MemoryLocationProvider(_host));
        _pool  = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set   = _pool.Acquire([new ConnectionProfile("memory-nas", "memory", "NAS", "nas")]);
        _files = new LocationFileSystem(VirtualFileSystem.Instance);
        _files.RegisterLocation(_set.Mounts[0]);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_pool is null) return;
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }

    [TestMethod]
    public async Task AProgramRunsOnTheHost_InTheFolderTheHostNames_WithItsArgumentsAsGiven()
    {
        Mount(run => new ProgramExit(0, "one\ntwo\n", string.Empty));
        var host  = _files.GetProgramHost($@"{Root}\home\a%3Ab") ?? throw new AssertFailedException("a location with a shell has a host");
        var lines = new List<string>();

        var exit = await host.RunProgramAsync(
            new ProgramRun("git", ["log", "--format=%H %s", "it's $HOME"], $@"{Root}\home\a%3Ab") { Environment = new Dictionary<string, string> { ["A"] = "1" } },
            new SyncLines(lines), CancellationToken.None);

        Assert.AreEqual("NAS", host.Label);
        Assert.IsTrue(exit.Succeeded);
        CollectionAssert.AreEqual(new[] { "one", "two" }, lines);
        Assert.IsTrue(_host.Runs.TryDequeue(out var ran));
        Assert.AreEqual("/home/a:b", ran.WorkingDirectory, "the folder is handed over as the host names it, its name decoded");
        CollectionAssert.AreEqual(new[] { "log", "--format=%H %s", "it's $HOME" }, ran.Arguments.ToArray());
        Assert.AreEqual("1", ran.Environment!["A"]);
    }

    [TestMethod]
    public void HostPaths_RoundTripTheFileSystemsPaths_AndAPathElsewhereHasNone()
    {
        Mount(_ => new ProgramExit(0, "", ""));
        var host = _files.GetProgramHost(Root)!;

        Assert.AreEqual("/home/a:b/notes.md", host.HostPathOf($@"{Root}\home\a%3Ab\notes.md"));
        Assert.AreEqual("/", host.HostPathOf(Root));
        Assert.AreEqual($@"{Root}\home\a%3Ab\notes.md", host.PathOnLocation("/home/a:b/notes.md"));
        Assert.IsNull(host.HostPathOf(@"C:\Users"));
    }

    [TestMethod]
    public async Task AFolderOffTheLocation_IsRefused_AndAMissingFolderIsNotFound()
    {
        Mount(_ => new ProgramExit(0, "", ""));
        var host = _files.GetProgramHost(Root)!;

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await host.RunProgramAsync(new ProgramRun("git", [], @"C:\Users"), null, CancellationToken.None));
        var missing = await Assert.ThrowsExactlyAsync<LocationException>(
            async () => await host.RunProgramAsync(new ProgramRun("git", [], $@"{Root}\nowhere"), null, CancellationToken.None));
        Assert.AreEqual(LocationError.NotFound, missing.Error);
    }

    [TestMethod]
    public void AFolderOnThisComputer_OrOnALocationWithNoShell_HasNoHost()
    {
        Mount(programs: null);

        Assert.IsNull(_files.GetProgramHost(TestTemp.Root), "this computer's programs are the caller's to run");
        Assert.IsNull(_files.GetProgramHost(Root), "a location without a shell runs nothing");
    }

    /// <summary>Lines as they are reported, on the reporting thread, so the test reads them as soon as the run returns.</summary>
    private sealed class SyncLines(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }
}
