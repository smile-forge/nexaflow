using Markdig.Syntax;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using System.IO;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// End-to-end check that every diagram in the sample markdown dataset parses and renders
/// through the real markdown pipeline (Markdig → <see cref="BlockRenderer"/> → diagram handler).
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("markdown sample corpus")]
public class MarkdownSampleRenderTests
{
    [TestMethod]
    public void EverySampleDiagramRenders() => UiThread.Run(() =>
    {
        // Diagram docs are named mermaid-*; the extensions doc has no diagram fence.
        foreach (var path in TestSampleData.Files("markdown")
                     .Where(p => Path.GetFileName(p).StartsWith("mermaid-", StringComparison.Ordinal)))
        {
            string md  = File.ReadAllText(path);
            var    doc = MdMarkdown.Parse(md, MarkdownPipelineFactory.Default);

            var fences = doc.OfType<FencedCodeBlock>()
                            .Where(f => DiagramRenderer.IsDiagramLanguage(f.Info))
                            .ToList();

            Assert.AreNotEqual(0, fences.Count, $"no diagram fence in {Path.GetFileName(path)}");
            foreach (var fc in fences)
                Assert.IsNotNull(BlockRenderer.Render(fc, md), $"render returned null for {Path.GetFileName(path)}");
        }
    });

    /// <summary>The non-diagram extensions sample (emphasis extras, abbreviations, alert blocks)
    /// renders every block through <see cref="BlockRenderer"/> without throwing, and produces the
    /// expected extension block/inline types.</summary>
    [TestMethod]
    public void ExtensionsSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "extensions.md"));
        var    doc = MdMarkdown.Parse(md, MarkdownPipelineFactory.Default);

        foreach (var block in doc)
            Assert.IsNotNull(BlockRenderer.Render(block, md), "render returned null");

        var kinds = doc.OfType<Markdig.Extensions.Alerts.AlertBlock>()
                       .Select(a => a.Kind.ToString().ToUpperInvariant()).ToHashSet();
        foreach (var k in new[] { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" })
            Assert.IsTrue(kinds.Contains(k), $"expected a [!{k}] alert");
        Assert.IsTrue(doc.Descendants().OfType<Markdig.Extensions.Abbreviations.AbbreviationInline>().Any(),
            "expected at least one abbreviation occurrence");
    });
}
