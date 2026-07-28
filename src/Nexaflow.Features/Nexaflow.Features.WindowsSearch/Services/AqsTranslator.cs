using System.Runtime.InteropServices;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// <see cref="IAqsTranslator"/> over Windows' own structured-query parser (<c>IQueryParser</c>), which
/// hands back a condition tree rather than a string.
/// <para>
/// Delegating rather than re-implementing is the point: the property vocabulary is hundreds of names and
/// aliases, sizes carry suffixes, and dates are localised phrases. Windows already parses all of it
/// exactly as Explorer's search box does, so <c>kind:document</c> means here what it means there.
/// </para>
/// <para>
/// Every failure — no Windows Search service, an unknown property, an unparseable value — surfaces as
/// "not recognised" or a null condition, never as an exception and never as a guess.
/// </para>
/// </summary>
public sealed class AqsTranslator : IAqsTranslator, IDisposable
{
    private static readonly Guid IID_IQueryParser = new("2EBDEE67-3505-43F8-9946-EA44ABC8E5B0");
    private static readonly Guid IID_IEnumUnknown = new("00000100-0000-0000-C000-000000000046");

    /// <summary>
    /// The pseudo-properties a bare word resolves to. Free text IS a valid parse — <c>ocr</c> comes back
    /// as <c>* WordStartsWith "ocr"</c> — but it is not a property constraint, and treating it as one
    /// would swallow every ordinary search word into AQS and hand the post-filter a term it must not
    /// re-test.
    /// </summary>
    private static readonly string[] AllProperties = ["*", "System.Search.Contents"];

    private readonly Dictionary<string, SearchCondition?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private IQueryParser? _parser;
    private bool _initialised;
    private bool _disposed;

    /// <summary>True when the parser could be created at all — false on a machine with Windows Search
    /// disabled, where every constraint is dropped rather than guessed.</summary>
    public bool IsAvailable
    {
        get { lock (_gate) return Parser() is not null; }
    }

    public bool Recognises(string token) => Parse(token) is not null;

