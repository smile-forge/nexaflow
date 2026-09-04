namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A parse-tree part, seen as the editor needs to see it: the stretch of source it stands for.
///
/// <para>
/// A piece of layout does not know where it was written — it knows what it was drawn <em>from</em>, and
/// that is what answers the question. Keeping the answer on the piece as well would be a second copy of
/// a fact the tree already holds, and the two go out of step the moment anything is edited.
/// </para>
/// <para>
/// So this is deliberately the whole of it. What a part <em>is</em> — its kind, its role, what it prints
/// as — is each content's own vocabulary and stays there; what every content has in common is that its
/// parts can be pointed at, and an editor working in a document of characters has to be told where.
/// </para>
/// </summary>
public interface ISourcePart
{
    /// <summary>Offset of the first source character this part is named by.</summary>
    int Start { get; }

    /// <summary>How many source characters it is named by. Zero for a part standing for none.</summary>
    int Length { get; }
}

/// <summary>
/// Where a piece of layout sits in the source — a stretch of it, or a point in it.
///
/// <para>
/// Derived, never stored. <see cref="LayoutNodeExtensions.Sits"/> works it out from the part a piece was
/// drawn from, or, for a piece drawn from nothing anybody wrote, from the part it was drawn inside.
/// Keeping it on the layout instead would be a second copy of a fact the parse tree already holds, and
/// the two go out of step the moment anything is edited — which is the whole reason the layout is built
/// from a parse tree in the first place.
/// </para>
/// </summary>
public readonly record struct SourcePlace(int Start, int Length)
{
    /// <summary>One past the last source character.</summary>
    public int End => Start + Length;
}
