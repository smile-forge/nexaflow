using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Architecture;

/// <summary>What an <c>architecture-beta</c> block's front matter asks for, as its stages hang it on the block.</summary>
[TestClass]
[CoversNode("architecture-ast")]
public class ArchitectureConfigTests
{
    private static ArchitectureConfig Config(string source) => ((ConfiguredNode<ArchitectureConfig>)MermaidStaged.Read(source)).Config;

    [TestMethod]
    public void TheFrontMattersOptionsAreRead()
    {
        Assert.AreEqual(ArchitectureConfig.Icon, Config("architecture-beta\n  service a").IconSize);

        var config = Config("---\nconfig:\n  architecture:\n    iconSize: 64\n    fontSize: 15\n    padding: 20\n"
                            + "    nodeSeparation: 50\n    idealEdgeLengthMultiplier: 3\n---\narchitecture-beta\n  service a");

        Assert.AreEqual(64, config.IconSize);
        Assert.AreEqual(15, config.FontSize);
        Assert.AreEqual(20, config.Padding);
        Assert.AreEqual(50, config.NodeSeparation);
        Assert.AreEqual(3, config.EdgeLength);
    }
}
