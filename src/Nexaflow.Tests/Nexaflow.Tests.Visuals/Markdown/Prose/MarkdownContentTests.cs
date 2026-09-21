using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// Writing into a markdown document that is drawn rather than typed out: what taking back a character means where the
/// character is not on the screen, and what pressing a tick means.
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownContentTests
{
    // ── Backspace at the end of a line shows the line ───────────────────────

    [TestMethod]
    public void BackspaceAtTheEndOfATitleShowsTheLineAsItWasWritten()
    {
        // The hashes are not on the screen, so there is nothing there to take back. What a reader at the end of a
        // title is reaching for is the markup.
        var shown = Erase("# Title\n\nwords\n", caret: 7);

        Assert.AreEqual(0, shown?.Raw?.Start);
        Assert.AreEqual(7, shown?.Raw?.End);
    }

    [TestMethod]
    public void AShownLineIsDrawnAsItsCharacters()
    {
        var state = Erase("# Title\n\nwords\n", caret: 7)!;
        var laid = MarkdownBuilder.Lay(state.Source, StyleFormat.Dark, 480, state.Raw);

        var source = laid.Root.SelfAndDescendants().Where(piece => piece.Kind == LayoutText.SourceKind).ToList();

        Assert.AreEqual(1, source.Count);
        Assert.AreEqual("# Title", source[0].Words!.Glyphs.Text);
    }

    [TestMethod]
    public void BackspaceAtTheEndOfALineThatIsDrawnAsItselfTakesBackACharacter()
    {
        // Nothing hidden on it, so the key means what it always means and the element is left to do it.
        Assert.IsNull(Erase("plain words\n", caret: 11));
    }

    [TestMethod]
    public void BackspaceInTheMiddleOfALineTakesBackACharacter()
    {
        Assert.IsNull(Erase("# Title\n", caret: 4));
    }

    [TestMethod]
    public void BackspaceAtTheEndOfAWordSetHeavyShowsTheMarksRoundIt()
    {
        Assert.IsNotNull(Erase("a **bold** word\n", caret: 15)?.Raw);
    }

    [TestMethod]
    public void AShownLineIsTextToItsEndsAndNoFurther()
    {
        var state = new EditState("# Title\n", 0, null, new RawZone(0, 7));
        var content = MarkdownContent.Of(StyleFormat.Dark);

        // At its start there is nothing of it left to take, so the key stops rather than eating the line before it.
        Assert.IsNotNull(content.Erasing(Land(content, state), forward: false));

        // Anywhere inside it, it is ordinary text.
        Assert.IsNull(content.Erasing(Land(content, state with { Caret = 4 }), forward: false));
    }

    [TestMethod]
    public void TypingIntoAShownLineKeepsItShown()
    {
        var state = new EditState("# Title\n", 7, null, new RawZone(0, 7));
        var content = MarkdownContent.Of(StyleFormat.Dark);

        var typed = content.Typing(Land(content, state), "!");

        Assert.AreEqual("# Title!\n", typed?.Source);
        Assert.AreEqual(8, typed?.Raw?.End);
    }

    [TestMethod]
    public void DeleteIsLeftAloneEntirely()
    {
        var content = MarkdownContent.Of(StyleFormat.Dark);
        var state = new EditState("# Title\n", 7);

        Assert.IsNull(content.Erasing(Land(content, state), forward: true));
    }

    // ── Ticking ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickingWritesTheTickIntoTheSource()
    {
        var ticked = MarkdownContent.Ticked(new EditState("- [ ] to do\n", 0), new SourceSpan(2, 3));

        Assert.AreEqual("- [x] to do\n", ticked?.Source);
    }

    [TestMethod]
    public void TickingSomethingAlreadyTickedTakesItBack()
    {
        var ticked = MarkdownContent.Ticked(new EditState("- [x] done\n", 0), new SourceSpan(2, 3));

        Assert.AreEqual("- [ ] done\n", ticked?.Source);
    }

    [TestMethod]
    public void NothingIsWrittenWhereThereIsNoTick()
    {
        Assert.IsNull(MarkdownContent.Ticked(new EditState("- to do\n", 0), new SourceSpan(2, 3)));
        Assert.IsNull(MarkdownContent.Ticked(new EditState("- [ ] to do\n", 0), null));
    }

    [TestMethod]
    public void TheBoxTheBuilderDrewIsTheOneThatGetsWritten()
    {
        // The whole of the arc in one test: the builder says which characters it drew a box over, and what a press on
        // it means is written over exactly those characters.
        const string source = "- [ ] to do\n- [x] done\n";

        var ticks = MarkdownBuilder.Lay(source, StyleFormat.Dark, 480).Root.SelfAndDescendants()
            .Where(piece => piece.Kind == MarkdownPieces.Tick)
            .ToList();

        Assert.AreEqual("- [x] to do\n- [x] done\n",
                        MarkdownContent.Ticked(new EditState(source, 0), ticks[0].Part)?.Source);

        Assert.AreEqual("- [ ] to do\n- [ ] done\n",
                        MarkdownContent.Ticked(new EditState(source, 0), ticks[1].Part)?.Source);
    }

    // ── Reading the answers ─────────────────────────────────────────────────

    private static EditState? Erase(string source, int caret)
    {
        var content = MarkdownContent.Of(StyleFormat.Dark);

        return content.Erasing(Land(content, new EditState(source, caret)), forward: false);
    }

    private static Landing Land(MarkdownContent content, EditState state) =>
        new(state, content.Lay(state, 480, readOnly: false), -1);
}
