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
    public void TickingWritesTheTickIntoTheSource() => UiThread.Run(() =>
        Assert.AreEqual("- [x] to do\n", Pressed("- [ ] to do\n", box: 0).Markdown));

    [TestMethod]
    public void TickingSomethingAlreadyTickedTakesItBack() => UiThread.Run(() =>
        Assert.AreEqual("- [ ] done\n", Pressed("- [x] done\n", box: 0).Markdown));

    [TestMethod]
    public void ADocumentOnlyBeingReadIsNotWrittenByAPress() => UiThread.Run(() =>
        Assert.AreEqual("- [ ] to do\n", Pressed("- [ ] to do\n", box: 0, readOnly: true).Markdown));

    [TestMethod]
    public void TheBoxPressedIsTheOneThatGetsWritten() => UiThread.Run(() =>
    {
        const string source = "- [ ] to do\n- [x] done\n";

        Assert.AreEqual("- [x] to do\n- [x] done\n", Pressed(source, box: 0).Markdown);
        Assert.AreEqual("- [ ] to do\n- [ ] done\n", Pressed(source, box: 1).Markdown);
    });

    [TestMethod]
    public void TickingIsAnEditTheDocumentIsToldOf() => UiThread.Run(() =>
    {
        var told = 0;

        Pressed("- [ ] to do\n", box: 0, before: element => element.SourceChanged += (_, _) => told++);

        Assert.AreEqual(1, told, "whoever follows the document hears of it, as of any other edit");
    });

    // ── What a word says when the pointer rests on it ───────────────────────

    [TestMethod]
    public void AnAbbreviationSaysWhatItStandsForWhilePointedAt() => UiThread.Run(() =>
    {
        var element = Laid("*[HTML]: HyperText Markup Language\n\nHTML is a thing\n");
        var word = element.Laid.Root.SelfAndDescendants().First(piece => piece.Words?.Glyphs.Text == "HTML");

        element.PointerCursor(Middle(word));

        Assert.AreEqual("HyperText Markup Language", element.Saying);
        Assert.IsNull(element.ToolTip, "said by the element's own tip, which WPF would never look for on a property that changes mid-hover");
    });

    [TestMethod]
    public void AndWordsThatStandForNothingElseSayNothing() => UiThread.Run(() =>
    {
        var element = Laid("*[HTML]: HyperText Markup Language\n\nHTML is a thing\n");
        var abbreviation = element.Laid.Root.SelfAndDescendants().First(piece => piece.Words?.Glyphs.Text == "HTML");
        var plain = element.Laid.Root.SelfAndDescendants().First(piece => piece.Words?.Glyphs.Text.Contains("is a thing") == true);

        element.PointerCursor(Middle(abbreviation));
        element.PointerCursor(Middle(plain));

        Assert.IsNull(element.Saying, "moving off the word takes what it said away");
    });

    [TestMethod]
    public void ThePointerIsABarOnlyOverWriting() => UiThread.Run(() =>
    {
        var element = Laid("Short.\n\n> quoted\n\n```cs\nvar x = 1;\n```\n\nEnd.\n");

        foreach (var words in new[] { "Short.", "quoted", "End." })
        {
            var piece = element.Laid.Root.SelfAndDescendants().First(one => one.Words?.Glyphs.Text == words);

            Assert.AreEqual(System.Windows.Input.Cursors.IBeam, element.PointerCursor(Middle(piece)), $"over '{words}'");
            Assert.AreEqual(System.Windows.Input.Cursors.Arrow,
                            element.PointerCursor(new System.Windows.Point(piece.Bounds.Right + 80, piece.Bounds.Y + (piece.Bounds.Height / 2))),
                            $"on the blank page beside '{words}'");
        }
    });

    [TestMethod]
    public void ADocumentOfNothingButLineEndingsStillHasSomewhereForTheCaret() => UiThread.Run(() =>
    {
        var element = Laid("\n\n");

        element.TakeCaret(1);

        Assert.IsFalse(element.Laid.Root.CaretRect(element.Caret).IsEmpty);
    });

    private static MarkdownElement Laid(string source)
    {
        var element = new MarkdownElement(source, StyleFormat.Dark);
        element.Measure(new System.Windows.Size(480, 2000));
        element.Arrange(new System.Windows.Rect(0, 0, 480, 2000));
        return element;
    }

    private static System.Windows.Point Middle(Piece piece) =>
        new(piece.Bounds.X + (piece.Bounds.Width / 2), piece.Bounds.Y + (piece.Bounds.Height / 2));

    /// <summary>A document laid out, and its <paramref name="box"/>th task box pressed as a reader presses it.</summary>
    private static MarkdownElement Pressed(string source, int box, bool readOnly = false, System.Action<MarkdownElement>? before = null)
    {
        var element = new MarkdownElement(source, StyleFormat.Dark) { IsReadOnly = readOnly };
        element.Measure(new System.Windows.Size(480, 2000));
        element.Arrange(new System.Windows.Rect(0, 0, 480, 2000));
        before?.Invoke(element);

        var tick = element.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == MarkdownPieces.Tick).ElementAt(box);

        element.BeginPointerSelect(new System.Windows.Point(tick.Bounds.X + (tick.Bounds.Width / 2), tick.Bounds.Y + (tick.Bounds.Height / 2)));
        element.EndPointerSelect();

        return element;
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
