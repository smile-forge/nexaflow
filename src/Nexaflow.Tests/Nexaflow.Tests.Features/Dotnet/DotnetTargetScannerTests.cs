using System.IO;
using Nexaflow.Features.Dotnet.Services;

namespace Nexaflow.Tests.Features.Dotnet;

[TestClass]
public class DotnetTargetScannerTests
{
    private readonly List<string> _temp = [];

    private string TempDir(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexadotnet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "<Project/>");
        _temp.Add(dir);
        return dir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void Scan_EmptyFolder_NoTargets()
        => Assert.AreEqual(0, DotnetTargetScanner.Scan(TempDir()).Count);

    [TestMethod]
    public void Scan_Project_FoundAsNonSolution()
    {
        var targets = DotnetTargetScanner.Scan(TempDir("Foo.csproj"));

        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("Foo.csproj", targets[0].DisplayName);
        Assert.IsFalse(targets[0].IsSolution);
    }

    [TestMethod]
    public void Scan_SolutionAndProject_SolutionListedFirst()
    {
        var targets = DotnetTargetScanner.Scan(TempDir("Bar.sln", "Foo.csproj"));

        Assert.AreEqual(2, targets.Count);
        Assert.IsTrue(targets[0].IsSolution, "solutions should be listed before projects");
        Assert.AreEqual("Bar.sln", targets[0].DisplayName);
    }
}
