using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// The ```abc fence, from markdown to an engraved tune, on both surfaces.
///
/// <para>
/// One registration lights it up on both, and this is the test that says so — the element renderer and the
/// FlowDocument renderer are separate switches that both gate on the same question, and a language added
/// to one and not the other looks registered and is not.
/// </para>
/// <para>
/// The offset is the other half. The parser is handed the fence's <em>body</em> and reports offsets into
/// it; the editing host splices into the whole markdown block, fence lines and all. Without the bias every
/// edit would land a couple of lines early, which is a fault nothing but arithmetic can catch.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcFenceTests
{
    private const string Document = "Before it.\n\n```abc\nX:1\nK:C\nCDEF|\n```\n\nAfter it.\n";

    [TestMethod]
    public void TheFenceIsALanguageTheRendererKnows() =>
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("abc"));

    [TestMethod]
    public void AndItEngravesOnTheElementSurface() => UiThread.Run(() =>
    {
        var block = Markdig.Markdown.Parse(Document, MarkdownPipelineFactory.Default)
            .OfType<Markdig.Syntax.FencedCodeBlock>()
            .Single();

        var element = BlockRenderer.Render(block, Document, MarkdownPalette.Dark);

        Assert.IsNotNull(Inside(element), "no score came out of the fence");
    });

    [TestMethod]
    public void AndOnTheFlowDocumentSurfaceTheEditorUses() => UiThread.Run(() =>
    {
        var document = MarkdownFlowDocument.Build(Document, MarkdownPalette.Dark);

        var scores = document.Blocks
            .SelectMany(b => b is BlockUIContainer { Child: { } child } ? Descendants(child) : [])
            .OfType<AbcElement>()
            .ToList();

        Assert.AreEqual(1, scores.Count, "the editor's surface has to render it too, or the caret has nothing to enter");
    });

    [TestMethod]
    public void AndTheTuneKnowsWhereItSitsInTheBlockAroundIt() => UiThread.Run(() =>
    {
        var block = Markdig.Markdown.Parse(Document, MarkdownPipelineFactory.Default)
            .OfType<Markdig.Syntax.FencedCodeBlock>()
            .Single();

        var score = Inside(BlockRenderer.Render(block, Document, MarkdownPalette.Dark))!;
        var editable = (IEditableBlock)score;

        // Whatever the host splices, the tune has to sit exactly where the element says it does.
        Assert.AreEqual(score.Source, Document.Substring(editable.SourceStart, score.Source.Length));
    });

    private static AbcElement? Inside(DependencyObject root) => Descendants(root).OfType<AbcElement>().FirstOrDefault();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return child;

        if (root is FrameworkElement { } element)
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
                foreach (var deeper in Descendants(child))
                    yield return deeper;
    }
}
