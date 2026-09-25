using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Timeline;

/// <summary>What a <c>timeline</c> block's front matter asks for, as its stages hang it on the block.</summary>
[TestClass]
[CoversNode("timeline-ast")]
public class TimelineConfigTests
{
    [TestMethod]
    public void TheFrontMattersOptionsAndColourSlotsAreRead()
    {
        var config = ((ConfiguredNode<TimelineConfig>)MermaidStaged.Read("---\nconfig:\n  timeline:\n    disableMulticolor: true\n    padding: 4\n"
            + "  themeVariables:\n    cScale2: \"#4e79a7\"\n    cScaleLabel2: \"#ffffff\"\n---\ntimeline\n    2002 : LinkedIn")).Config;

        Assert.IsTrue(config.DisableMulticolor);
        Assert.AreEqual(4, config.Padding);
        Assert.AreEqual("#4e79a7", config.ScaleAt(2));
        Assert.AreEqual("#ffffff", config.ScaleLabelAt(2));
        Assert.IsNull(config.ScaleAt(0), "a slot nobody writes is the theme's");
    }
}
