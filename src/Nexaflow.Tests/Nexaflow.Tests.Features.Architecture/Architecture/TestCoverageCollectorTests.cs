using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Cli;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The collector reads the built test DLLs and their portable PDBs to produce the coverage manifest. Its
/// whole value is the source file it recovers per declaration: without one the Integrity page can say
/// "this test claims a node" but cannot offer the verifiable <c>code</c> snaplink that would back it.
/// </summary>
[TestClass]
[NoCoverage("test-coverage tooling — the manifest it builds maps to no single product node")]
public class TestCoverageCollectorTests
{
    /// <summary>
    /// An <c>async</c> test compiles to a stub that starts a state machine, and the PDB hangs the sequence
    /// points off the generated <c>MoveNext</c> — so "what document is this method in" answers nothing for
    /// exactly the tests most likely to be UI or IO. It hid for a long time behind the collector's fallback
    /// to the class's file, which is usually the same file and so usually right; it only surfaced where a
    /// class's tests were <em>all</em> async, because then the fallback had no method with debug info to
    /// probe either and the rows landed with an empty path.
    /// <para>
    /// This guard lives here rather than beside the collector because only this project's output holds
    /// every suite's DLL — and the classes that exposed the bug (an all-async surface suite) were in
    /// Viewers, not in whichever project happened to host the test.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Every_declaration_resolves_a_source_file_including_the_async_ones()
    {
        var dlls = FeatureTestSuites.Assemblies()
            .Select(a => Path.Combine(AppContext.BaseDirectory, a.GetName().Name + ".dll"))
            .Where(File.Exists)
            .ToList();
        Assert.IsTrue(dlls.Count >= 3, $"expected the suite DLLs beside the guards, found {dlls.Count}");

        var manifest = TestCoverageCollector.Collect(dlls, RepoRoot.Locate(), "guard");

        var rows = manifest.Coverage.SelectMany(kv => kv.Value.Select(r => (Node: kv.Key, Ref: r))).ToList();
        Assert.IsTrue(rows.Count > 0, "the scan found no [CoversNode] declarations at all");
        Assert.IsTrue(rows.Any(r => r.Ref.Method is not null),
            "no method-level declarations were scanned — this guard would prove nothing");

        var unresolved = rows.Where(r => string.IsNullOrEmpty(r.Ref.File))
                             .Select(r => $"{r.Ref.Class}.{r.Ref.Method ?? "(class)"} → {r.Node}")
                             .OrderBy(x => x, StringComparer.Ordinal)
                             .ToList();

        Assert.AreEqual(0, unresolved.Count,
            "Every [CoversNode] declaration must resolve to a source file, or the Integrity page cannot "
            + $"offer to link it. Unresolved:\n  {string.Join("\n  ", unresolved)}");
    }
}
