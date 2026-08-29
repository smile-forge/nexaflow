using Markdig.Syntax;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using System.IO;
using MdMarkdown = Markdig.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Markdown;

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

    /// <summary>The <c>latex-math-*.md</c> references render every block through
    /// <see cref="BlockRenderer"/> without throwing. Unsupported LaTeX degrades to a styled
    /// fallback rather than crashing, so every block still renders to a non-null element —
    /// which is exactly what lets the docs double as a live support map.</summary>
    [TestMethod]
    public void LatexMathSamplesRender() => UiThread.Run(() =>
    {
        var files = TestSampleData.Files("markdown")
            .Where(p => Path.GetFileName(p).StartsWith("latex-math-", StringComparison.Ordinal))
            .ToList();
        Assert.AreNotEqual(0, files.Count, "no latex-math-* samples found");

        foreach (var path in files)
        {
            string md  = File.ReadAllText(path);
            var    doc = MdMarkdown.Parse(md, MarkdownPipelineFactory.Default);
            foreach (var block in doc)
                Assert.IsNotNull(BlockRenderer.Render(block, md),
                    $"render returned null in {Path.GetFileName(path)}");
        }
    });

    /// <summary>
    /// Every formula in the latex-math-* samples is set, save the ones named below.
    ///
    /// <para>
    /// Asked of <see cref="LatexLayout"/>, which is what draws the page. It used to be asked of
    /// FormulaControl, a control nothing ships with any more, which read the formula with the engine's own
    /// parser — so this measured a reader the app had stopped using and went on passing while the real one
    /// could not set <c>\mod</c>, <c>\genfrac</c> or <c>\limits</c> at all. Two of those are fixed; the
    /// third is named here rather than hidden, and the count is asserted so a fourth cannot join it
    /// quietly.
    /// </para>
    /// </summary>
    [TestMethod]
    public void LatexMathSamplesTypeset() => UiThread.Run(() =>
    {
        // Not "unsupported by design" — a gap, written down. \limits and \nolimits say whether a big
        // operator wears its scripts over and under or beside it, and nothing here reads them: they were
        // handled only by the reader that has gone. The fix is the reading's, not the builder's — the
        // operator and the word after it are one thing, the way `\not` and what it crosses are.
        string[] known = [@"\sideset", @"\limits", @"\nolimits", @"\hline"];

        var files = TestSampleData.Files("markdown")
            .Where(p => Path.GetFileName(p).StartsWith("latex-math-", StringComparison.Ordinal))
            .ToList();
        Assert.AreNotEqual(0, files.Count, "no latex-math-* samples found");

        int typeset = 0, fellBack = 0;
        foreach (var path in files)
        {
            var    doc  = MdMarkdown.Parse(File.ReadAllText(path), MarkdownPipelineFactory.Default);
            string name = Path.GetFileName(path);

            foreach (var math in doc.Descendants().OfType<Markdig.Extensions.Mathematics.MathInline>())
            {
                string latex = math.Content.ToString();

                var layout = LatexLayout.Build(latex, 20);
                var ok = layout is not null
                         && !layout.Tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

                if (known.Any(gap => latex.Contains(gap, StringComparison.Ordinal)))
                {
                    Assert.IsFalse(ok, $"{name}: a known gap now typesets — take it off the list: {latex}");
                    fellBack++;
                }
                else
                {
                    Assert.IsTrue(ok, $"{name}: formula fell back to source: {latex}");
                    typeset++;
                }
            }
        }

        Assert.AreNotEqual(0, typeset, "no formula was found in the latex-math-* samples");
        Assert.AreEqual(3, fellBack, "exactly the formulas naming a known gap should fail to typeset");
    });

    /// <summary>The <c>music-*.md</c> references parse into <c>#% … #%</c> music blocks and engrave (or
    /// gracefully fall back) through <see cref="BlockRenderer"/> without throwing — the docs double as a
    /// live map of the notation engine's support.</summary>
    [TestMethod]
    public void MusicSamplesRender() => UiThread.Run(() =>
    {
        var files = TestSampleData.Files("markdown")
            .Where(p => Path.GetFileName(p).StartsWith("music-", StringComparison.Ordinal))
            .ToList();
        Assert.AreNotEqual(0, files.Count, "no music-* samples found");

        foreach (var path in files)
        {
            string md  = File.ReadAllText(path);
            var    doc = MdMarkdown.Parse(md, MarkdownPipelineFactory.Default);
            Assert.IsTrue(doc.OfType<Nexaflow.Visuals.Text.Markdown.Music.MusicBlock>().Any(),
                $"no music block parsed in {Path.GetFileName(path)}");
            foreach (var block in doc)
                Assert.IsNotNull(BlockRenderer.Render(block, md),
                    $"render returned null in {Path.GetFileName(path)}");
        }
    });
}
