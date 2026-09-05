using System.Collections.Generic;
using System.IO;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// Whether a stored coverage manifest still describes the assemblies on disk.
/// <para>
/// The manifest is reflected out of built test DLLs, so it is only ever as current as the last build — and
/// nothing used to notice when it stopped being. A rescan is cheap and silent, so the only thing these
/// assert is that the question is answered correctly: rescan when the build has moved, and — just as
/// important — <em>do not</em> rescan when it has not, because a check that always says "stale" costs
/// seconds on every validate and is quickly ignored.
/// </para>
/// </summary>
[TestClass]
[CoversNode("nfi-scan-tests")]
public class CoverageManifestFreshnessTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nfi-freshness-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temp dir that will not go is the OS's problem, not the test's */ }
    }

    private string WriteFakeAssembly(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Provenance as the collector records it, for an assembly that is on disk right now.</summary>
    private ScannedAssemblyRef Provenance(string path)
    {
        var info = new FileInfo(path);
        return new ScannedAssemblyRef
        {
            Path         = Path.GetRelativePath(_root, path).Replace('\\', '/'),
            Size         = info.Length,
            WriteTimeUtc = info.LastWriteTimeUtc.ToString("o")
        };
    }

    [TestMethod]
    public void AnUntouchedBuildNeedsNoRescan()
    {
        var dll = WriteFakeAssembly("A.dll", "one");
        var manifest = new TestCoverageManifest { Assemblies = [Provenance(dll)] };

        Assert.IsFalse(TestCoverageCollector.NeedsRescan(manifest, [dll], _root),
            "nothing changed, so re-reflecting every assembly would be pure cost on every validate");
    }

    [TestMethod]
    public void ARebuiltAssemblyNeedsARescan()
    {
        var dll = WriteFakeAssembly("A.dll", "one");
        var manifest = new TestCoverageManifest { Assemblies = [Provenance(dll)] };

        // A rebuild changes the content and the write time; either alone is enough to notice.
        File.WriteAllText(dll, "one but longer");

        Assert.IsTrue(TestCoverageCollector.NeedsRescan(manifest, [dll], _root),
            "the manifest describes an assembly that no longer exists in that form");
    }

    [TestMethod]
    public void AnAssemblyTheScanNeverSawNeedsARescan()
    {
        var seen = WriteFakeAssembly("A.dll", "one");
        var manifest = new TestCoverageManifest { Assemblies = [Provenance(seen)] };

        var added = WriteFakeAssembly("B.dll", "two");

        Assert.IsTrue(TestCoverageCollector.NeedsRescan(manifest, [seen, added], _root),
            "a test project that has appeared since the scan contributes declarations the manifest lacks");
    }

    [TestMethod]
    public void AManifestWrittenBeforeProvenanceExistedRefreshesOnce()
    {
        var dll = WriteFakeAssembly("A.dll", "one");

        // Written by an older nfi: coverage data, but nothing saying what it was read from.
        var manifest = new TestCoverageManifest { Assemblies = [] };

        Assert.IsTrue(TestCoverageCollector.NeedsRescan(manifest, [dll], _root),
            "an unstamped manifest cannot be shown to be current, so it is refreshed and self-heals");
    }

    [TestMethod]
    public void NoManifestIsNotStaleness()
    {
        var dll = WriteFakeAssembly("A.dll", "one");

        Assert.IsFalse(TestCoverageCollector.NeedsRescan(null, [dll], _root),
            "a clean CI checkout has no manifest at all — the coverage checks are skipped, not failed");
    }

    /// <summary>
    /// The regression that motivated the provenance: some assemblies in the discovered set never load (the
    /// discovery walks every .csproj under the tests directory, which includes dependency-only libraries).
    /// Recording only the ones that scanned left the stored set permanently smaller than the discovered set,
    /// so every future check read as stale and rescanned — seconds added to every single validate, forever.
    /// </summary>
    [TestMethod]
    public void AnAssemblyThatCouldNotBeScannedStillCountsAsSeen()
    {
        var scanned = WriteFakeAssembly("A.dll", "one");
        var skipped = WriteFakeAssembly("B.dll", "not a real assembly");

        var manifest = new TestCoverageManifest
        {
            ScannedAssemblies = 1,                                   // only A contributed declarations
            Assemblies        = [Provenance(scanned), Provenance(skipped)]   // but both were looked at
        };

        Assert.IsFalse(TestCoverageCollector.NeedsRescan(manifest, [scanned, skipped], _root),
            "an assembly that failed to load is still one the scan has seen at that exact version");
    }
}
