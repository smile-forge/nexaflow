using Nexaflow.Services.Initiatives.Cli.Building;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// The SDK's own MSBuild, driven from this process the way the resident process drives it: two projects restored and
/// built side by side on worker processes, one producing its assembly and the other an error that names its line.
/// </summary>
[TestClass]
[CoversNode("nfi-build")]
public class HostedBuildTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(TestTemp.Root, "nfi-build-" + Guid.NewGuid().ToString("N")[..8]);
        Write("Fine/Fine.csproj", Project);
        Write("Fine/Fine.cs", "public static class Fine { public static int Answer => 42; }");
        Write("Broken/Broken.csproj", Project);
        Write("Broken/Broken.cs", "public static class Broken {\n    public static int Answer => undefined;\n}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void EachEntry_BuildsToItsAssembly_OrSaysWhereItFailed()
    {
        using var pool = new BuildPool(new HostedMSBuild("nfi tests", nodes: 2, reuseNodes: false), BuildPool.Linger);
        var fine   = Path.Combine(_root, "Fine", "Fine.csproj");
        var broken = Path.Combine(_root, "Broken", "Broken.csproj");

        var report = pool.Build(new BuildOrder(_root, [fine, broken], ["Build"], new Dictionary<string, string>(), Restore: true),
                                CancellationToken.None);

        Assert.IsNull(report.Problem, report.Problem);
        Assert.IsFalse(report.Succeeded, "one entry failed, so the build did");

        var built = report.Projects.Single(p => p.Project == fine);
        Assert.IsTrue(built.Succeeded);
        Assert.AreEqual("Fine.dll", Path.GetFileName(built.Outputs.Single()));
        Assert.IsTrue(File.Exists(built.Outputs.Single()), "the output reported is the assembly on disk");

        Assert.IsFalse(report.Projects.Single(p => p.Project == broken).Succeeded);
        var error = report.Diagnostics.Single(d => d.Severity == "error");
        Assert.AreEqual(("Broken/Broken.cs", 2, "CS0103"), (error.File, error.Line, error.Code),
                        "an error names its file relative to the tree, its line, and its code");
    }

    [TestMethod]
    public void ADesignTimeBuild_HandsBackTheCompilerCommandLine()
    {
        using var pool = new BuildPool(new HostedMSBuild("nfi tests", nodes: 1, reuseNodes: false), BuildPool.Linger);
        var fine = Path.Combine(_root, "Fine", "Fine.csproj");
        Assert.IsTrue(pool.Build(new BuildOrder(_root, [fine], ["Restore"], new Dictionary<string, string>(), Restore: false),
                                 CancellationToken.None).Succeeded, "restored first, as a loaded project is");

        var (args, error) = new PooledDesignTimeBuilder(pool, _root).CommandLineOf(fine, CancellationToken.None);

        Assert.IsNull(error, error);
        Assert.IsTrue(args!.Any(a => a.EndsWith("Fine.cs", StringComparison.OrdinalIgnoreCase)), "the command line names the source it compiles");
        Assert.IsFalse(File.Exists(Path.Combine(_root, "Fine", "bin", "Debug", "net10.0", "Fine.dll")), "and nothing was compiled");
    }

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
