using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A markdown document as a block on a page: pressing it.
///
/// <para>
/// One behaviour is the document's own — ticking an item — and the point of these is that it is not a mechanism. It is
/// an edit: the press writes to the source, the source is read back, and the box is drawn from what the tree then
/// says. Nothing anywhere remembers which items are done.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class MarkdownElementTests
{
    private const string Source = "- [ ] to do\n- [x] done\n";

    [TestMethod]
    public void PressingATickTicksTheItem() => UiThread.Run(() =>
    {
        var element = Element();

        element.BeginPointerSelect(Middle(Ticks(element)[0]));

        Assert.AreEqual("- [x] to do\n- [x] done\n", element.Source);
    });

    [TestMethod]
    public void PressingATickedItemTakesTheTickBack() => UiThread.Run(() =>
    {
        var element = Element();

        element.BeginPointerSelect(Middle(Ticks(element)[1]));

        Assert.AreEqual("- [ ] to do\n- [ ] done\n", element.Source);
    });

    [TestMethod]
    public void TheBoxIsDrawnFromTheSourceAndNothingRemembersIt() => UiThread.Run(() =>
    {
        var element = Element();
        Assert.AreEqual("on", Ticks(element)[0].Acts?.Click?.Target, "pressing it would tick it");

        element.BeginPointerSelect(Middle(Ticks(element)[0]));

        // Same item, laid again from a source that now says something else.
        Assert.AreEqual("off", Ticks(element)[0].Acts?.Click?.Target, "and now pressing it would untick it");
    });

    [TestMethod]
    public void PressingATickOfSomethingOnlyBeingLookedAtDoesNothing() => UiThread.Run(() =>
    {
        var element = Element(readOnly: true);

        element.BeginPointerSelect(Middle(Ticks(element)[0]));

        Assert.AreEqual(Source, element.Source);
    });

    [TestMethod]
    public void PressingAnythingElsePutsTheCaretWhereItWasPressed() => UiThread.Run(() =>
    {
        // A press the block does not claim means what a press always means, which is why claiming one has to be
        // deliberate.
        var element = Element();
        var words = element.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == MarkdownPieces.Words && piece.Words!.Glyphs.Text.Contains("do"));

        element.BeginPointerSelect(Middle(words));

        Assert.AreEqual(Source, element.Source, "nothing was written");
        Assert.IsTrue(element.Caret > 0, "and the caret went where it was pressed");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static MarkdownElement Element(bool readOnly = false)
    {
        var element = new MarkdownElement(Source, StyleFormat.Dark) { IsReadOnly = readOnly };

        element.Measure(new Size(400, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 400, element.DesiredSize.Height));

        return element;
    }

    private static List<Piece> Ticks(MarkdownElement element) =>
        [.. element.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == MarkdownPieces.Tick)];

    private static Point Middle(Piece piece) =>
        new(piece.Bounds.X + (piece.Bounds.Width / 2), piece.Bounds.Y + (piece.Bounds.Height / 2));
}
