using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

[TestClass]
[CoversNode("vtext-pipeline")]
public class MarkdownPipelineFactoryTests
{
    [TestMethod]
    public void Pipeline_ParsesPipeTables()
    {
        const string src = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        var doc = MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);
        Assert.IsTrue(doc.OfType<Table>().Any(), "Pipe table not recognised");
    }

    [TestMethod]
    public void Pipeline_ParsesMathBlocks()
    {
        const string src = "$$\nx^2\n$$\n";
        var doc = MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);
        Assert.IsTrue(doc.OfType<MathBlock>().Any(), "Math block not recognised");
    }

    [TestMethod]
    public void Pipeline_ParsesFencedDiagrams()
    {
        const string src = "```mermaid\ngraph LR\nA-->B\n```\n";
        var doc = MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);
        var fc = doc.OfType<FencedCodeBlock>().FirstOrDefault();
        Assert.IsNotNull(fc);
        Assert.AreEqual("mermaid", fc.Info);
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage(fc.Info));
    }

    [TestMethod]
    public void Pipeline_ReturnsSameInstance()
    {
        // Cached singleton — used heavily in tight render loops
        var a = MarkdownPipelineFactory.Default;
        var b = MarkdownPipelineFactory.Default;
        Assert.AreSame(a, b);
    }
}
