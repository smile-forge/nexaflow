using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
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
    public void TheProseAroundATuneIsSelectableLikeTheMusic() => UiThread.Run(() =>
    {
        // A title is words a reader wants to copy, and the words around a tune are exactly the ones they
        // reach for. They used to be TextBlocks stacked around the drawing on that reasoning — but a flow
        // document selects an embedded element whole or not at all, so the title was as unreachable as if it
        // had been painted. Engraved into the tree it names the field it came from, like a note head does.
        var tune = "X:1\nT:Speed the Plough\nT:a second title\nR:reel\nC:Trad.\nO:England\n"
                 + "S:Sussex\nK:G\nGABc dedB|\nW:a verse printed under the score\n";

        var layout = AbcBuilder.Build(tune, 700, System.Windows.Media.Brushes.Black, 1.0);

        foreach (var (kind, text) in new[]
        {
            ("title", "Speed the Plough"),
            ("subtitle", "a second title"),
            ("rhythm", "reel"),
            ("credit", "Trad."),
            ("verse", "a verse printed under the score"),
        })
        {
            var piece = layout.Root.SelfAndDescendants()
                .FirstOrDefault(n => n.Kind == kind);

            Assert.IsTrue(piece.Exists, $"nothing was engraved for the {kind}");

            var at = piece.Sits();
            StringAssert.Contains(tune.Substring(at.Start, at.Length), text,
                $"the {kind} names {at.Start}+{at.Length}, which is not where it was written");

            // On the page rather than off the left of it, which is the failure a centred or right-aligned
            // piece has: the text engine is told the column, and where the letters land inside it is worked
            // out separately for the bounds a reader drags across.
            Assert.IsTrue(piece.Bounds.X >= 0 && piece.Bounds.Right <= 700 + 1,
                $"the {kind} was drawn at {piece.Bounds.X:F0}..{piece.Bounds.Right:F0} of a 700-wide page");
        }

        // Where a tune was collected is a fact ABOUT the tune rather than part of it, so it is read and
        // kept — a details panel is the place for it — and it is not drawn.
        Assert.IsFalse(layout.Root.SelfAndDescendants().Any(n => n.Kind == "source"));
        Assert.AreEqual("Sussex", AbcHeader.Of(ContentReading.Of(AbcPipeline.Read(tune))).Source);
    });

    [TestMethod]
    public void AndADragRunsFromTheHeadingIntoTheMusic() => UiThread.Run(() =>
    {
        // The point of engraving the words rather than stacking text around the drawing: one selection
        // model, so the two ends of a drag are the same kind of thing.
        var tune = "X:1\nT:Speed the Plough\nK:G\nGABc dedB|\n";
        var layout = AbcBuilder.Build(tune, 700, System.Windows.Media.Brushes.Black, 1.0);

        var title = layout.Root.SelfAndDescendants().First(n => n.Kind == "title");
        var note = layout.Root.SelfAndDescendants().First(n => n.Kind == "note");

        var swept = ContentSelection.Between(layout.Root, title, note);

        Assert.IsFalse(swept.IsEmpty, "a drag from the title to a note selected nothing");
        Assert.IsTrue(swept.Pieces.Contains(title), "…and it did not include the title it started on");
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
