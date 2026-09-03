using System.Collections.Generic;

namespace XamlMath;

/// <summary>
/// One part a construct is made of, under the name the construct gives it.
/// </summary>
/// <param name="Role">
/// What this part <em>is</em> to the thing holding it — <c>degree</c>, <c>radicand</c>,
/// <c>numerator</c>, <c>base</c>, <c>superscript</c>. Named rather than numbered because a position means
/// nothing on its own: a root with a degree and one without have their contents at different indices, and
/// anything counting would silently read the wrong one.
/// </param>
/// <param name="Node">The part itself.</param>
/// <param name="Row">
/// Which row of a table this part is in, or <c>-1</c> where the holder is not a table.
/// <para>
/// The exception the note above anticipates. For every other construct a position means nothing on its
/// own, which is why the parts are named; for a table the position <em>is</em> the name — the third cell
/// of the second row is what that cell is to the matrix holding it, and there is nothing else to call it.
/// Without this a matrix's cells came back as a flat run of "cell", so nothing downstream could say which
/// column it was looking at, and a reader could not be offered "insert a row above this one".
/// </para>
/// </param>
/// <param name="Column">Which column of a table this part is in, or <c>-1</c>.</param>
public readonly record struct FormulaSlot(string Role, IFormulaNode Node, int Row = -1, int Column = -1);

/// <summary>
/// A node of the parse tree, in as much of it as a reader of a formula needs: what it was built
/// from, and what it is made of.
/// <para>
/// This exists so a consumer can answer "what is this piece <em>to</em> the thing holding it" — which
/// cannot be answered from geometry, or from source offsets, or from anything the layout knows. Copying
/// the <c>3</c> out of <c>\sqrt[3]{x+1}</c> yields a 3, and also yields "the degree of a root"; only the
/// second reading lets pasting it onto something else produce a cube root of that something. That reading
/// is a fact about the parse, so it lives here, on the parse tree, and not on the layout — where it would
/// be a copy free to disagree, and where a fraction's <em>bar</em> would have to be given a role it does
/// not have.
/// </para>
/// <para>
/// Deliberately a projection rather than the atom itself. A consumer wants the shape of the tree, not the
/// typesetting machinery hanging off it, and the atoms stay free to change without breaking anyone.
/// </para>
/// </summary>
public interface IFormulaNode
{
    /// <summary>What it is made of, in reading order. Empty for anything that is made of nothing.</summary>
    IReadOnlyList<FormulaSlot> Slots { get; }

    /// <summary>
    /// The part of the parse tree this was built from, and the only thing here that says where it came
    /// from. There is no offset beside it: an offset stored beside a tree is a second copy of what the
    /// tree already holds, and the two part company the moment anything is edited.
    /// <para>
    /// Every atom <see cref="TexFormulaBuilder"/> makes from a reading carries one, so every box that
    /// comes out of it knows which part of the source it was set from without anything having to match
    /// spans up afterwards. The ones that carry none are the atoms nobody wrote: a short row's cells,
    /// squared off so that "the third column" means the same thing in every row, and the insides of a
    /// macro's expansion, which belong to a definition rather than to the formula.
    /// </para>
    /// <para>
    /// The whole part, because this is the backlink an editor follows and an editor needs where things
    /// are. The narrowing is on the other side: <see cref="TexFormulaBuilder"/> only ever holds these as
    /// <see cref="Nexaflow.Maths.Latex.ITexPart"/>, so it cannot read a position while it builds — but
    /// what it hangs here is the real part, and asking that for a position later is exactly right,
    /// because later is the only time the answer can be current.
    /// </para>
    /// </summary>
    Nexaflow.Maths.Latex.TexPart? Origin { get; }
}
