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
[CoversNode("product-snaplinks")]
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
    public void A_declared_id_absent_from_the_tree_is_a_non_addable_unknown_node()
    {
        var state = State(("real-node", [new ConcernLink { Tag = "tests", Status = Status.Should }]));
        var manifest = Manifest("ghost-node", Ref("src/Tests/GhostTests.cs", "GhostTests"));

        var advisory = TestCoverageReconciler.Reconcile(state, manifest).Advisories.Single();
        Assert.AreEqual(CoverageAdvisoryKind.UnknownNode, advisory.Kind);
        Assert.IsFalse(advisory.CanAdd);
    }

    [TestMethod]
    public void A_null_manifest_yields_no_advisories()
    {
        var state = State(("n", [new ConcernLink { Tag = "tests", Status = Status.Done }]));
        Assert.IsTrue(TestCoverageReconciler.Reconcile(state, null).IsClean);
    }

    [TestMethod]
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
}