    public SearchCondition? Parse(string token)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(token, out var cached)) return cached;

            var condition = TryParse(token);

            // A tree that only ever mentions a free-text pseudo-property is prose, not a constraint.
            if (condition is not null &&
                condition.Properties.All(p => AllProperties.Contains(p, StringComparer.OrdinalIgnoreCase)))
                condition = null;

            _cache[token] = condition;
            return condition;
        }
    }

    private SearchCondition? TryParse(string token)
    {
        var parser = Parser();
        if (parser is null) return null;

        ICondition? root = null;
        ICondition? resolved = null;
        try
        {
            var solution = parser.Parse(token, null);
            if (solution.GetQuery(out root, out var mainType) != 0 || root is null) return null;

            // GetQuery hands back an IEntity we never use; not releasing it leaks on every keystroke.
            if (mainType != IntPtr.Zero) Marshal.Release(mainType);

            // The raw parse is semantic but untyped: "1mb" is still an internal token, "last week" is
            // still a phrase. Resolving turns both into real values against a reference time of now.
            var now = SystemTime.Now();
            if (solution.Resolve(root, SqroDefault, ref now, out resolved) != 0 || resolved is null)
                return null;

            return Convert(resolved, depth: 0);
        }
        catch (COMException)
        {
            // A malformed constraint is a normal outcome of someone typing, not an error to report.
            return null;
        }
        finally
        {
            if (resolved is not null) Marshal.ReleaseComObject(resolved);
            if (root is not null) Marshal.ReleaseComObject(root);
        }
    }

    /// <summary>SQRO_DEFAULT — resolve dates, simplify trees, map relations. Everything the raw parse
    /// leaves undone.</summary>
    private const int SqroDefault = 0;

    /// <summary>Guards against a pathological tree costing more than the search it describes. AQS nesting
    /// is shallow in practice; anything deeper is not a query a person typed.</summary>
    private const int MaxDepth = 32;

    private static SearchCondition? Convert(ICondition condition, int depth)
    {
        if (depth > MaxDepth) return null;

        var type = condition.GetConditionType();

        if (type == ConditionType.Leaf)
        {
            var hr = condition.GetComparisonInfo(out var property, out var operation, out var variant);
            if (hr != 0 || string.IsNullOrEmpty(property)) return null;

            object? value;
            try     { value = PropVariantReader.Read(ref variant); }
            finally { PropVariantReader.Clear(ref variant); }

            return SearchCondition.Leaf(property, Map(operation), value);
        }

        var children = SubConditions(condition)
            .Select(c => Convert(c, depth + 1))
            .ToList();

        // One unreadable child makes the whole branch unsafe to evaluate: dropping it from an AND widens
        // the query, and dropping it from an OR narrows it. Neither is the query that was asked for.
        if (children.Any(c => c is null)) return null;

        var kept = children.Select(c => c!).ToList();
        if (kept.Count == 0) return null;

        return type switch
        {
            ConditionType.And => SearchCondition.And(kept),
            ConditionType.Or  => SearchCondition.Or(kept),
            ConditionType.Not => kept.Count == 1 ? SearchCondition.Not(kept[0]) : null,
            _                 => null,
        };
    }

    private static IEnumerable<ICondition> SubConditions(ICondition condition)
    {
        var iid = IID_IEnumUnknown;
        object raw;
        try     { raw = condition.GetSubConditions(ref iid); }
        catch (COMException) { yield break; }

        if (raw is not IEnumUnknown enumerator) yield break;

        try
        {
            var buffer = new object?[1];
            while (enumerator.Next(1, buffer, out var fetched) == 0 && fetched == 1)
            {
                if (buffer[0] is ICondition child) yield return child;
                buffer[0] = null;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Windows' operator to ours. Anything unrecognised becomes
    /// <see cref="SearchComparison.Unsupported"/> rather than the nearest neighbour — a wrong operator
    /// answers a different question and looks exactly like a working query.
    /// </summary>
    private static SearchComparison Map(ConditionOperation operation) => operation switch
    {
        ConditionOperation.Equal              => SearchComparison.Equal,
        ConditionOperation.NotEqual           => SearchComparison.NotEqual,
        ConditionOperation.LessThan           => SearchComparison.LessThan,
        ConditionOperation.GreaterThan        => SearchComparison.GreaterThan,
        ConditionOperation.LessThanOrEqual    => SearchComparison.LessThanOrEqual,
        ConditionOperation.GreaterThanOrEqual => SearchComparison.GreaterThanOrEqual,
        ConditionOperation.ValueStartsWith    => SearchComparison.StartsWith,
        ConditionOperation.ValueEndsWith      => SearchComparison.EndsWith,
        ConditionOperation.ValueContains      => SearchComparison.Contains,
        ConditionOperation.ValueNotContains   => SearchComparison.NotContains,
        ConditionOperation.DosWildcards       => SearchComparison.Wildcards,
        ConditionOperation.WordEqual          => SearchComparison.WordEqual,
        ConditionOperation.WordStartsWith     => SearchComparison.WordStartsWith,

        // COP_IMPLICIT means "the property's own default", which only the schema knows. Equality is the
        // usual answer but not always, so it is not ours to assume.
        _ => SearchComparison.Unsupported,
    };

    /// <summary>The parser, created once and loaded with SystemIndex's schema. A failure is remembered so
    /// a machine without the service doesn't pay for a COM activation on every keystroke.</summary>
    private IQueryParser? Parser()
    {
        if (_initialised) return _parser;
        _initialised = true;

        try
        {
            var manager = (IQueryParserManager)new QueryParserManager();
            var iid     = IID_IQueryParser;

            if (manager.CreateLoadedParser("SystemIndex", CurrentLangId(), ref iid, out var raw) != 0 ||
                raw is not IQueryParser parser)
                return _parser = null;

            // Without this the parser has no keyword handling at all — "size:>1mb" would not resolve.
            // NQS off: natural language would read a bare word as a property phrase.
            manager.InitializeOptions(fUnderstandNQS: false, fAutoWildCard: true, parser);

            _parser = parser;
        }
        catch (COMException)      { _parser = null; }
        catch (InvalidCastException) { _parser = null; }

        return _parser;
    }

    private static ushort CurrentLangId() =>
        (ushort)(System.Globalization.CultureInfo.CurrentUICulture.LCID & 0xFFFF);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_parser is not null) Marshal.ReleaseComObject(_parser);
            _parser = null;
            _initialised = true;
        }
    }
}
