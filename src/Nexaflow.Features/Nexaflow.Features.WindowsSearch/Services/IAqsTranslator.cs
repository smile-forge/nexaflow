namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Turns a fragment of Advanced Query Syntax — <c>kind:document</c>, <c>size:&gt;1mb</c>,
/// <c>modified:last week</c> — into a SQL WHERE fragment for SystemIndex.
/// <para>
/// An interface because the real implementation is COM (<c>IQueryParser</c> → <c>ICondition</c>) and
/// depends on the Windows Search service being present. Everything above it — recognising which token is
/// AQS, assembling terms, building the rest of the query — stays pure and unit-testable against a fake.
/// </para>
/// </summary>
public interface IAqsTranslator
{
    /// <summary>
    /// True when <paramref name="token"/> looks like a property constraint this can translate. Cheap and
    /// side-effect free: it is asked about every token of every query.
    /// </summary>
    bool Recognises(string token);

    /// <summary>
    /// The WHERE fragment for <paramref name="token"/>, or null when it can't be translated — an unknown
    /// property, an unparseable value, or the service being unavailable. Null means "drop this constraint
    /// and say so", never "match everything".
    /// </summary>
    string? ToWhereClause(string token);
}
