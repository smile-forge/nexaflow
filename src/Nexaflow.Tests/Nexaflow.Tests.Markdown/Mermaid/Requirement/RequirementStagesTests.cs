using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Requirement;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Requirement;

/// <summary>
/// What a <c>requirementDiagram</c> block's stages write into its tree: what styles each requirement, said on the name first
/// writing it, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("requirement-diagram")]
public class RequirementStagesTests
{
    /// <summary>What styles each requirement, by its name.</summary>
    private static Dictionary<string, MermaidStyle> Styles(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<StyledNode>().ToDictionary(named => named.Words()!.Text, named => named.Style);

    [TestMethod, TestCategory("Unit")]
    public void ANameOnALineOfItsOwnTakesTheClassGivenIt_WhereverTheClassIsDefined() =>
        Assert.AreEqual("#00f", Styles("requirementDiagram\n  A:::blue\n  classDef blue fill:#00f")["A"].Fill);

    [TestMethod, TestCategory("Unit")]
    public void AStyleLineAndAClassLineBothReachWhatTheyName()
    {
        var styles = Styles("requirementDiagram\n  a - traces -> b\n  classDef blue fill:#00f\n  class a blue\n  style b fill:#f00");

        Assert.AreEqual("#00f", styles["a"].Fill);
        Assert.AreEqual("#f00", styles["b"].Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheStyleIsSaidOnTheNameFirstWritingIt()
    {
        var tree = MermaidStaged.Read("requirementDiagram\n  a - traces -> b\n  requirement a {\n    id: 1\n  }\n  style a fill:#f00");

        Assert.AreEqual(1, tree.SelfAndDescendants().OfType<StyledNode>().Count(named => named.Words()!.Text == "a"), "once, where it is first named");
    }

    [TestMethod, TestCategory("Unit")]
    public void NothingIsSaidWhereNothingIsStyled() =>
        Assert.AreEqual(0, Styles("requirementDiagram\n  a - traces -> b").Count);

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysHowSmallABoxMayBe()
    {
        var config = ((ConfiguredNode<RequirementConfig>)MermaidStaged.Read("---\nconfig:\n  requirement:\n    rect_min_width: 140\n    rect_padding: 6\n---\nrequirementDiagram\n  a - traces -> b")).Config;

        Assert.AreEqual(140, config.MinWidth);
        Assert.AreEqual(6, config.Padding);
        Assert.AreEqual(RequirementConfig.Shortest, config.MinHeight, "and leaves the rest as they are");
    }
}
