using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

/// <summary>
/// The advisory modelling-rule checks behind <c>nfi lint</c> (docs/feature-tree-and-tests.md
/// §1–§4). The bar is the mirror of <see cref="SnaplinkValidatorTests"/>'s: this one must not cry wolf on a
/// correctly-modelled feature, because a noisy advisory gets ignored — so <see cref="AWellModelledFeature_LintsClean"/>
/// (built to the Text Viewer's shape) is the anchor test.
/// </summary>
[TestClass]
[NoCoverage("Advisory tree-convention linter for the headless CLI — tooling, not a product-tree node.")]
public class StructureLinterTests
{
    private static ConcernLink Concern(string tag, Status status = Status.Done, bool linked = false) => new()
    {
        Tag = tag,
        Status = status,
        Snaplinks = linked ? [new Snaplink { Type = "code", Doc = "src/Some/Test.cs", Class = "SomeTests" }] : null,
    };

    /// <summary>A feature modelled the way the doc says: the backbone, a journey on UI, a themed panel with no
    /// tests of its own, unit-tested leaves, and a behaviour under Functionality.</summary>
    private static ProductState WellModelled() => new()
    {
        Nodes = new Dictionary<string, ProductNode>
        {
            ["features"] = new() { Title = "Features", Children = ["feat"] },
            ["feat"] = new()
            {
                Title = "Feature", Parent = "features", Children = ["feat-ui", "feat-functionality", "feat-ai"],
                Concerns = [Concern("theming"), Concern("tests", linked: true), Concern("AI Ready")],
            },
            ["feat-ui"] = new()
            {
                Title = "UI", Parent = "feat", Children = ["feat-panel"],
                Concerns = [Concern("tests", linked: true)],   // the one journey
            },
            ["feat-panel"] = new()
            {
                Title = "Toolbar", Parent = "feat-ui", Children = ["feat-button"],
                Concerns = [Concern("theming")],               // panel: theming, no tests
            },
            ["feat-button"] = new()
            {
                Title = "Save", Parent = "feat-panel",
                Concerns = [Concern("theming"), Concern("tests", linked: true)],
            },
            ["feat-functionality"] = new() { Title = "Functionality", Parent = "feat", Children = ["feat-behaviour"] },
            ["feat-behaviour"] = new()
            {
                Title = "Search", Parent = "feat-functionality", Concerns = [Concern("tests", linked: true)],
            },
            ["feat-ai"] = new() { Title = "AI Integration", Parent = "feat", Children = ["feat-ai-context"] },
            ["feat-ai-context"] = new()
            {
                Title = "Context", Parent = "feat-ai", Concerns = [Concern("tests", linked: true)],
            },
        }
    };

    private static StructureLinter.Rule[] Rules(ProductState state, string? under = null) =>
        [.. StructureLinter.Lint(state, under).Select(f => f.Rule)];

    /// <summary>A manifest in which <paramref name="nodeId"/> is declared by <paramref name="count"/> tests,
    /// spread over <paramref name="files"/> files the way a leaf that has outgrown itself usually is.</summary>
    private static TestCoverageManifest Declaring(string nodeId, int count, int files = 1)
    {
        var m = new TestCoverageManifest { Generated = "2026-08-26T00:00:00Z" };
        m.Coverage[nodeId] =
        [
            .. Enumerable.Range(0, count).Select(i => new TestRef
            {
                Assembly = "Nexaflow.Tests.Example",
                File = $"src/Tests/Part{i % Math.Max(files, 1)}Tests.cs",
                Class = $"Part{i % Math.Max(files, 1)}Tests",
                Method = $"Behaviour{i}",
            }),
        ];
        return m;
    }

    // ── The anchor: no false positives on a correctly-modelled feature ─────────

    [TestMethod]
    public void AWellModelledFeature_LintsClean()
        => CollectionAssert.AreEqual(Array.Empty<StructureLinter.Rule>(), Rules(WellModelled()));

