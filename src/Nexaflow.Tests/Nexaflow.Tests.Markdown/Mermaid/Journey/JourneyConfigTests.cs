using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Journey;

/// <summary>What a <c>journey</c> block's front matter asks for, as its stage hangs it on the block.</summary>
[TestClass]
[CoversNode("journey-ast")]
public class JourneyConfigTests
{
    [TestMethod]
    public void TheFrontMattersSizesAndColourListsAreRead()
    {
        var config = ((ConfiguredNode<JourneyConfig>)MermaidStaged.Read("---\nconfig:\n  journey:\n    width: 200\n    height: 60\n    boxMargin: 4\n    taskFontSize: 14\n"
            + "    actorColours:\n      - \"#ff0000\"\n      - \"#00ff00\"\n    sectionFills: [\"#101010\"]\n"
            + "  themeVariables:\n    fillType1: \"#202020\"\n---\njourney\n  A: 3: Me")).Config;

        Assert.AreEqual(200, config.Width);
        Assert.AreEqual(60, config.Height);
        Assert.AreEqual(4, config.BoxMargin);
        Assert.AreEqual(14, config.TaskFontSize);
        Assert.AreEqual("#ff0000", config.ActorColour(0));
        Assert.IsNull(config.ActorColour(2), "no colour is written for a third actor");
        Assert.AreEqual("#101010", config.SectionFill(0), "the list is what a section is filled with");
        Assert.AreEqual("#202020", config.SectionFill(1), "and the theme's slot behind it");
    }
}
