using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Parses a fragment of Advanced Query Syntax — <c>kind:document</c>, <c>size:&gt;1mb</c>,
/// <c>modified:last week</c> — into a <see cref="SearchCondition"/> tree.
/// <para>
/// A tree, not a SQL string, and that is the entire design. The same constraint has to reach two very
/// different backends: the index, which wants SQL, and a folder walk, which wants a predicate. Handing
/// out SQL serves only the first, and the second then has nothing to apply — which doesn't fail, it
/// silently matches everything.
/// </para>
/// <para>
/// An interface because the real implementation is COM and needs the Windows Search service. Everything
/// above it — recognising which token is AQS, assembling terms, emitting SQL, walking a folder — stays
/// pure and testable against a hand-built tree.
/// </para>
/// </summary>
public interface IAqsTranslator
{
    /// <summary>
    /// True when <paramref name="token"/> is a property constraint this can parse. Cheap and side-effect
    /// free: it is asked about every token of every query.
    /// </summary>
    bool Recognises(string token);

    /// <summary>
    /// The parsed constraint, or null when it can't be parsed — an unknown property, an unparseable
    /// value, or the service being unavailable. Null means "drop this constraint and say so", never
    /// "match everything".
    /// </summary>
    SearchCondition? Parse(string token);
}
