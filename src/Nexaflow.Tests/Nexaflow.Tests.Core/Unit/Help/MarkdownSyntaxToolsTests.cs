using System.Text.Json.Nodes;
using Nexaflow.Core.Help;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What the assistant's Markdown can draw and how to write each, as the assistant asks for it — read from the
/// Markdown feature's help, one section a kind of content, so it is told exactly what the user reads.
/// </summary>
[TestClass]
[CoversNode("help-ai-tools")]
public class MarkdownSyntaxToolsTests
{
    [TestMethod]
    public async Task TheSyntaxes_AreTheMarkdownHelpsSections_AndNoOtherPagesOnes()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "get_markdown_syntaxes");

        StringAssert.Contains(result.ModelText, "Markdown: Pie chart, Math");
        Assert.IsFalse(result.ModelText.Contains("Searching"), "the Text viewer's help is not about what Markdown draws");
    }

    [TestMethod]
    public async Task ASyntax_IsItsSectionWhole_WithItsExample()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "get_markdown_syntax", ("name", "pie chart"));

        Assert.IsFalse(result.IsError);
        StringAssert.StartsWith(result.ModelText, "## Pie chart");
        StringAssert.Contains(result.ModelText, "\"Apples\" : 3", "the example comes with it");
        Assert.IsFalse(result.ModelText.Contains("## Math"), "and stops where the next section starts");
    }

    [TestMethod]
    public async Task AnUnknownSyntax_SaysWhereTheListIs_RatherThanGuessing()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "get_markdown_syntax", ("name", "Hologram"));

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.ModelText, "get_markdown_syntaxes");
    }

    [TestMethod]
    public void AHeadingInsideAFence_IsTheExamplesNotTheHelps()
    {
        var sections = MarkdownSyntaxTools.Sections(HelpFixture.MarkdownPage);

        CollectionAssert.AreEqual(new[] { "Pie chart", "Math" }, sections.Select(s => s.Name).ToArray());
    }

    [TestMethod]
    public void EveryToolIsSafeAndReadOnly()
    {
        using var help = new HelpFixture();

        foreach (var tool in new MarkdownSyntaxTools(help.Library).Tools)
            Assert.AreEqual(ToolSafety.SafeOperation, tool.Safety, $"{tool.Name} only reads.");
    }

    private static async Task<ToolResult> Run(HelpFixture help, string name, params (string Key, string Value)[] args)
    {
        var tool = new MarkdownSyntaxTools(help.Library).Tools.Single(t => t.Name == name);
        var json = new JsonObject();
        foreach (var (key, value) in args) json[key] = value;
        return await tool.InvokeAsync(json, CancellationToken.None);
    }
}
