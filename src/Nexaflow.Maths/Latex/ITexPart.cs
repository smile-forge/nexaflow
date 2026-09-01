using System.Collections.Generic;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// A formula's parts as anything <em>building</em> from them is allowed to see them: what each part is,
/// what it is to the thing holding it, and what is under it. No positions, and no source.
///
/// <para>
/// The layout tree may never name a point in the source text, and the surest way to hold a rule like that
/// is to make breaking it impossible rather than forbidden. A builder handed one of these has no offset
/// to copy and no formula text to read, so "it never does" stops being something to remember and becomes
/// something the type says. The parse tree itself stays read-write — an editor rewrites it all day — but
/// rewriting it is not a builder's job either, and this offers no way to.
/// </para>
/// <para>
/// <see cref="Text"/> is the node's own text and never a stretch of the formula: the <c>\frac</c> of a
/// fraction, the <c>a</c> of a letter, the <c>^</c> of a script. Identity, not position — which is
/// precisely what a builder needs in order to know what to make, and all of what it needs.
/// </para>
/// <para>
/// Where a position is genuinely wanted — an editor mapping a click to a caret — it is asked of
/// <see cref="TexPart"/> at the time of asking, which is the only time it can be right.
/// </para>
/// </summary>
public interface ITexPart
{
    /// <summary>What this piece is.</summary>
    TexKind Kind { get; }

    /// <summary>What it is to the thing holding it.</summary>
    string Role { get; }

    /// <summary>Its own text: a command's name, a character, a brace.</summary>
    string Text { get; }

    /// <summary>
    /// Why the reading gave up on this part, or null where it did not.
    /// <para>
    /// Not a position and not source text, so it belongs here: it is what this part <em>is</em>. A builder
    /// needs it for one thing only — to know the reading has already accounted for this piece, so that a
    /// name nothing has heard of is reported once rather than again by whoever next fails to draw it.
    /// </para>
    /// </summary>
    string? Trouble { get; }

    /// <summary>
    /// This part written back out — its own text and everything under it, in order.
    /// <para>
    /// Not a slice of anything: the tree owns its text, so this is built up from the nodes rather than
    /// cut out of a formula. That is what round-tripping means, and it is how a builder reads the one
    /// thing it sometimes has to read whole — an array's <c>{c|c}</c>, a fence's delimiter.
    /// </para>
    /// </summary>
    string Print();

    /// <summary>What holds it, or null for the whole formula.</summary>
    ITexPart? Parent { get; }

    /// <summary>Everything under it, machinery included, in written order.</summary>
    IReadOnlyList<ITexPart> Children { get; }

    /// <summary>
    /// The parts that mean something to this one — everything but the punctuation that makes it what it
    /// is, and the space between them.
    /// </summary>
    IEnumerable<ITexPart> Parts { get; }

    /// <summary>The first part with this role, or null.</summary>
    ITexPart? Part(string role);

    /// <summary>This part and everything under it, outermost first.</summary>
    IEnumerable<ITexPart> SelfAndDescendants();
}
