using System;
using System.IO;
using System.Linq;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The catalog's cache-validity stamp. The case that matters is the one that shipped: two builds carrying the
/// same app version, where the old index was trusted verbatim and every page kind added since rendered as an
/// empty tab. The stamp has to notice the DLLs moved even when the version did not.
/// </summary>
[TestClass]
[NoCoverage("feature-discovery cache validity — infrastructure, no single product node")]
public class FeatureCatalogStampTests
{
    private const string Core = "Nexaflow.Core.dll";

    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"catalogstamp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        Write(Core, "core");
        Write("Nexaflow.Features.Alpha.dll", "alpha");
        Write("Nexaflow.Features.Beta.dll", "beta");
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    private System.Collections.Generic.List<FeatureFileStamp> Capture() =>
        FeatureCatalogStamp.Capture(_dir, Core);

    // ── What it captures ─────────────────────────────────────────────────────

    [TestMethod]
    public void Capture_CoversEveryFeatureAssemblyAndCore()
    {
        var names = Capture().Select(s => s.Name).ToList();

        CollectionAssert.AreEquivalent(
            new[] { Core, "Nexaflow.Features.Alpha.dll", "Nexaflow.Features.Beta.dll" },
            names,
            "Core carries registrations too and does not match the feature pattern — it must be stamped.");
    }

    [TestMethod]
    public void Capture_IsNameOrdered_SoEnumerationOrderCannotCauseAFalseRebuild()
    {
        var names = Capture().Select(s => s.Name).ToList();
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        CollectionAssert.AreEqual(sorted, names);
    }

    [TestMethod]
    public void UnchangedDirectory_Matches()
    {
        Assert.IsTrue(FeatureCatalogStamp.Matches(Capture(), Capture()));
    }

    // ── What must force a rescan ─────────────────────────────────────────────

    [TestMethod]
    public void RebuiltAssembly_DoesNotMatch()
    {
        // The shipped bug: same version, different DLL. The old index claimed the new build's page kinds
        // did not exist, and the shell rendered them as empty tabs.
        var before = Capture();
        Write("Nexaflow.Features.Alpha.dll", "alpha, now with another page kind");

        Assert.IsFalse(FeatureCatalogStamp.Matches(before, Capture()));
    }

    [TestMethod]
    public void SameSizeButRewritten_DoesNotMatch()
    {
        // A rebuild that happens to produce an identical length still moves the write time.
        var before = Capture();
        var path = Path.Combine(_dir, "Nexaflow.Features.Beta.dll");
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));

        Assert.IsFalse(FeatureCatalogStamp.Matches(before, Capture()));
    }

    [TestMethod]
    public void AddedAssembly_DoesNotMatch()
    {
        var before = Capture();
        Write("Nexaflow.Features.Gamma.dll", "gamma");

        Assert.IsFalse(FeatureCatalogStamp.Matches(before, Capture()));
    }

    [TestMethod]
    public void RemovedAssembly_DoesNotMatch()
    {
        var before = Capture();
        File.Delete(Path.Combine(_dir, "Nexaflow.Features.Beta.dll"));

        Assert.IsFalse(FeatureCatalogStamp.Matches(before, Capture()));
    }

    // ── What must never be trusted ───────────────────────────────────────────

    [TestMethod]
    public void UnstampedIndex_DoesNotMatch()
    {
        // An index written before stamping existed deserializes with an empty Files list. It describes an
        // unknown DLL set, so it has to be rebuilt rather than assumed current.
        Assert.IsFalse(FeatureCatalogStamp.Matches([], Capture()));
        Assert.IsFalse(FeatureCatalogStamp.Matches(null, Capture()));
    }

    [TestMethod]
    public void UnreadableDirectory_YieldsAStampThatMatchesNothing()
    {
        var missing = FeatureCatalogStamp.Capture(Path.Combine(_dir, "does-not-exist"), Core);

        Assert.AreEqual(0, missing.Count);
        Assert.IsFalse(FeatureCatalogStamp.Matches(missing, Capture()));
        Assert.IsFalse(FeatureCatalogStamp.Matches(missing, missing),
            "a stamp that could not be captured must never validate a cache.");
    }
}
