using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Markdown.Source;

/// <summary>
/// Coverage for <see cref="EditState"/> — what typing, committing and deleting actually do to a
/// formula's source.
///
/// These are the editing rules stated as rules. No control, no layout, no STA thread: the state machine
/// was kept a pure value precisely so that "backspace behind α gives you back \alpha" can be asserted as
/// a fact rather than inferred from a screenshot.
/// </summary>
[TestClass]
[CoversNode("latex-editing")]
public class EditStateTests
{
    private static EditState Typed(string text, EditState? from = null)
    {
        var state = from ?? EditState.For(string.Empty);
        foreach (var character in text) state = state.Typing(character) ?? state.Type(character);
        return state;
    }

    // ── Typing a command ────────────────────────────────────────────────────

    [TestMethod]
    public void AHalfTypedCommandIsShownAsItself()
    {
        var state = Typed(@"\alp");

        Assert.AreEqual(@"\alp", state.Source);
        Assert.AreEqual(@"\alp", state.RawText,
            "a command being written is shown literally, not put through four failing parses");
    }

    [TestMethod]
    public void SpaceSettlesTheCommandAndIsKept()
    {
        // Typing the space IS settling it. There is no second way to say so: a non-letter ends a control
        // word, which is the same rule that ends it when the next thing typed is a plus.
        var state = Typed(@"\alpha ");

        Assert.AreEqual(@"\alpha ", state.Source,
            "the space has to survive, or \\alpha x becomes the unknown command \\alphax");
        Assert.IsNull(state.Raw, "and the command is no longer raw");
    }

    [TestMethod]
    public void ANonLetterEndsTheCommandByItself()
    {
        // TeX reads a control word as a backslash and letters; the first non-letter terminates it.
        var state = Typed(@"\alpha+");

        Assert.AreEqual(@"\alpha+", state.Source);
        Assert.IsNull(state.Raw, "the plus finished the command without needing a space");
    }

    [TestMethod]
    public void OnlyABackslashStartsARawZone()
    {
        var state = Typed("x+2");

        Assert.AreEqual("x+2", state.Source);
        Assert.IsNull(state.Raw, "ordinary maths typesets as you type — there is nothing to hold back");
    }

    [TestMethod]
    public void TypingContinuesInsideAnExistingFormula()
    {
        var state = EditState.For(@"\frac{}{2}").MoveCaretTo(6);   // in the numerator
        state = Typed("x", state);

        Assert.AreEqual(@"\frac{x}{2}", state.Source);
        Assert.AreEqual(7, state.Caret);
    }

    // ── Backspace ───────────────────────────────────────────────────────────

    [TestMethod]
    public void BackspaceBehindARenderedCommandUnRendersIt()
    {
        // The caret sits after an α that six characters produced. Deleting one of them would leave
        // \alph — a formula the user never wrote.
        var state = EditState.For(@"\alpha").Backspace((0, 6));

        Assert.AreEqual(@"\alpha", state.Source, "nothing is deleted — it is shown, not removed");
        Assert.AreEqual(@"\alpha", state.RawText);
    }

    [TestMethod]
    public void ASecondBackspaceThenDeletesACharacter()
    {
        var state = EditState.For(@"\alpha").Backspace((0, 6)).Backspace();

        Assert.AreEqual(@"\alph", state.Source, "once it is on show, backspace is just backspace");
        Assert.AreEqual(@"\alph", state.RawText);
    }

    [TestMethod]
    public void BackspaceBehindAPlainCharacterJustDeletes()
    {
        var state = EditState.For("x+2").Backspace();

        Assert.AreEqual("x+", state.Source);
        Assert.AreEqual(2, state.Caret);
        Assert.IsNull(state.Raw);
    }

    [TestMethod]
    public void UnRenderingTheLastCharacterClosesTheZone()
    {
        var state = EditState.For(@"\a").Backspace((0, 2)).Backspace().Backspace();

        Assert.AreEqual("", state.Source);
        Assert.IsNull(state.Raw, "with nothing raw left there is nothing to hold open");
    }

    [TestMethod]
    public void BackspaceAtTheStartDoesNothing() =>
        Assert.AreEqual("x", EditState.For("x").MoveCaretTo(0).Backspace().Source);

    // ── Selection ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TypingOverASelectionReplacesIt()
    {
        var state = Typed("y", EditState.For("x+2").Select(0, 1));

        Assert.AreEqual("y+2", state.Source);
        Assert.IsFalse(state.HasSelection);
    }

    [TestMethod]
    public void BackspaceOverASelectionRemovesIt()
    {
        var state = EditState.For(@"\frac{x}{2}").Select(6, 1).Backspace();

        Assert.AreEqual(@"\frac{}{2}", state.Source);
        Assert.AreEqual(6, state.Caret, "and leaves the caret where the selection was");
    }

    // ── Palette insertion ───────────────────────────────────────────────────

    [TestMethod]
    public void ATemplateLeavesTheCaretInsideIt()
    {
        // This is what the Solver's palette needs: \frac{}{} should land you in the numerator.
        var state = EditState.For("").Insert(@"\frac{}{}", caretBack: 3);

        Assert.AreEqual(@"\frac{}{}", state.Source);
        Assert.AreEqual(6, state.Caret);
        Assert.AreEqual("{", state.Source[state.Caret - 1].ToString(), "the caret is just inside the numerator");
    }

    [TestMethod]
    public void InsertingWrapsWhatIsSelected()
    {
        var state = EditState.For("x+2").Select(0, 3).Wrap(@"\sqrt{", "}");

        Assert.AreEqual(@"\sqrt{x+2}", state.Source);
        Assert.AreEqual(9, state.Caret, "the caret ends inside the wrapper, ready to keep typing");
    }

    [TestMethod]
    public void InsertingSettlesAHalfTypedCommand()
    {
        // Reaching for the palette mid-command is a decision to stop typing that command.
        var state = Typed(@"\alp").Insert(@"\beta ");

        Assert.AreEqual(@"\alp\beta ", state.Source);
        Assert.IsNull(state.Raw);
    }

    // ── What is being written, and where ────────────────────────────────────

    [TestMethod]
    public void WhatIsBeingWrittenIsNamedInTheFormulasOwnOffsets()
    {
        // The zone names a stretch of the source, and the source is the only text there is. There used
        // to be a second one — the formula minus this stretch — with every offset existing in both and
        // a translation at each of two dozen call sites. The typesetter is told which stretch to set as
        // written instead, so the stretch is *in* the formula rather than cut out of it and painted
        // back over the gap, which is what covered whatever followed it.
        var state = Typed(@"\alp", EditState.For("x+2").MoveCaretTo(2));

        Assert.AreEqual(@"x+\alp2", state.Source);
        Assert.IsNotNull(state.Raw);
        Assert.AreEqual(2, state.Raw!.Value.Start, "it starts where the writing started");
        Assert.AreEqual(6, state.Raw!.Value.End, "and ends after what has been written so far");
        Assert.AreEqual(@"\alp", state.RawText);
    }
}
