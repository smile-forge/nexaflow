using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Er;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Er;

/// <summary>
/// What an <c>erDiagram</c> block's stages write into its tree: each subgraph gathered with what is written in it, which ends of
/// a relationship name a subgraph rather than an entity, what styles each entity, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("er-diagram")]
public class ErStagesTests
{
    /// <summary>Each end of a relationship naming a subgraph, by the name written, and which subgraph it names.</summary>
    private static Dictionary<string, int> Joined(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<GroupReferenceNode>()
                     .ToDictionary(named => named.SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Role == ErRoles.Id).Text, named => named.Group);

    /// <summary>What styles each entity, by its name.</summary>
    private static Dictionary<string, MermaidStyle> Styles(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<StyledNode>().ToDictionary(named => named.Words()!.Text, named => named.Style);

    [TestMethod, TestCategory("Unit")]
    public void EachSubgraphIsGatheredWithWhatIsWrittenInIt()
    {
        var tree = MermaidStaged.Read("erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end\n  C");
        var outer = tree.Children.Single(child => child.Kind == MermaidKinds.Group);

        Assert.AreEqual(1, outer.Children.Count(child => child.Kind == MermaidKinds.Group), "the one inside it, inside it");
        Assert.AreEqual("erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end\n  C", tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void ARelationshipMayNameASubgraphRatherThanAnEntity()
    {
        var joined = Joined("erDiagram\n  subgraph stock\n    PRODUCT\n  end\n  subgraph other\n  end\n  SUPPLIER ||--o{ other : supplies");

        Assert.AreEqual(1, joined["other"], "the second subgraph written");
        Assert.IsFalse(joined.ContainsKey("SUPPLIER"), "and the other end is an entity");
    }

    [TestMethod, TestCategory("Unit")]
    public void ASubgraphIsNamedWhetherItIsWrittenAboveTheRelationshipOrBelowIt() =>
        Assert.AreEqual(0, Joined("erDiagram\n  SUPPLIER ||--o{ stock : supplies\n  subgraph stock\n    PRODUCT\n  end")["stock"]);

    [TestMethod, TestCategory("Unit")]
    public void ANameNoSubgraphIsCalledIsAnEntity() =>
        Assert.AreEqual(0, Joined("erDiagram\n  subgraph stock\n    PRODUCT\n  end\n  SUPPLIER ||--o{ PRODUCT : supplies").Count);

    [TestMethod, TestCategory("Unit")]
    public void AStyleLineAndAClassLineBothReachTheEntityTheyName()
    {
        var styles = Styles("erDiagram\n  A ||--|| B : x\n  classDef blue fill:#00f\n  classDef bold stroke-width:3px\n"
                            + "  class A,B blue,bold\n  style B fill:#f00");

        Assert.AreEqual("#00f", styles["A"].Fill);
        Assert.AreEqual(3, styles["A"].StrokeWidth, "a class line gives every class it names");
        Assert.AreEqual("#f00", styles["B"].Fill, "what is written for one on its own is laid over its classes");
    }

    [TestMethod, TestCategory("Unit")]
    public void SeveralClassesGivenAtOnceAreAllLaidOn()
    {
        var style = Styles("erDiagram\n  A:::blue,bold ||--|| B : x\n  classDef blue fill:#00f\n  classDef bold stroke-width:3px")["A"];

        Assert.AreEqual("#00f", style.Fill);
        Assert.AreEqual(3, style.StrokeWidth);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMatterSaysHowSmallAnEntityMayBe()
    {
        static ErConfig Config(string source) => ((ConfiguredNode<ErConfig>)MermaidStaged.Read(source)).Config;

        var config = Config("---\nconfig:\n  er:\n    minEntityWidth: 140\n    entityPadding: 6\n    fontSize: 16\n---\nerDiagram\n  A");

        Assert.AreEqual(140, config.MinWidth);
        Assert.AreEqual(6, config.Padding);
        Assert.AreEqual(24, config.RowHeight, "a row follows how big the words are");
        Assert.AreEqual(ErConfig.Shortest, config.MinHeight, "and the rest are left as they are");

        var painted = Config("---\nconfig:\n  er:\n    fill: honeydew\n    stroke: gray\n---\nerDiagram\n  A");
        Assert.AreEqual(("honeydew", "gray"), (painted.Fill, painted.Stroke));
    }
}