    // ── One rule at a time, each starting from the clean tree ─────────────────

    [TestMethod]
    public void AiReady_BelowTheFeatureRoot_IsFlagged()
    {
        var s = WellModelled();
        s.Nodes["feat-button"].Concerns!.Add(Concern("AI Ready"));

        var finding = StructureLinter.Lint(s).Single();
        Assert.AreEqual(StructureLinter.Rule.AiReadyBelowFeature, finding.Rule);
        Assert.AreEqual("feat-button", finding.NodeId);
    }

    [TestMethod]
    public void AiReady_OnTheFeatureRootItself_IsFine()
        => Assert.IsFalse(Rules(WellModelled()).Contains(StructureLinter.Rule.AiReadyBelowFeature));

    [TestMethod]
    public void APanelCarryingItsOwnTestsConcern_IsFlagged()
    {
        var s = WellModelled();
        s.Nodes["feat-panel"].Concerns!.Add(Concern("tests", linked: true));

        CollectionAssert.Contains(Rules(s), StructureLinter.Rule.ContainerHasTests);
    }

    [TestMethod]
    public void APanelWithoutTheming_IsFlagged_ButAStateNodeWithNoConcernsIsNot()
    {
        var s = WellModelled();
        s.Nodes["feat-panel"].Concerns = [Concern("docs")];        // has concerns, but no theming
        CollectionAssert.Contains(Rules(s), StructureLinter.Rule.PanelMissingTheming);

        var stateNode = WellModelled();
        stateNode.Nodes["feat-panel"].Concerns = null;             // pure structure — no concerns at all
        CollectionAssert.DoesNotContain(Rules(stateNode), StructureLinter.Rule.PanelMissingTheming);
    }

    [TestMethod]
    public void ALeafWithNoTestsConcern_IsFlagged_UnderUiAndUnderFunctionality()
    {
        var ui = WellModelled();
        ui.Nodes["feat-button"].Concerns = [Concern("theming")];
        CollectionAssert.Contains(Rules(ui), StructureLinter.Rule.LeafMissingTests);

        var func = WellModelled();
        func.Nodes["feat-behaviour"].Concerns = null;
        CollectionAssert.Contains(Rules(func), StructureLinter.Rule.LeafMissingTests);
    }

    [TestMethod]
    public void TestsDone_WithNoSnaplink_IsFlagged()
    {
        var s = WellModelled();
        s.Nodes["feat-button"].Concerns = [Concern("theming"), Concern("tests", Status.Done)];   // unlinked

        CollectionAssert.Contains(Rules(s), StructureLinter.Rule.TestsDoneWithoutSnaplink);
    }

    [TestMethod]
    public void TestsStillShould_WithNoSnaplink_IsNotFlagged()
    {
        // "not done yet" is an honest state, not drift — only a terminal status must name its test.
        var s = WellModelled();
        s.Nodes["feat-button"].Concerns = [Concern("theming"), Concern("tests", Status.Should)];

        CollectionAssert.DoesNotContain(Rules(s), StructureLinter.Rule.TestsDoneWithoutSnaplink);
    }

    [TestMethod]
    public void TestsShouldnt_NeedsANoteSayingWhy()
    {
        var s = WellModelled();
        s.Nodes["feat-button"].Concerns = [Concern("theming"), Concern("tests", Status.Shouldnt)];
        CollectionAssert.Contains(Rules(s), StructureLinter.Rule.ShouldntWithoutNote);

        s.Nodes["feat-button"].Note = "WPF ApplicationCommands forwarder — covered by the UI journey.";
        CollectionAssert.DoesNotContain(Rules(s), StructureLinter.Rule.ShouldntWithoutNote);
    }

    [TestMethod]
    public void AUiNodeWithoutTheJourney_IsFlagged()
    {
        var s = WellModelled();
        s.Nodes["feat-ui"].Concerns = null;

        CollectionAssert.Contains(Rules(s), StructureLinter.Rule.UiMissingJourney);
    }

