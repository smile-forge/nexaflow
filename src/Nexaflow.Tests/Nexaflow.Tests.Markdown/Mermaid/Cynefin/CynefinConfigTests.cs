using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Cynefin;

/// <summary>What a <c>cynefin-beta</c> block's front matter asks for, as its stages hang it on the block.</summary>
[TestClass]
[CoversNode("cynefin-ast")]
public class CynefinConfigTests
{
    /// <summary>Where each domain's fill is kept: the order <see cref="CynefinGrammar.Domains"/> lists them.</summary>
    private const int Clear = 0;
    private const int Complex = 2;

    private static CynefinConfig Config(string source) => ((ConfiguredNode<CynefinConfig>)MermaidStaged.Read(source)).Config;

    [TestMethod]
    public void TheFrontMattersOptionsAndDomainColoursAreRead()
    {
        var config = Config("---\nconfig:\n  cynefin:\n    width: 600\n    height: 400\n    padding: 4\n    showDomainDescriptions: true\n"
            + "  themeVariables:\n    cynefin:\n      complexBg: \"#4e79a7\"\n      boundaryColor: \"#888888\"\n---\ncynefin-beta\n  complex");

        Assert.AreEqual(600, config.Width);
        Assert.AreEqual(400, config.Height);
        Assert.AreEqual(4, config.Padding);
        Assert.IsTrue(config.ShowDomainDescriptions);
        Assert.AreEqual("#4e79a7", config.DomainFills[Complex]);
        Assert.AreEqual("#888888", config.BoundaryColour);
        Assert.IsTrue(CynefinConfig.Default.ShowDomainDescriptions, "a domain says how it is worked unless the front matter says not to");
        Assert.IsFalse(Config("---\nconfig:\n  cynefin:\n    showDomainDescriptions: false\n---\ncynefin-beta\n  complex").ShowDomainDescriptions);
    }

    [TestMethod]
    public void ADomainColourNamingNoDiagramIsStillTheDomains() =>
        Assert.AreEqual("#59a14f", Config("---\nconfig:\n  themeVariables:\n    clearBg: \"#59a14f\"\n---\ncynefin-beta\n  clear").DomainFills[Clear]);

    [TestMethod]
    public void TheFrontMatterSaysHowTheBoundariesTheCliffTheArrowsAndTheWordsAreDrawn()
    {
        var config = Config("---\nconfig:\n  themeVariables:\n    cynefin:\n      boundaryWidth: 2\n      cliffColor: \"#ff0000\"\n      cliffWidth: 4\n"
            + "      arrowColor: \"#00ff00\"\n      arrowWidth: 3\n      labelColor: \"#ffffff\"\n      textColor: \"#cccccc\"\n"
            + "      domainFontSize: 18\n      itemFontSize: 9\n---\ncynefin-beta\n  complex\n    \"One\"");

        Assert.AreEqual(2, config.BoundaryWidth);
        Assert.AreEqual("#ff0000", config.CliffColour);
        Assert.AreEqual(4, config.CliffWidth);
        Assert.AreEqual("#00ff00", config.ArrowColour);
        Assert.AreEqual(3, config.ArrowWidth);
        Assert.AreEqual("#ffffff", config.LabelColour);
        Assert.AreEqual("#cccccc", config.TextColour);
        Assert.AreEqual(18, config.DomainFontSize);
        Assert.AreEqual(9, config.ItemFontSize);
    }
}
