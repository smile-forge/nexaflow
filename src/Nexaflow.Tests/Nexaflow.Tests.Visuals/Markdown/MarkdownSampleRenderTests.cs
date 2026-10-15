using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using System.IO;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// End-to-end check that every diagram in the sample markdown dataset parses and renders
/// through the real markdown pipeline — read, its stages run, and laid by <see cref="MarkdownBuilder"/> with every fence laid by its language.
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
            var    doc = Blocks(md);

            var fences = Fences(doc).Where(f => ContentLanguages.Reads(Named(f)))
                            .ToList();

            Assert.AreNotEqual(0, fences.Count, $"no diagram fence in {Path.GetFileName(path)}");
            Lays(md, fences, Path.GetFileName(path));
        }
    });

    /// <summary>The non-diagram extensions sample (emphasis extras, abbreviations, alert blocks)
    /// lays without throwing, and produces the
    /// expected extension block/inline types.</summary>
    [TestMethod]
    public void ExtensionsSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "extensions.md"));
        var    doc = Blocks(md);

        Lays(md, [], "the whole document");

        var kinds = doc.Where(block => block.Kind == MarkdownKinds.Alert)
                       .Select(alert => alert.SelfAndDescendants().First(part => part.Role == Roles.Name).Text.ToUpperInvariant()).ToHashSet();
        foreach (var k in new[] { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" })
            Assert.IsTrue(kinds.Contains(k), $"expected a [!{k}] alert");
        Assert.IsTrue(doc.SelectMany(block => block.SelfAndDescendants()).Any(part => part.Kind == MarkdownKinds.Abbreviation && !part.Derived),
            "expected at least one abbreviation occurrence");
    });

    /// <summary>The <c>latex-math-*.md</c> references render every block through
    /// lay without throwing. Unsupported LaTeX degrades to a styled
    /// fallback rather than crashing, so every block still draws something —
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
            var    doc = Blocks(md);
            Lays(md, [], Path.GetFileName(path));
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
        // Not "unsupported by design" — a gap, written down. \limits, \nolimits, \hline, \mod and
        // \genfrac were all on this list and are all set now; \sideset is what is left. \limits and \nolimits say whether a big
        // operator wears its scripts over and under or beside it, and nothing here reads them: they were
        // handled only by the reader that has gone. The fix is the reading's, not the builder's — the
        // operator and the word after it are one thing, the way `\not` and what it crosses are.
        string[] known = [@"\sideset"];

        var files = TestSampleData.Files("markdown")
            .Where(p => Path.GetFileName(p).StartsWith("latex-math-", StringComparison.Ordinal))
            .ToList();
        Assert.AreNotEqual(0, files.Count, "no latex-math-* samples found");

        int typeset = 0, fellBack = 0;
        foreach (var path in files)
        {
            var    doc  = Blocks(File.ReadAllText(path));
            string name = Path.GetFileName(path);

            foreach (var math in doc.SelectMany(block => block.SelfAndDescendants()).Where(part => part.Node is BlockNode { Language: "latex" } && part.Part(Roles.Body) is not null))
            {
                string latex = ContentNested.Own(math.Part(Roles.Body)!.Node);

                var layout = Laying.Formula(latex, 20);
                var ok = layout is not null
                         && !layout.Trouble.Any(d => d.Severity == DiagnosticSeverity.Error);

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
        Assert.AreEqual(1, fellBack, "exactly the formulas naming a known gap should fail to typeset");
    });

    /// <summary>The <c>music-*.md</c> references parse into <c>abc</c> / <c>lilypond</c> fences and engrave (or
    /// gracefully fall back) without throwing — the docs double as a
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
            var    doc = Blocks(md);
            Assert.IsTrue(Fences(doc).Any(fence => Named(fence) is "abc" or "lilypond"),
                $"no music fence parsed in {Path.GetFileName(path)}");
            Lays(md, [], Path.GetFileName(path));
        }
    });

    /// <summary>
    /// The <c>qr-codes.md</c> reference: every <c>qr</c> fence in it reaches the QR handler and draws.
    /// The doc deliberately ends with a block that cannot be built, so this also asserts the thing a
    /// render-without-throwing test usually misses ΓÇö that a bad block still produces an element, in
    /// place of the picture, rather than taking the document down with it.
    /// </summary>
    [TestMethod]
    public void QrSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "qr-codes.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "qr".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 14, $"expected the reference to show every type, found {fences.Count} qr fences");

        Lays(md, fences, "qr");

        Lays(md, [], "the whole document");
    });

    /// <summary>
    /// The <c>barcodes.md</c> reference: every <c>barcode</c> fence reaches the handler and draws.
    ///
    /// <para>
    /// The doc deliberately ends with two blocks that cannot be drawn as asked — a value the format cannot
    /// carry, and a format that does not exist — because those take different paths. The first stays a
    /// barcode and is marked; the second falls back to its source. Both must produce an element, and
    /// neither may take the document down.
    /// </para>
    /// </summary>
    [TestMethod]
    public void BarcodeSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "barcodes.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "barcode".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 23, $"expected the reference to show every format, found {fences.Count}");

        Lays(md, fences, "barcode");

        Lays(md, [], "the whole document");

        // That the reference ends with a value the format cannot carry and a format that does not exist is
        // the point of it: both paths have to produce an element rather than take the document down, which
        // the loop above has just established. Which element each produces is asserted where it belongs, in
        // BarcodeElementTests.
    });

    [TestMethod]
    public void DataMatrixSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "datamatrix.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "datamatrix".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 9, $"expected the reference to show every type, found {fences.Count}");

        Lays(md, fences, "datamatrix");
    });

    [TestMethod]
    public void Pdf417SampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "pdf417.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "pdf417".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 8, $"expected the reference to show every setting, found {fences.Count}");

        Lays(md, fences, "pdf417");
    });

    [TestMethod]
    public void AztecSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "aztec.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "aztec".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 12, $"expected the reference to show both families, found {fences.Count}");

        Lays(md, fences, "aztec");
    });

    [TestMethod]
    public void SmilesSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "smiles.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "smiles".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 6, $"expected every section of the reference, found {fences.Count}");

        Lays(md, fences, "smiles");
    });

    /// <summary>
    /// Every cloud in the reference draws. The sections are deliberately awkward — a shape, turned words,
    /// colours written out, a weight that will not read, a word named as a setting — so that a block which
    /// stops being a cloud in one of those ways is caught here rather than in a document.
    /// </summary>
    [TestMethod]
    public void WordCloudSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "wordcloud.md"));
        var    doc = Blocks(md);

        var fences = Fences(doc).Where(f => "wordcloud".Equals(Named(f), StringComparison.OrdinalIgnoreCase))
                        .ToList();

        Assert.IsTrue(fences.Count >= 8, $"expected every section of the reference, found {fences.Count}");

        Lays(md, fences, "wordcloud");
    });

    /// <summary>
    /// Every correlation-plot fence of the reference renders — the four languages, and every section of
    /// what they can be written with.
    /// </summary>
    [TestMethod]
    public void PlotSampleRenders() => UiThread.Run(() =>
    {
        string md  = File.ReadAllText(TestSampleData.Path("markdown", "plots.md"));
        var    doc = Blocks(md);

        string[] fences = ["scatter", "bubble", "heatmap", "density2d"];

        var blocks = Fences(doc).Where(f => fences.Contains(Named(f), StringComparer.OrdinalIgnoreCase)).ToList();

        Assert.IsTrue(blocks.Count >= 10, $"expected every section of the reference, found {blocks.Count}");

        foreach (var fence in fences)
            Assert.IsTrue(blocks.Any(block => fence.Equals(Named(block), StringComparison.OrdinalIgnoreCase)),
                          $"the reference shows no {fence}");

        Lays(md, blocks, "plot");
    });

    /// <summary>
    /// The whole document laid as a reader is shown it, drawing something — and every one of <paramref name="fences"/>
    /// drawn: something on the page standing for its characters, a picture or, where it could not be drawn, what was
    /// written.
    /// </summary>
    private static void Lays(string md, IEnumerable<ContentPart> fences, string what)
    {
        var laid = Laying.Lay(null, md, 700, StyleFormat.Dark);
        Assert.IsTrue(laid.Draws, $"{what}: the document drew nothing");

        var drawn = laid.Root.SelfAndDescendants().Where(piece => piece.Part is not null).Select(piece => piece.Sits().Start).ToList();

        foreach (var fence in fences)
            Assert.IsTrue(drawn.Any(at => at >= fence.Start && at <= fence.End),
                          $"{what}: nothing was drawn for the fence on line {md.AsSpan(0, fence.Start).Count('\n') + 1}");
    }

    /// <summary>The blocks a document is written in, as its reading has them.</summary>
    private static IReadOnlyList<ContentPart> Blocks(string md) => ContentPart.Of(MarkdownParser.Worked(md)).Children;

    /// <summary>Every fenced block of a document.</summary>
    private static IEnumerable<ContentPart> Fences(IEnumerable<ContentPart> blocks) =>
        blocks.Where(block => block.Kind == Kinds.Block && block.Node is BlockNode);

    /// <summary>The language a fence names.</summary>
    private static string Named(ContentPart fence) => ((BlockNode)fence.Node).Language;
}
