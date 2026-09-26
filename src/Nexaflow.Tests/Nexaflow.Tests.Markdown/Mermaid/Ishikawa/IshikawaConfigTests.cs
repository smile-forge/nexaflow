using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Ishikawa;

/// <summary>What an <c>ishikawa</c> block's front matter asks for, as its stage hangs it on the block.</summary>
[TestClass]
[CoversNode("ishikawa-ast")]
public class IshikawaConfigTests
{
    private static IshikawaConfig Config(string source) => ((ConfiguredNode<IshikawaConfig>)MermaidStaged.Read(source)).Config;

    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = Config("---\nconfig:\n  fontSize: 18\n  ishikawa:\n    diagramPadding: 40\n    useMaxWidth: true\n    singleBone: true\n  themeVariables:\n    lineColor: \"#ff0000\"\n    mainBkg: \"#00ff00\"\n    textColor: \"#0000ff\"\n---\nishikawa\n  Problem");

        Assert.AreEqual(new IshikawaConfig { DiagramPadding = 40, UseMaxWidth = true, SingleBone = true, FontSize = 18, LineColour = "#ff0000", Background = "#00ff00", TextColour = "#0000ff" }, config);
        Assert.AreEqual(IshikawaConfig.Default, Config("ishikawa\n  Problem"));
    }
}
