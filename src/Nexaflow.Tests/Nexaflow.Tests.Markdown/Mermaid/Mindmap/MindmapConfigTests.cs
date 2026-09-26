using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Mindmap;

/// <summary>What a <c>mindmap</c> block's front matter asks for, as its stages hang it on the block.</summary>
[TestClass]
[CoversNode("mindmap-ast")]
public class MindmapConfigTests
{
    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = ((ConfiguredNode<MindmapConfig>)MermaidStaged.Read("---\nconfig:\n  layout: tidy-tree\n  mindmap:\n    padding: 14\n    maxNodeWidth: 150\n  themeVariables:\n    cScale1: \"#ff0000\"\n    git0: \"#00ff00\"\n---\nmindmap\n  r((root))")).Config;

        Assert.AreEqual(("tidy-tree", 14d, 150d, "#ff0000", "#00ff00"), (config.Layout, config.Padding!.Value, config.MaxNodeWidth!.Value, config.Scale[1], config.RootFill));
    }
}
