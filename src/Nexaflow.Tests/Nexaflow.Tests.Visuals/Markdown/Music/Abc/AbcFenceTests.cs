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

    [TestMethod]
    public void TheProseAroundATuneIsTextRatherThanPicture() => UiThread.Run(() =>
    {
        // A title painted into the drawing is words nobody can select, and the words around a tune are
        // exactly the ones a reader wants to copy.
        var tune = "X:1\nT:Speed the Plough\nT:a second title\nR:reel\nC:Trad.\nO:England\n"
                 + "S:Sussex\nK:G\nGABc dedB|\n";

        var element = new AbcScore(tune, MarkdownPalette.Dark, 0);

        var written = Descendants(element).OfType<System.Windows.Controls.TextBlock>()
            .Select(t => t.Text)
            .ToList();

        CollectionAssert.Contains(written, "Speed the Plough");
        CollectionAssert.Contains(written, "a second title");
        CollectionAssert.Contains(written, "reel");
        CollectionAssert.Contains(written, "Trad. (England)", "the composer with the origin in brackets");
        CollectionAssert.Contains(written, "Source: Sussex", "labelled the way an engraver labels it");

        // …and the music is still in there for the caret to find.
        Assert.IsNotNull(Inside(element));
    });

    private static AbcElement? Inside(DependencyObject root) => Descendants(root).OfType<AbcElement>().FirstOrDefault();

    /// <summary>
    /// Everything under a root, by both trees, each thing once.
    /// <para>
    /// Both trees, because an element that has never been measured has no visual children and the block is
    /// only reachable logically — and each thing once, because the two trees overlap and a walk that
    /// followed them independently visits the same element down two paths until the stack runs out.
    /// </para>
    /// </summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>([root]);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node)) continue;
            yield return node;

            // Only a Visual has visual children, and the logical tree holds things that are not one — a
            // Grid's ColumnDefinitions among them, which the visual helper throws on rather than skipping.
            if (node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++) pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            }

            if (node is not FrameworkElement element) continue;
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
                pending.Push(child);
        }
    }
}
