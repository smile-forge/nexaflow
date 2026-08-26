using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

/// <summary>
/// Where the product store is allowed to write.
/// <para>
/// The rule is that live metadata — the tree, the integrity report, the coverage manifest, the knowledge
/// graph and its cache — stays inside <c>.product/</c>, which is gitignored working state. Exactly two
/// things write outside it: a <b>snapshot/export</b>, which is the point of the export folder and is meant to
/// be committed, and the one-time <c>.gitignore</c> edit that makes <c>.product/</c> ignored in the first
/// place.
/// </para>
/// <para>
/// This matters more now that the assistant can drive the store: <c>graph_build</c> reads across the whole
/// repo to parse source, and it would be easy to assume something that ranges that widely also writes
/// widely. It does not, and nothing here can — every write path is derived from the product root by
/// <see cref="ProductStore"/> itself, so no caller can redirect one. These tests pin that rather than
/// leaving it as something you have to read the class to know.
/// </para>
/// </summary>
[TestClass]
[CoversNode("data-model")]
public class ProductStoreWriteScopeTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-writescope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot() { try { Directory.Delete(_root, recursive: true); } catch { } }

    /// <summary>Every file under the root, relative and slash-normalised.</summary>
    private IReadOnlyList<string> FilesUnderRoot() =>
        [.. Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
                     .OrderBy(f => f, StringComparer.Ordinal)];

    // ── The live-state writes ─────────────────────────────────────────────────

    [TestMethod]
    public void EveryLiveWriteLandsInsideDotProduct()
    {
        var store = new ProductStore(_root);
        store.Initialize("ScopeTest");

        store.SaveTree(new Dictionary<string, ProductNode> { ["n"] = new() { Title = "Node" } });
        store.SaveIntegrity(new IntegrityReport());
        store.SaveTestCoverage(new TestCoverageManifest());
        store.SaveGraph(new KnowledgeGraph());
        store.SaveGraphCache(new GraphCache());

        var strays = FilesUnderRoot()
            .Where(f => !f.StartsWith(".product/", StringComparison.Ordinal) && f != ".gitignore")
            .ToList();

        Assert.AreEqual(0, strays.Count,
            "live product metadata belongs in .product/ — these landed elsewhere: " + string.Join(", ", strays));
    }

    [TestMethod]
    public void TheGraphAndItsCacheGoToDotProduct_HoweverWidelyTheBuildHadToRead()
    {
        var store = new ProductStore(_root);
        store.Initialize("ScopeTest");

        store.SaveGraph(new KnowledgeGraph());
        store.SaveGraphCache(new GraphCache());

        Assert.IsTrue(File.Exists(Path.Combine(_root, ".product", "graph.json")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, ".product", "graph-cache.json")));
        StringAssert.Contains(store.GraphFilePath.Replace('\\', '/'), "/.product/graph.json");
    }

    [TestMethod]
    public void TheStoreDerivesItsOwnDirectory_SoNoCallerCanRedirectAWrite()
    {
        // ProductStore takes a product root and computes .product itself; there is no overload that accepts
        // an output path. If one is ever added, this is the test that should stop it.
        var writeMethods = typeof(ProductStore).GetMethods()
            .Where(m => m.Name.StartsWith("Save", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(writeMethods.Count > 0, "precondition: the store has Save* methods");
        foreach (var m in writeMethods)
        {
            var pathish = m.GetParameters()
                .Where(p => p.ParameterType == typeof(string)
                         && p.Name is not null
                         && (p.Name.Contains("path", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("dir", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("file", StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .ToList();

            // The export writes legitimately take the export folder — see the next test.
            if (m.Name.Contains("Snapshot", StringComparison.Ordinal)
             || m.Name.Contains("Export", StringComparison.Ordinal)) continue;

            Assert.AreEqual(0, pathish.Count,
                $"{m.Name} takes a caller-supplied path ({string.Join(", ", pathish)}) — a live write must be "
              + "derived from the product root, not handed in");
        }
    }

    // ── The two deliberate exceptions ─────────────────────────────────────────

    [TestMethod]
    public void ASnapshotIsTheOneLiveWriteThatBelongsOutsideDotProduct()
    {
        var store = new ProductStore(_root);
        store.Initialize("ScopeTest");

        // Snapshots are the committed record — the whole point of the export folder is that it is *not*
        // gitignored, unlike everything in .product/.
        var exportPath = store.ExportPath("docs/product").Replace('\\', '/');

        Assert.IsFalse(exportPath.Contains("/.product/", StringComparison.Ordinal));
        StringAssert.Contains(exportPath, "docs/product");
    }

    [TestMethod]
    public void InitializeGitignoresDotProduct_WhichIsTheOtherWriteOutsideIt()
    {
        new ProductStore(_root).Initialize("ScopeTest");

        var gitignore = Path.Combine(_root, ".gitignore");
        Assert.IsTrue(File.Exists(gitignore), "the live folder has to be ignored or it lands in every commit");
        StringAssert.Contains(File.ReadAllText(gitignore), ".product/");
    }

    [TestMethod]
    public void GitignoringIsIdempotent_AndKeepsWhatWasAlreadyThere()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n");
        var store = new ProductStore(_root);

        store.EnsureGitignored();
        store.EnsureGitignored();

        var lines = File.ReadAllLines(Path.Combine(_root, ".gitignore"));
        Assert.AreEqual(1, lines.Count(l => l.Trim().TrimEnd('/') == ".product"),
                        "re-running must not stack duplicate entries");
        CollectionAssert.IsSubsetOf(new[] { "bin/", "obj/" }, lines, "and must not drop existing rules");
    }
}
