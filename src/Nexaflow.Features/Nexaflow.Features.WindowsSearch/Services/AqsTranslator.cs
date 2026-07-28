using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// <see cref="IAqsTranslator"/> over Windows' own AQS parser (<c>ISearchQueryHelper</c>).
/// <para>
/// Delegating rather than re-implementing is the whole point: the property vocabulary is hundreds of
/// names and aliases, sizes carry suffixes, and dates are localised phrases. Windows already parses all
/// of it exactly as Explorer's search box does, so <c>kind:document</c> means here what it means there.
/// </para>
/// <para>
/// Every failure — no Windows Search service, an unknown property, an unparseable value — surfaces as
/// "not recognised" or a null clause, never as an exception and never as a guessed clause. A dropped
/// constraint only widens the index query, and the post-filter still applies the full search.
/// </para>
/// </summary>
public sealed partial class AqsTranslator : IAqsTranslator, IDisposable
{
    // A concrete property reference is what distinguishes a constraint from prose. AQS renders free
    // text as CONTAINS(*, …) with no property named, so "notaproperty:x" — which it treats as text —
    // produces no match here. System.Search.Contents is excluded for the same reason: it IS free text.
    [GeneratedRegex(@"System\.(?!Search\.Contents\b)[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*",
        RegexOptions.Compiled)]
    private static partial Regex PropertyReference { get; }

    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private ISearchQueryHelper? _helper;
    private bool _initialised;
    private bool _disposed;

    /// <summary>True when the indexer answered at all — false on a machine with Windows Search
    /// disabled, where every constraint is simply dropped.</summary>
    public bool IsAvailable
    {
        get { lock (_gate) return Helper() is not null; }
    }

    public bool Recognises(string token) => Translate(token) is not null;

    public string? ToWhereClause(string token) => Translate(token);

    /// <summary>The WHERE fragment for a single token, or null when it isn't a property constraint we
    /// can translate. Cached — <see cref="Recognises"/> is asked about every token of every query, and
    /// the answer for a given token never changes within a session.</summary>
    private string? Translate(string token)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(token, out var cached)) return cached;

            var clause = TryGenerate(token, out var sql) && TryExtractWhere(sql, out var where)
                         && PropertyReference.IsMatch(where)
                ? where
                : null;

            _cache[token] = clause;
            return clause;
        }
    }

    private bool TryGenerate(string token, [NotNullWhen(true)] out string? sql)
    {
        sql = null;
        var helper = Helper();
        if (helper is null) return false;

        try
        {
            sql = helper.GenerateSQLFromUserQuery(token);
            return !string.IsNullOrWhiteSpace(sql);
        }
        catch (COMException)
        {
            // A malformed constraint is a normal outcome of someone typing, not an error to report.
            return false;
        }
    }

    /// <summary>
    /// The restriction out of a generated <c>SELECT … FROM SystemIndex WHERE … [ORDER BY …]</c>.
    /// <para>
    /// Split on the last WHERE, not the first: a constraint whose value contains the word (a search for
    /// a file named "where") would otherwise truncate the clause and silently change its meaning.
    /// </para>
    /// </summary>
    private static bool TryExtractWhere(string sql, [NotNullWhen(true)] out string? where)
    {
        where = null;

        var at = sql.LastIndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;

        var rest = sql[(at + " WHERE ".Length)..];

        var order = rest.LastIndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
        if (order >= 0) rest = rest[..order];

        where = rest.Trim();
        return where.Length > 0;
    }

    /// <summary>The query helper, created once. A failure is remembered so a machine without the
    /// service doesn't pay for a COM activation on every keystroke.</summary>
    private ISearchQueryHelper? Helper()
    {
        if (_initialised) return _helper;
        _initialised = true;

        try
        {
            var manager = (ISearchManager)new CSearchManager();
            _helper = manager.GetCatalog("SystemIndex").GetQueryHelper();

            // The default is natural-language, which reads "kind:document" as prose.
            _helper.put_QuerySyntax(SearchQuerySyntax.Advanced);
            // Anything the helper was carrying would otherwise be AND-ed into every fragment we extract.
            _helper.put_QueryWhereRestrictions(null);
        }
        catch (COMException)
        {
            _helper = null;
        }
        catch (InvalidCastException)
        {
            _helper = null;
        }

        return _helper;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_helper is not null) Marshal.ReleaseComObject(_helper);
            _helper = null;
            _initialised = true;
        }
    }
}
