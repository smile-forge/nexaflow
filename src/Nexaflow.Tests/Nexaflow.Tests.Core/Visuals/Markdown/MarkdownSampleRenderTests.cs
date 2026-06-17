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
public class MarkdownSampleRenderTests
{
    [TestMethod]
    public void EverySampleDiagramRenders() => UiThread.Run(() =>
    {
        foreach (var path in TestSampleData.Files("markdown"))
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
}
