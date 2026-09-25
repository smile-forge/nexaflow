namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A parse-tree part, seen as an editor needs to see it: the stretch of source it stands for.
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
/// <para>
/// <see cref="ContentPart"/> implements it directly. A language whose parts are <em>named by</em>
/// something other than the characters they span — a braced argument named by its contents, so replacing
/// it does not re-brace what is already braced — wraps one in an adapter of its own instead, because
/// that narrowing is a convention about editing rather than a fact about the content.
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
/// A stretch of source that no one part of the tree names — one a language's reader works out as it reads, such as a word
/// inside a label, or one the editing layer measures.
///
/// <para>
/// It is not a licence to skip the parse tree. A part is the answer wherever one exists, because a part survives an edit and a
/// pair of numbers does not; and a builder never makes one — a piece of layout standing for several parts names them
/// (<see cref="PartRun"/>).
/// </para>
/// </summary>
public readonly record struct SourceSpan(int Start, int Length) : ISourcePart;

/// <summary>
/// A piece of layout standing for several parts that no one part holds — a bar's notes, a beam's, a section's bars, the letters
/// of a title: named by those parts, and reaching over them.
///
/// <para>
/// What is handed over is the parts, never a position: where the run starts and how far it reaches is worked out here from
/// the parts themselves, so it goes on standing for them when an edit moves them.
/// </para>
/// </summary>
public sealed record PartRun(IReadOnlyList<ISourcePart> Parts) : ISourcePart
{
    public int Start => this.Parts.Min(part => part.Start);

    public int Length => this.Parts.Max(part => part.End()) - this.Start;

    /// <summary>The run of those of <paramref name="parts"/> that are there, or null where none is.</summary>
    public static ISourcePart? Of(IEnumerable<ISourcePart?> parts) =>
        parts.OfType<ISourcePart>().ToList() is { Count: > 0 } named ? new PartRun(named) : null;
}

/// <summary>
/// Where a piece of layout sits in the source — a stretch of it, or a point in it.
///
/// <para>
/// Derived, never stored. It is worked out from the part a piece was drawn from, or, for a piece drawn
/// from nothing anybody wrote, from the part it was drawn inside. Keeping it on the layout instead would
/// be a second copy of a fact the parse tree already holds, and the two go out of step the moment
/// anything is edited — which is the whole reason the layout is built from a parse tree in the first
/// place.
/// </para>
/// </summary>
public readonly record struct SourcePlace(int Start, int Length)
{
    /// <summary>One past the last source character.</summary>
    public int End => Start + Length;
}

/// <summary>Convenience over <see cref="ISourcePart"/>, so the arithmetic is written once.</summary>
public static class SourcePartExtensions
{
    /// <summary>One past the last source character this part is named by.</summary>
    public static int End(this ISourcePart part) => part.Start + part.Length;

    /// <summary>Whether this part's stretch of source wholly contains another's.</summary>
    public static bool Covers(this ISourcePart part, ISourcePart other) =>
        other.Start >= part.Start && other.End() <= part.End();
}
