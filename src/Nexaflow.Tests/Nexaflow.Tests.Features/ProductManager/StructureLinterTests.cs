using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

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
}
