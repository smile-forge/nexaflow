using System.Text.Json.Nodes;
using Nexaflow.Core.Help;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The help, as the assistant meets it: the three rungs a model that has never seen Nexaflow has to climb —
/// which pages exist, which of them mention a thing, and then the page itself.
/// </summary>
[TestClass]
[CoversNode("help-ai-tools")]
public class HelpClientToolsTests
{
    [TestMethod]
    public void EveryToolIsSafeAndReadOnly()
    {
        using var help = new HelpFixture();

        foreach (var tool in new HelpClientTools(help.Library).Tools)
            Assert.AreEqual(ToolSafety.SafeOperation, tool.Safety, $"{tool.Name} should need no approval — it only reads.");
    }

    [TestMethod]
    public async Task ListHelpTopics_NamesEveryPage_ButNotTheIndex()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "list_help_topics");

        StringAssert.Contains(result.ModelText, "id=Text");
        StringAssert.Contains(result.ModelText, "id=Markdown");
        Assert.IsFalse(result.ModelText.Contains("id=index"), "the index is not a page anyone asks for by name");
    }

    [TestMethod]
    public async Task SearchHelp_FindsThePageThatMentionsTheWord()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "search_help", ("query", "diagrams"));

        StringAssert.Contains(result.ModelText, "id=Markdown");
        Assert.IsFalse(result.ModelText.Contains("id=Text"), "the Text page never mentions diagrams");
    }

    [TestMethod]
    public async Task SearchHelp_FindingNothing_SaysHowToSeeWhatThereIs()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "search_help", ("query", "kerning"));

        StringAssert.Contains(result.ModelText, "list_help_topics");
    }

    [TestMethod]
    public async Task ReadHelp_IsThePageTheAppShows()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "read_help", ("topic", "Text"));

        Assert.AreEqual(HelpFixture.TextPage, result.ModelText);
    }

    [TestMethod]
    public async Task ReadHelp_AnUnknownPage_SaysSoRatherThanGuessing()
    {
        using var help = new HelpFixture();

        var result = await Run(help, "read_help", ("topic", "Trombone"));

        Assert.IsTrue(result.IsError, "an invented topic must not read as a page");
        StringAssert.Contains(result.ModelText, "list_help_topics");
    }

    private static async Task<ToolResult> Run(HelpFixture help, string name, params (string Key, string Value)[] args)
    {
        var tool = new HelpClientTools(help.Library).Tools.Single(t => t.Name == name);

        var json = new JsonObject();
        foreach (var (key, value) in args) json[key] = value;

        return await tool.InvokeAsync(json, CancellationToken.None);
    }
}
