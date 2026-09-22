using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;
using MdMarkdown = Markdig.Markdown;
using Nexaflow.Markdown.Prose;
using Markdig.Renderers.Html;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// One pipeline reads every document, and this is the list of what it can therefore see.
///
/// <para>
/// There is a single set of Markdig extensions — <see cref="MarkdownParser.Reading"/> — and every surface
/// parses with <see cref="MarkdownParser.Pipeline"/>. Two lists is the bug this guards: an extension added
/// to one and not the other makes a construct that renders in the viewer and not in the editor, or the
/// other way round, with nothing to say why.
/// </para>
/// </summary>
[TestClass]
[CoversNode("vtext-pipeline")]
public class MarkdownPipelineTests
{
    [TestMethod]
    public void Pipeline_ParsesPipeTables()
    {
        const string src = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        var doc = MdMarkdown.Parse(src, MarkdownParser.Pipeline);
        Assert.IsTrue(doc.OfType<Table>().Any(), "Pipe table not recognised");
    }

    [TestMethod]
    public void Pipeline_ParsesMathBlocks()
    {
        const string src = "$$\nx^2\n$$\n";
        var doc = MdMarkdown.Parse(src, MarkdownParser.Pipeline);
        Assert.IsTrue(doc.OfType<MathBlock>().Any(), "Math block not recognised");
    }

    [TestMethod]
    public void Pipeline_GivesEveryHeadingAnId()
    {
        // What an in-page link is resolved against. Without it every [x](#anchor) in the help pages,
        // which are the biggest documents the app ships, reaches nothing.
        var doc = MdMarkdown.Parse("## Getting Started\n", MarkdownParser.Pipeline);
        var heading = doc.OfType<HeadingBlock>().Single();

        Assert.AreEqual("getting-started", heading.GetAttributes().Id);
    }

    [TestMethod]
    public void Pipeline_ParsesFencedDiagrams()
    {
        const string src = "```mermaid\ngraph LR\nA-->B\n```\n";
        var doc = MdMarkdown.Parse(src, MarkdownParser.Pipeline);
        var fc = doc.OfType<FencedCodeBlock>().FirstOrDefault();
        Assert.IsNotNull(fc);
        Assert.AreEqual("mermaid", fc.Info);
        Assert.IsTrue(ContentLanguages.Reads(fc.Info));
    }

    [TestMethod]
    public void Pipeline_ParsesMusicFences()
    {
        // Music is a fence like every other language, read by the same table. It has no block syntax of
        // its own, so nothing here is a special case.
        foreach (var named in new[] { "abc", "lilypond" })
        {
            var doc = MdMarkdown.Parse($"```{named}\nX:1\n```\n", MarkdownParser.Pipeline);
            var fence = doc.OfType<FencedCodeBlock>().Single();

            Assert.AreEqual(named, fence.Info);
            Assert.IsTrue(ContentLanguages.Reads(fence.Info), $"nothing reads a {named} fence");
        }
    }

    [TestMethod]
    public void Pipeline_ReturnsSameInstance()
    {
        // Cached singleton — used heavily in tight render loops
        var a = MarkdownParser.Pipeline;
        var b = MarkdownParser.Pipeline;
        Assert.AreSame(a, b);
    }
}
