using System;
using System.Collections.Generic;
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
/// gestures: its source, its layout, what is selected inside it, what could not be read, and a caret that
/// can be handed in at an edge or handed back out.
/// <para>
/// This is the whole of the seam between prose and rendered content. A document that can drive it can
/// select across a formula, arrow into and out of one, and show what is wrong inside it — without knowing
/// what a formula is. When the score and the diagrams implement it they inherit all of that unchanged,
/// which is the point of writing it here rather than in the formula.
/// </para>
/// </summary>
public interface IEditableBlock : IInteractiveBlock
{
    /// <summary>The source this block stands for — what a selection over it yields.</summary>
    string Source { get; }

    /// <summary>Its layout, for the shared queries, or null while none of it could be laid out.</summary>
    ILayoutNode? Root { get; }

    /// <summary>What is selected inside it, in its own source's offsets.</summary>
    IReadOnlyList<(int Start, int Length)> Selection { get; }

    /// <summary>Whatever could not be read — what the host draws a wave under.</summary>
    IReadOnlyList<Diagnostic> Diagnostics { get; }

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
}
