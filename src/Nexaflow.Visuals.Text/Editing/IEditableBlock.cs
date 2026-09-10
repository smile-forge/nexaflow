using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>Which way the caret was travelling when it ran out of block.</summary>
public enum BlockExit
{
    /// <summary>Off the front — the host should put the caret before this block.</summary>
    Before,

    /// <summary>Off the back — the host should put the caret after it.</summary>
    After,
}

/// <summary>What kind of move brought the caret to a block.</summary>
public enum CaretStep
{
    /// <summary>A character step — left or right along the content.</summary>
    Character,

    /// <summary>A line step — up or down onto the block.</summary>
    Line,
}

/// <summary>
/// The caret crossing into a block from the prose around it.
/// <para>
/// It carries how the reader got there, because that is the only thing that makes the crossing
/// invisible. A block handed nothing but "you have the caret" can only guess, and every guess is a jump
/// nobody asked for. The two kinds of move want different answers: stepping <em>along</em> the text puts
/// the caret on the character you stepped onto, so coming back leftwards lands at the block's end, while
/// stepping <em>onto a line</em> puts it where that line begins, whichever direction you came from.
/// </para>
/// </summary>
/// <param name="Edge">
/// The edge it came in over: <see cref="BlockExit.Before"/> when the caret was in the content ahead of
/// the block and moved forward into it, <see cref="BlockExit.After"/> when it came back from behind.
/// </param>
/// <param name="Step">Whether the reader moved along the text or onto a new line.</param>
/// <param name="Column">
/// Where along the edge the caret was, as an x in the block's own coordinates, for a
/// <see cref="CaretStep.Line"/> arrival. Null for a character step, which has no column to keep. Offered
/// because only the host can know it; a block wide enough for it to mean something — a score, a
/// diagram — may use it, and one that reads as a single run of content ignores it.
/// </param>
public readonly record struct CaretArrival(BlockExit Edge, CaretStep Step, double? Column);

/// <summary>
/// What a piece of rendered content offers the document holding it, beyond owning its own pointer
/// gestures: its source, its layout, what is selected inside it, what could not be read, a caret that can
/// be handed in at an edge or handed back out, and the keys that change it.
/// <para>
/// This is the whole of the seam between prose and rendered content. A document that can drive it can
/// select across a formula, arrow into and out of one, type into it, and show what is wrong inside it —
/// without knowing what a formula is. A barcode implements the same members and is driven by the same
/// host code unchanged, which is the point of writing it here rather than in the formula.
/// </para>
/// <para>
/// What is declared below rather than assumed is only what is genuinely not shared:
/// moving between the parts of a fraction, tabbing through the holes of a half-written construct,
/// settling a command with a space. A block whose content is one run of characters has none of those to
/// want, so those keys fall back to the document rather than being swallowed by a block with no use
/// for them.
/// </para>
/// </summary>
public interface IEditableBlock : IInteractiveBlock
{
    /// <summary>The source this block stands for — what a selection over it yields.</summary>
    string Source { get; }

    /// <summary>
    /// Its layout, for the shared queries. Nothing at all — see <see cref="Piece.Exists"/> — while none
    /// of it could be laid out.
    /// </summary>
    Piece Root { get; }

    /// <summary>What is selected inside it, in its own source's offsets.</summary>
    IReadOnlyList<(int Start, int Length)> Selection { get; }

    /// <summary>Whatever could not be read — what the host draws a wave under.</summary>
    IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Raised when the reader's own editing changed <see cref="Source"/>. The host answers by putting the
    /// new source back into the markdown the block came from — see <see cref="SourceStart"/>.
    /// <para>
    /// A block names this whatever its content is called (a formula's <c>LatexChanged</c>, a barcode's
    /// <c>ValueChanged</c>); this is the seam's name for it, and what the host listens to.
    /// </para>
    /// </summary>
    event EventHandler? SourceChanged;

