namespace Nexaflow.Markdown.Ast;

/// <summary>
/// What writing into a tree produced: the tree, and where in what it prints as the writing landed.
///
/// <para>
/// Where it landed is not the length of what was handed in, and cannot be worked out from it. Text
/// written against a command's name gains a space to keep that name whole; a note given a second octave
/// mark grows by a character the caller never typed — so only the edit knows what it really wrote, and
/// saying so here is what stops a caller re-deriving it from the shape of a change it did not make.
/// </para>
/// </summary>
/// <param name="Tree">
/// The content as it now stands — <strong>provisional</strong>, and meant to be printed and read back
/// rather than built from. The stages between the parser and the builder do not re-derive themselves when
/// a tree is changed underneath them, so an edit prints, and the source it prints as is read back and
/// built from.
/// </param>
/// <param name="Start">Where the writing begins in what <paramref name="Tree"/> prints as.</param>
/// <param name="Length">How much of it the writing is.</param>
/// <param name="Reshaped">
/// Whether holding the writing took more than putting it there. A caller with rules of its own about
/// plain typing wants to know: where nothing was reshaped, its own rules should have the keystroke.
/// </param>
public readonly record struct AstWrite(ContentNode Tree, int Start, int Length, bool Reshaped)
{
    /// <summary>One past the last character written.</summary>
    public int End => this.Start + this.Length;
}