    // ── Backbone: report an unconverted feature once, not once per leaf ────────

    [TestMethod]
    public void AFeatureWithNoBackbone_IsReportedOnce_AndSuppressesTheRoleRules()
    {
        var s = new ProductState
        {
            Nodes = new Dictionary<string, ProductNode>
            {
                ["features"] = new() { Title = "Features", Children = ["old"] },
                ["old"] = new() { Title = "Legacy", Parent = "features", Children = ["old-panel"] },
                ["old-panel"] = new() { Title = "Bar", Parent = "old" },   // no concerns, no tests
            }
        };

        var findings = StructureLinter.Lint(s);
        Assert.AreEqual(1, findings.Count, "one to-do, not a wall of leaf findings");
        Assert.AreEqual(StructureLinter.Rule.MissingBackbone, findings[0].Rule);
        Assert.AreEqual("old", findings[0].NodeId);
        StringAssert.Contains(findings[0].Detail, "UI");
    }

    [TestMethod]
    public void MissingBackbone_NamesOnlyTheAbsentParts()
    {
        var s = WellModelled();
        s.Nodes["feat"].Children.Remove("feat-ai");

        var finding = StructureLinter.Lint(s).Single(f => f.Rule == StructureLinter.Rule.MissingBackbone);
        StringAssert.Contains(finding.Detail, "AI Integration");
        Assert.IsFalse(finding.Detail.Contains("Functionality"), "Functionality is present — don't name it");
    }

    // ── Scoping ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Under_LimitsTheLintToOneFeature()
    {
        var s = WellModelled();
        s.Nodes["features"].Children.Add("other");
        s.Nodes["other"] = new ProductNode { Title = "Other", Parent = "features" };   // no backbone

        Assert.AreEqual(1, StructureLinter.Lint(s).Count, "unscoped: the other feature is reported");
        Assert.AreEqual(0, StructureLinter.Lint(s, "feat").Count, "scoped to the clean feature: nothing");
        Assert.AreEqual(1, StructureLinter.Lint(s, "features").Count, "scoped to the container: all features");
    }

    [TestMethod]
    public void ATreeWithNoFeaturesContainer_YieldsNothing()
        => Assert.AreEqual(0, StructureLinter.Lint(new ProductState
        {
            Nodes = new Dictionary<string, ProductNode> { ["x"] = new() { Title = "X" } }
        }).Count);

    // ── Leaf granularity, read back from the tests ────────────────────────────

    [TestMethod]
    public void ALeafThatManyTestsDeclare_IsFlaggedAsUnderModelled()
    {
        var findings = StructureLinter.Lint(
            WellModelled(), null, Declaring("feat-behaviour", StructureLinter.MaxTestsPerLeaf + 1, files: 3));

        var f = findings.Single();
        Assert.AreEqual(StructureLinter.Rule.LeafCoveredByTooManyTests, f.Rule);
        Assert.AreEqual("feat-behaviour", f.NodeId);
        StringAssert.Contains(f.Detail, "across 3 files");
        StringAssert.Contains(f.Detail, "add-node", "the finding has to say what to do about it");
    }

    [TestMethod]
    public void ALeafExactlyAtTheLimit_IsLeftAlone()
        => CollectionAssert.AreEqual(
            Array.Empty<StructureLinter.Rule>(),
            (StructureLinter.Rule[])[.. StructureLinter.Lint(
                WellModelled(), null, Declaring("feat-behaviour", StructureLinter.MaxTestsPerLeaf)).Select(f => f.Rule)],
            "the threshold is inclusive — a rule that fires AT the documented limit reads as off-by-one");

