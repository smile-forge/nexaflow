using Nexaflow.Features.Dotnet.Models;
using Nexaflow.Features.Dotnet.Services;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dotnet;

/// <summary>
/// The .NET viewlet's commands on a location, against an in-memory location whose host answers <c>dotnet</c>: each verb
/// runs on the host, in the folder as the host names it and on the target as the host names it, and a host without
/// <c>dotnet</c> says so rather than reaching for this PC's.
/// </summary>
[TestClass]
[CoversNode("dotnet-verbs")]
public sealed class DotnetCliOnAHostTests
{
    private HostedLocation _box = null!;

    [TestInitialize]
    public void Setup()
    {
        _box = new HostedLocation();
        _box.File("/home/adam/app/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"/>");
    }

    [TestCleanup]
    public async Task Cleanup() => await _box.DisposeAsync();

    private static string Folder => HostedLocation.PathOf("/home/adam/app");

    private static DotnetTarget Target => new("App.csproj", HostedLocation.PathOf("/home/adam/app/App.csproj"), IsSolution: false);

    private sealed class Lines : IProgress<string>
    {
        public List<string> Seen { get; } = [];
        public void Report(string value) { lock (Seen) Seen.Add(value); }
    }

    [TestMethod]
    [CoversNode("dotnet-verb-build")]
    public async Task ABuild_RunsTheHostsDotnet_OnTheTargetAsTheHostNamesIt()
    {
        _box.Answer("dotnet", "build /home/adam/app/App.csproj", 0, "Restoring…\nBuild succeeded.\n", "warning CS0168\n");
        var lines = new Lines();

        var result = await DotnetCli.RunAsync("build", Target, Folder, _box.Files.GetProgramHost(Folder), lines);

        Assert.IsTrue(result.Succeeded);
        var run = _box.Runs.Single();
        Assert.AreEqual("/home/adam/app", run.WorkingDirectory);
        StringAssert.Contains(result.Output, "Build succeeded.");
        StringAssert.Contains(result.Output, "warning CS0168", "stdout and stderr read as one transcript");
        CollectionAssert.Contains(lines.Seen, "Build succeeded.");
    }

    [TestMethod]
    [CoversNode("dotnet-verb-run")]
    public async Task Run_NamesTheProject_AndTheOutdatedCheck_RunsInTheTargetsFolder()
    {
        _box.Answer("dotnet", "run --project /home/adam/app/App.csproj", 0, "Hello\n");
        _box.Answer("dotnet", "list /home/adam/app/App.csproj package --outdated", 0, "   > Newtonsoft.Json   12.0.1   12.0.1   13.0.3\n");
        var host = _box.Files.GetProgramHost(Folder);

        var ran     = await DotnetCli.RunAsync("run", Target, Folder, host);
        var outdated = await NugetUpdateChecker.CheckAsync(Target, host, CancellationToken.None);

        Assert.IsTrue(ran.Succeeded);
        Assert.IsTrue(outdated.Checked);
        Assert.AreEqual("Newtonsoft.Json 12.0.1 → 13.0.3",
                        outdated.Updates.Select(u => $"{u.Name} {u.Current} → {u.Latest}").Single());
        Assert.IsTrue(_box.Runs.All(r => r.WorkingDirectory == "/home/adam/app"));
    }

    [TestMethod]
    [CoversNode("dotnet-verb-build")]
    public async Task AHostWithoutDotnet_IsAStartError_SayingSo()
    {
        _box.Answer("dotnet", "build /home/adam/app/App.csproj",
                    _ => throw new LocationException(LocationError.NotFound, "dotnet", "dotnet: not on the PATH of a login shell"));

        var result = await DotnetCli.RunAsync("build", Target, Folder, _box.Files.GetProgramHost(Folder));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Failed to start dotnet: dotnet: not on the PATH of a login shell", result.Transcript);
    }
}
