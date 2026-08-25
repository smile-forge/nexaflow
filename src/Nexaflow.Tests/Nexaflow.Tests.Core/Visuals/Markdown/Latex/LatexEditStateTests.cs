using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// Coverage for <see cref="LatexEditState"/> — what typing, committing and deleting actually do to a
/// formula's source.
///
/// These are the editing rules stated as rules. No control, no layout, no STA thread: the state machine
/// was kept a pure value precisely so that "backspace behind α gives you back \alpha" can be asserted as
/// a fact rather than inferred from a screenshot.
/// </summary>
[TestClass]
[CoversNode("latex-editing")]
public class LatexEditStateTests
{
    private static LatexEditState Typed(string text, LatexEditState? from = null)
    {
        var state = from ?? LatexEditState.For(string.Empty);
        foreach (var character in text) state = state.Type(character);
        return state;
    }

    // ── Typing a command ────────────────────────────────────────────────────

    [TestMethod]
    public void AHalfTypedCommandIsShownAsItself()
    {
        var state = Typed(@"\alp");

        Assert.AreEqual(@"\alp", state.Latex);
        Assert.AreEqual(@"\alp", state.RawText,
            "a command being written is shown literally, not put through four failing parses");
        Assert.AreEqual("", state.Committed, "and the rest of the formula is what still typesets");
    }

    [TestMethod]
    public void SpaceSettlesTheCommandAndIsKept()
    {
        var state = Typed(@"\alpha").Commit();

        Assert.AreEqual(@"\alpha ", state.Latex,
            "the space has to survive, or \\alpha x becomes the unknown command \\alphax");
        Assert.IsNull(state.Raw, "and the command is no longer raw");
    }

    [TestMethod]
    public void ANonLetterEndsTheCommandByItself()
    {
        // TeX reads a control word as a backslash and letters; the first non-letter terminates it.
        var state = Typed(@"\alpha+");

        Assert.AreEqual(@"\alpha+", state.Latex);
        Assert.IsNull(state.Raw, "the plus finished the command without needing a space");
    }

    [TestMethod]
    public void OnlyABackslashStartsARawZone()
    {
        var state = Typed("x+2");

        Assert.AreEqual("x+2", state.Latex);
        Assert.IsNull(state.Raw, "ordinary maths typesets as you type — there is nothing to hold back");
    }

    [TestMethod]
    public void TypingContinuesInsideAnExistingFormula()
    {
        var state = LatexEditState.For(@"\frac{}{2}").MoveCaretTo(6);   // in the numerator
        state = Typed("x", state);

        Assert.AreEqual(@"\frac{x}{2}", state.Latex);
        Assert.AreEqual(7, state.Caret);
    }

    // ── Backspace ───────────────────────────────────────────────────────────

    [TestMethod]
    public void BackspaceBehindARenderedCommandUnRendersIt()
    {
        // The caret sits after an α that six characters produced. Deleting one of them would leave
        // \alph — a formula the user never wrote.
        var state = LatexEditState.For(@"\alpha").Backspace((0, 6));

        Assert.AreEqual(@"\alpha", state.Latex, "nothing is deleted — it is shown, not removed");
        Assert.AreEqual(@"\alpha", state.RawText);
    }

    [TestMethod]
    public void ASecondBackspaceThenDeletesACharacter()
    {
        var state = LatexEditState.For(@"\alpha").Backspace((0, 6)).Backspace();

        Assert.AreEqual(@"\alph", state.Latex, "once it is on show, backspace is just backspace");
        Assert.AreEqual(@"\alph", state.RawText);
    }

    [TestMethod]
    public void BackspaceBehindAPlainCharacterJustDeletes()
    {
        var state = LatexEditState.For("x+2").Backspace();

        Assert.AreEqual("x+", state.Latex);
        Assert.AreEqual(2, state.Caret);
        Assert.IsNull(state.Raw);
    }

    [TestMethod]
    public void UnRenderingTheLastCharacterClosesTheZone()
    {
        var state = LatexEditState.For(@"\a").Backspace((0, 2)).Backspace().Backspace();

        Assert.AreEqual("", state.Latex);
        Assert.IsNull(state.Raw, "with nothing raw left there is nothing to hold open");
    }

    [TestMethod]
    public void BackspaceAtTheStartDoesNothing() =>
        Assert.AreEqual("x", LatexEditState.For("x").MoveCaretTo(0).Backspace().Latex);

    // ── Selection ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TypingOverASelectionReplacesIt()
    {
        var state = Typed("y", LatexEditState.For("x+2").Select(0, 1));

        Assert.AreEqual("y+2", state.Latex);
        Assert.IsFalse(state.HasSelection);
    }

    [TestMethod]
    public void BackspaceOverASelectionRemovesIt()
    {
        var state = LatexEditState.For(@"\frac{x}{2}").Select(6, 1).Backspace();

        Assert.AreEqual(@"\frac{}{2}", state.Latex);
        Assert.AreEqual(6, state.Caret, "and leaves the caret where the selection was");
    }

    // ── Palette insertion ───────────────────────────────────────────────────

    [TestMethod]
    public void ATemplateLeavesTheCaretInsideIt()
    {
        // This is what the Solver's palette needs: \frac{}{} should land you in the numerator.
        var state = LatexEditState.For("").Insert(@"\frac{}{}", caretBack: 3);

        Assert.AreEqual(@"\frac{}{}", state.Latex);
        Assert.AreEqual(6, state.Caret);
        Assert.AreEqual("{", state.Latex[state.Caret - 1].ToString(), "the caret is just inside the numerator");
    }

    [TestMethod]
    public void InsertingWrapsWhatIsSelected()
    {
        var state = LatexEditState.For("x+2").Select(0, 3).Wrap(@"\sqrt{", "}");

        Assert.AreEqual(@"\sqrt{x+2}", state.Latex);
        Assert.AreEqual(9, state.Caret, "the caret ends inside the wrapper, ready to keep typing");
    }

    [TestMethod]
    public void InsertingSettlesAHalfTypedCommand()
    {
        // Reaching for the palette mid-command is a decision to stop typing that command.
        var state = Typed(@"\alp").Insert(@"\beta ");

        Assert.AreEqual(@"\alp\beta ", state.Latex);
        Assert.IsNull(state.Raw);
    }

    // ── The committed view ──────────────────────────────────────────────────

    [TestMethod]
    public void TheSurroundingFormulaStaysTypesetWhileACommandIsWritten()
    {
        var state = Typed(@"\alp", LatexEditState.For("x+2").MoveCaretTo(2));

        Assert.AreEqual(@"x+\alp2", state.Latex);
        Assert.AreEqual("x+2", state.Committed,
            "the part that still typesets is the whole formula minus what is being written");
    }

    [TestMethod]
    public void OffsetsMapOnToTheCommittedText()
    {
        var state = Typed(@"\alp", LatexEditState.For("x+2").MoveCaretTo(2));

        Assert.AreEqual(1, state.ToCommitted(1), "before the zone, nothing moves");
        Assert.AreEqual(2, state.ToCommitted(4), "inside it, everything collapses to where it starts");
        Assert.AreEqual(3, state.ToCommitted(7), "after it, offsets shift back by its length");
    }
}