    /// <summary>
    /// Where <see cref="Source"/> sits inside the markdown block that produced this one — what the host
    /// needs to splice an edit back where it came from. Negative when the whole block <em>is</em> this
    /// content, as a <c>$$…$$</c> block is, and there is nothing to splice around.
    /// </summary>
    int SourceStart { get; }

    /// <summary>Types a character at the caret, replacing whatever is selected.</summary>
    void Type(char character);

    /// <summary>
    /// Deletes backwards from the caret, or deletes the selection. False when there was nothing to
    /// delete, which is the block saying the key was never its to take.
    /// </summary>
    bool Backspace();

    /// <summary>Deletes forwards, or deletes the selection. False when there was nothing to delete.</summary>
    bool Delete();

    /// <summary>
    /// Moves the caret one stop, extending the selection behind it when asked. False when it ran off an
    /// end, having raised <see cref="Exited"/> — the host then decides where the caret really goes.
    /// </summary>
    bool MoveCaret(bool forward, bool extend);

    /// <summary>
    /// Selects a stretch of its source. How a selection sweeping across the document reaches inside a
    /// block it only partly covers; the block still decides what that range really means, so a range
    /// clipping a construct comes back as the whole construct.
    /// </summary>
    void SelectRange(int start, int length);

    /// <summary>
    /// Takes the caret from the prose beside it, at the place the reader was coming from — see
    /// <see cref="CaretArrival"/>. This is what makes arrowing into rendered content feel like arrowing
    /// through text rather than like landing in it.
    /// </summary>
    void TakeCaretArriving(CaretArrival arrival);

    /// <summary>
    /// Gives the caret back, because something else now has it. The pair to
    /// <see cref="TakeCaretArriving"/>: exactly one thing on the page draws a caret, and a block that
    /// kept drawing one after losing it would leave the reader two to choose between.
    /// </summary>
    void ReleaseCaret();

    /// <summary>
    /// Raised when a caret movement ran off an end. The host answers by putting the caret in whatever sits
    /// on that side — the block has no idea what surrounds it.
    /// </summary>
    event EventHandler<BlockExit>? Exited;

    // ── Keys that are genuinely not shared ──────────────────────────────────
    //
    // Everything above is what any rendered content wants. What follows is what only some of it wants —
    // moving between the parts of a fraction, settling a command with a space, tabbing through the holes
    // of a half-written construct, moving a note an octave. These used to be `is FormulaElement` tests in
    // the host, which was honest while a formula was the only block with keys of its own and stopped being
    // so at the second; the host asks none of them now. Defaulted to declining, so a block with no use
    // for one never has to say so, and
    // the key falls back to the document exactly as it did.

    /// <summary>
    /// A key the block wants before the shared handling gets it. False leaves it to the host.
    /// <para>
    /// Asked first, and asked of every block, so a content type can claim a key nothing else uses — a
    /// score claims Page Up and Page Down to move a note an octave — without the host learning what a
    /// note is.
    /// </para>
    /// </summary>
    bool HandleKey(Key key, ModifierKeys modifiers) => false;

    /// <summary>Moves the caret onto the row above or below inside the content. False if there is none.</summary>
    bool MoveCaretVertically(bool up, bool extend) => false;

    /// <summary>
    /// Settles what is being typed, optionally writing <paramref name="text"/> as it goes — a space that
    /// finishes a command name, an Enter that finishes the construct. False when there was nothing to
    /// settle.
    /// </summary>
    bool Commit(string text) => false;

    /// <summary>Selects the next place something still has to be written. False when there are none.</summary>
    bool SelectNextPlaceholder(bool forward) => false;

    /// <summary>
    /// A small ribbon of the actions this block offers on what is selected, for the host to show where the
    /// reader right-clicked. Null from anything with none, which is what puts the ordinary text menu back.
    /// </summary>
    FrameworkElement? BuildRibbon() => null;
}