    [TestMethod]
    public void AContainerThatManyTestsDeclare_IsNotFlagged()
    {
        // A panel accumulates its children's tests by design. Flagging it would be flagging the tree for
        // being correctly nested, which is how an advisory earns a reputation for noise.
        var findings = StructureLinter.Lint(
            WellModelled(), null, Declaring("feat-panel", StructureLinter.MaxTestsPerLeaf * 4));

        Assert.AreEqual(0, findings.Count(f => f.Rule == StructureLinter.Rule.LeafCoveredByTooManyTests));
    }

    // ── Granularity, read from the links ──────────────────────────────────────

    [TestMethod]
    public void ANodeCarryingTooManySnaplinks_IsFlagged()
    {
        var s = WellModelled();
        var link = () => new Snaplink { Type = "code", Doc = "src/Some/File.cs", Class = "SomeType" };
        s.Nodes["feat-behaviour"].Snaplinks = [.. Enumerable.Range(0, StructureLinter.MaxSnaplinksPerNode + 1).Select(_ => link())];

        var f = StructureLinter.Lint(s).Single();

        Assert.AreEqual(StructureLinter.Rule.TooManySnaplinks, f.Rule);
        Assert.AreEqual("feat-behaviour", f.NodeId);
        StringAssert.Contains(f.Detail, "split it");
    }

    [TestMethod]
    public void SnaplinksOnConcerns_CountTowardsTheSameTotal()
    {
        // A node can carry the same weight through its concerns as on itself, and splitting the count
        // between the two places would let either half stay quietly under the line.
        var s = WellModelled();
        var link = () => new Snaplink { Type = "code", Doc = "src/Some/File.cs", Class = "SomeType" };
        var node = s.Nodes["feat-behaviour"];
        node.Snaplinks = [link(), link()];
        node.Concerns![0].Snaplinks = [.. Enumerable.Range(0, StructureLinter.MaxSnaplinksPerNode).Select(_ => link())];

        var f = StructureLinter.Lint(s).Single();

        Assert.AreEqual(StructureLinter.Rule.TooManySnaplinks, f.Rule);
        StringAssert.Contains(f.Detail, $"{StructureLinter.MaxSnaplinksPerNode + 2} snaplinks");
    }

    [TestMethod]
    public void ANodeExactlyAtTheSnaplinkLimit_IsLeftAlone()
    {
        var s = WellModelled();
        var link = () => new Snaplink { Type = "code", Doc = "src/Some/File.cs", Class = "SomeType" };
        var node = s.Nodes["feat-behaviour"];

        // Topped up to exactly the limit, counting the one its 'tests' concern already carries — writing
        // this as a flat MaxSnaplinksPerNode on the node alone puts the real total one over, which is the
        // mistake the rule exists to catch and an easy one to make in a test too.
        var existing = node.Concerns!.Sum(c => c.Snaplinks?.Count ?? 0);
        Assert.AreEqual(1, existing, "the well-modelled fixture backs its 'tests' concern with one link");
        node.Snaplinks = [.. Enumerable.Range(0, StructureLinter.MaxSnaplinksPerNode - existing).Select(_ => link())];

        CollectionAssert.AreEqual(Array.Empty<StructureLinter.Rule>(), Rules(s),
            "the threshold is inclusive, like MaxTestsPerLeaf — the two rules must not disagree about that");
    }

    [TestMethod]
    public void WithNoManifest_TheRuleSitsOut_RatherThanGuessing()
    {
        // The manifest is derived and gitignored, so absent is the normal case on a clean checkout. Every
        // other rule must still run.
        CollectionAssert.AreEqual(Array.Empty<StructureLinter.Rule>(), Rules(WellModelled()));

        var s = WellModelled();
        s.Nodes["feat-behaviour"].Concerns = null;
        CollectionAssert.AreEqual(
            new[] { StructureLinter.Rule.LeafMissingTests },
            (StructureLinter.Rule[])[.. StructureLinter.Lint(s, null, coverage: null).Select(f => f.Rule)]);
    }
}
