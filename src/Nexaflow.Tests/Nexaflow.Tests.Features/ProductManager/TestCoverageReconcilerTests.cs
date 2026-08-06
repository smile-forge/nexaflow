using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The test-coverage cross-check: reconciling the declared-coverage manifest against the tree into
/// non-gating advisories, and the gating <c>MissingSnaplink</c> rule for a RequiresSnaplink concern.
/// </summary>
[TestClass]
public class TestCoverageReconcilerTests
{
    private static ProductState State(params (string Id, ConcernLink[] Concerns)[] nodes)
    {
        var state = new ProductState
        {
            Product = new ProductDocument
            {
                Concerns = [new ConcernDef { Name = "tests", IsDefault = true, RequiresSnaplink = true }]
            }
        };
        foreach (var (id, concerns) in nodes)
            state.Nodes[id] = new ProductNode { Title = id, Concerns = [.. concerns] };
        return state;
    }

    private static TestCoverageManifest Manifest(string nodeId, TestRef @ref) =>
        new() { Coverage = { [nodeId] = [@ref] } };

    private static TestRef Ref(string file, string cls, string? method = null) =>
        new() { Assembly = "Nexaflow.Tests.Features", Class = cls, Method = method, File = file };

    [TestMethod]
    [CoversNode("integrity-advisories")]
    public void Declared_but_unlinked_yields_an_addable_advisory()
    {
        var state = State(("video-subtitles", [new ConcernLink { Tag = "tests", Status = Status.Done }]));
        var manifest = Manifest("video-subtitles",
            Ref("src/Tests/VideoViewModelTests.cs", "Nexaflow.Tests.Features.Video.VideoViewModelTests"));

        var report = TestCoverageReconciler.Reconcile(state, manifest);

        var advisory = report.Advisories.Single();
        Assert.AreEqual(CoverageAdvisoryKind.DeclaredButUnlinked, advisory.Kind);
        Assert.IsTrue(advisory.CanAdd);
        var proposed = advisory.ToSnaplink();
        // The proposed snaplink uses the SIMPLE class name — that is what the validator matches — and is
        // 'done' because it points at a real, compiled test (proof, not an intention).
        Assert.AreEqual("VideoViewModelTests", proposed.Class);
        Assert.AreEqual(Status.Done, proposed.Status);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void An_already_present_link_produces_no_advisory()
    {
        var existing = new Snaplink { Type = "code", Doc = "src/Tests/VideoViewModelTests.cs", Class = "VideoViewModelTests" };
        var state = State(("video-subtitles",
            [new ConcernLink { Tag = "tests", Status = Status.Done, Snaplinks = [existing] }]));
        var manifest = Manifest("video-subtitles",
            Ref("src/Tests/VideoViewModelTests.cs", "Nexaflow.Tests.Features.Video.VideoViewModelTests"));

        Assert.AreEqual(0, TestCoverageReconciler.Reconcile(state, manifest).Advisories.Count);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_declared_id_absent_from_the_tree_is_a_non_addable_unknown_node()
    {
        var state = State(("real-node", [new ConcernLink { Tag = "tests", Status = Status.Should }]));
        var manifest = Manifest("ghost-node", Ref("src/Tests/GhostTests.cs", "GhostTests"));

        var advisory = TestCoverageReconciler.Reconcile(state, manifest).Advisories.Single();
        Assert.AreEqual(CoverageAdvisoryKind.UnknownNode, advisory.Kind);
        Assert.IsFalse(advisory.CanAdd);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_null_manifest_yields_no_advisories()
    {
        var state = State(("n", [new ConcernLink { Tag = "tests", Status = Status.Done }]));
        Assert.IsTrue(TestCoverageReconciler.Reconcile(state, null).IsClean);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void Done_requires_snaplink_concern_with_no_link_is_a_gating_missing_snaplink_issue()
    {
        var state = State(("unbacked", [new ConcernLink { Tag = "tests", Status = Status.Done }]));

        var report = SnaplinkValidator.Validate(state, ".");

        var issue = report.Issues.Single(i => i.Kind == IntegrityKind.MissingSnaplink);
        Assert.AreEqual("unbacked", issue.NodeId);
        Assert.AreEqual("tests", issue.Concern);
        Assert.IsFalse(report.IsClean);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_should_concern_or_a_backed_done_concern_is_not_flagged()
    {
        // A URL link (valid target) keeps this focused on the RequiresSnaplink rule — any snaplink satisfies it.
        var backed = new Snaplink { Type = "url", Target = "https://example.com" };
        var state = State(
            ("still-open", [new ConcernLink { Tag = "tests", Status = Status.Should }]),
            ("backed", [new ConcernLink { Tag = "tests", Status = Status.Done, Snaplinks = [backed] }]));

        var report = SnaplinkValidator.Validate(state, ".");

        Assert.IsFalse(report.Issues.Any(i => i.Kind == IntegrityKind.MissingSnaplink));
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_coversnode_id_absent_from_the_tree_is_a_gating_issue()
    {
        var state = State(("real-node", [new ConcernLink { Tag = "tests", Status = Status.Should }]));
        var manifest = Manifest("ghost-node", Ref("src/Tests/GhostTests.cs", "GhostTests", "Explodes"));

        var report = SnaplinkValidator.Validate(state, ".", null, manifest);

        var issue = report.Issues.Single(i => i.Kind == IntegrityKind.StaleCoverageNode);
        Assert.AreEqual("ghost-node", issue.NodeId);
        Assert.AreEqual(-1, issue.Index, "there is no link in the tree to repair — the fix is in the test");
        // The detail has to name the test, or the build failure is unactionable: the tree cannot point at it.
        StringAssert.Contains(issue.Detail, "GhostTests.Explodes");
        StringAssert.Contains(issue.Detail, "ghost-node");
        Assert.IsFalse(report.IsClean, "a stale [CoversNode] id must fail the release gate");
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_declared_id_that_exists_is_not_gated_even_with_no_link_back()
    {
        // The deliberate line between the two halves of the reconciliation: a LIVE node whose tests concern
        // has no snaplink back stays a non-gating advisory (the Integrity page's "Add link"), because it is a
        // bookkeeping gap rather than proof of breakage. Only the unknown-id half fails the build.
        var state = State(("live-node", [new ConcernLink { Tag = "tests", Status = Status.Should }]));
        var manifest = Manifest("live-node", Ref("src/Tests/LiveTests.cs", "LiveTests"));

        var report = SnaplinkValidator.Validate(state, ".", null, manifest);

        Assert.IsFalse(report.Issues.Any(i => i.Kind == IntegrityKind.StaleCoverageNode));
        Assert.AreEqual(CoverageAdvisoryKind.DeclaredButUnlinked,
            TestCoverageReconciler.Reconcile(state, manifest).Advisories.Single().Kind);
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void With_no_manifest_the_coverage_gate_is_skipped_not_passed()
    {
        // A clean CI checkout has no test-coverage.json (it is gitignored, derived state). Nothing is
        // claimed there, so nothing can be disproved — the gate must not invent issues, and equally the
        // overload without a manifest must not be read as "coverage checked and clean".
        var state = State(("real-node", [new ConcernLink { Tag = "tests", Status = Status.Should }]));

        Assert.IsFalse(SnaplinkValidator.Validate(state, ".", null, null)
            .Issues.Any(i => i.Kind == IntegrityKind.StaleCoverageNode));
        Assert.IsFalse(SnaplinkValidator.Validate(state, ".")
            .Issues.Any(i => i.Kind == IntegrityKind.StaleCoverageNode));
    }

    // ── the reverse check: a shipped assembly the tree does not know about ─────────────────────────

    /// <summary>A repo skeleton with one feature assembly on disk, and a features family node linking
    /// whichever csprojs the caller names. Enough for the filesystem-walking half of the validator.</summary>
    private static string RepoWith(params string[] assemblies)
    {
        var root = Path.Combine(Path.GetTempPath(), "nfi-proj-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var asm in assemblies)
        {
            var dir = Path.Combine(root, "src", "Nexaflow.Features", asm);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, asm + ".csproj"), "<Project />");
        }
        return root;
    }

    private static ProductState FamilyLinking(params string[] csprojDocs)
    {
        var state = new ProductState { Product = new ProductDocument { Concerns = [] } };
        state.Nodes["features"] = new ProductNode { Title = "Features", Children = ["feat"] };
        state.Nodes["feat"] = new ProductNode
        {
            Title = "A Feature",
            Snaplinks = [.. csprojDocs.Select(d => new Snaplink { Type = "code", Doc = d })]
        };
        return state;
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_shipped_assembly_no_node_links_is_a_gating_untracked_project()
    {
        var root = RepoWith("Nexaflow.Features.Ghost");
        try
        {
            var report = SnaplinkValidator.Validate(FamilyLinking(), root);

            var issue = report.Issues.Single(i => i.Kind == IntegrityKind.UnlinkedProject);
            StringAssert.Contains(issue.Detail, "Nexaflow.Features.Ghost");
            Assert.AreEqual("features", issue.NodeId, "the finding hangs off the family that should own it");
            Assert.IsFalse(report.IsClean);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("integrity-scan")]
    public void A_linked_assembly_and_the_exempt_shapes_are_not_flagged()
    {
        // Ghost is linked; Common is shared contracts; Compressed.Modern is a codec backend behind a feature.
        // The latter two are implementation detail of an existing node — demanding their own node would force
        // noise into the tree, so they are exempt (mirroring the ProductTreeCoverageTests guard).
        var root = RepoWith("Nexaflow.Features.Ghost", "Nexaflow.Features.Common", "Nexaflow.Features.Compressed.Modern");
        try
        {
            var report = SnaplinkValidator.Validate(
                FamilyLinking("src/Nexaflow.Features/Nexaflow.Features.Ghost/Nexaflow.Features.Ghost.csproj"), root);

            Assert.IsFalse(report.Issues.Any(i => i.Kind == IntegrityKind.UnlinkedProject));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
