using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.State;

/// <summary>
/// What a <c>stateDiagram</c> block's stages write into its tree: each composite state gathered with what is written in it, the
/// dot each <c>[*]</c> is, the names that are a composite state's rather than a state's, what styles each state, and what its front
/// matter asks for.
/// </summary>
[TestClass]
[CoversNode("state-diagram")]
public class StateStagesTests
{
    /// <summary>The dot each <c>[*]</c> is, in the order they are written.</summary>
    private static (string Id, bool Stop)[] Dots(string source) =>
        [.. MermaidStaged.Read(source).SelfAndDescendants().OfType<MarkerNode>().Select(marker => (marker.Id, marker.Stop))];

    [TestMethod, TestCategory("Unit")]
    public void EveryEdgeInOneScopeIsTheSameDot_CalledWhatAClassLineStylesItBy()
    {
        CollectionAssert.AreEqual(new[] { ("start", false), ("end", true), ("end", true) },
                                  Dots("stateDiagram-v2\n  [*] --> one\n  one --> [*]\n  two --> [*]"),
                                  "the dot a transition leaves is where it starts, and the one it reaches where it stops");
    }

    [TestMethod, TestCategory("Unit")]
    public void ACompositeStateHasDotsOfItsOwn_AndEachOfItsRegionsDoes()
    {
        var dots = Dots("stateDiagram-v2\n  [*] --> A\n  state A {\n    [*] --> one\n    one --> [*]\n    --\n    [*] --> two\n  }");

        CollectionAssert.AreEqual(new[] { "start", "start@0", "end@0", "start@0#2" }, dots.Select(dot => dot.Id).ToArray());
    }

    [TestMethod, TestCategory("Unit")]
    public void ANameACompositeStateIsCalledByIsThatComposite_WrittenAboveItOrBelow()
    {
        var tree = MermaidStaged.Read("stateDiagram-v2\n  [*] --> A\n  state A {\n    one\n  }\n  state B {\n    two\n  }\n  A --> B\n  note right of B : this");
        var named = tree.SelfAndDescendants().OfType<GroupReferenceNode>()
                        .Select(reference => (reference.Children.First(child => child.Kind == MermaidKinds.Name).Words()!.Text, reference.Group))
                        .ToArray();

        CollectionAssert.AreEqual(new[] { ("A", 0), ("A", 0), ("B", 1), ("B", 1) }, named,
                                  "the transition above it, the one below them both, and the note about one");
    }

    [TestMethod, TestCategory("Unit")]
    public void EachCompositeStateIsGatheredWithWhatIsWrittenInIt()
    {
        const string source = "stateDiagram-v2\n  outside\n  state A {\n    inner\n    state B {\n      deeper\n    }\n  }";
        var tree = MermaidStaged.Read(source);
        var a = tree.Children.Single(child => child.Kind == MermaidKinds.Group);

        Assert.AreEqual(1, a.Children.Count(child => child.Kind == MermaidKinds.Group), "B inside A");
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void ClassesAndStylesReachTheStatesTheyName_AndTheDotsByWhatTheyAreCalled()
    {
        var styles = MermaidStaged.Read("stateDiagram-v2\n  [*] --> one\n  one:::busy --> two\n  classDef busy fill:#fee\n  class two busy\n"
                                        + "  style one stroke:#c00\n  style start fill:#0c0")
            .SelfAndDescendants().OfType<StyledNode>().ToDictionary(named => named.Words()!.Text, named => named.Style);

        Assert.AreEqual("#fee", styles["one"].Fill);
        Assert.AreEqual("#c00", styles["one"].Stroke, "a style line is laid over the class");
        Assert.AreEqual("#fee", styles["two"].Fill);
        Assert.AreEqual("#0c0", styles["[*]"].Fill, "the dot the diagram starts at is styled as start");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterIsApplied()
    {
        var config = ((ConfiguredNode<StateConfig>)MermaidStaged.Read("---\nconfig:\n  state:\n    nodeSpacing: 40\n    wrappingWidth: 90\n    forkWidth: 60\n"
                                                                      + "---\nstateDiagram-v2\n  one --> two")).Config;

        Assert.AreEqual(40, config.NodeSpacing, 0.01);
        Assert.AreEqual(90, config.Wrapping, 0.01);
        Assert.AreEqual(60, config.ForkWidth, 0.01);
        Assert.AreEqual(StateConfig.Along, config.RankSpacing, 0.01, "and what it says nothing about is Mermaid's own");
    }
}
