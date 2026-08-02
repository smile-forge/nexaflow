using System.Globalization;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// One of the two projections of a parsed <see cref="SearchCondition"/>: the SQL a SystemIndex query
/// wants. The other is <see cref="SearchConditionEvaluator"/>, which answers the same condition against a
/// file during a folder walk. Both read the same tree, so the index and the walk cannot drift into
/// answering different questions.
/// </summary>
public static class SearchConditionSql
{
    /// <summary>
    /// The WHERE fragment for <paramref name="condition"/>, or null when any part of it can't be
    /// expressed. Null means the caller drops the constraint and says so — emitting a partial clause
    /// would silently run a different query.
    /// </summary>
    public static string? ToWhereClause(SearchCondition condition)
    {
        switch (condition.Kind)
        {
            case SearchConditionKind.And:
            case SearchConditionKind.Or:
            {
                var parts = condition.Children.Select(ToWhereClause).ToList();
                if (parts.Count == 0 || parts.Any(p => p is null)) return null;

                var op = condition.Kind == SearchConditionKind.And ? " AND " : " OR ";
                return "(" + string.Join(op, parts) + ")";
            }

            case SearchConditionKind.Not:
            {
                if (condition.Children.Count != 1) return null;
                var inner = ToWhereClause(condition.Children[0]);
                return inner is null ? null : $"NOT {inner}";
            }

            default:
                return LeafClause(condition);
        }
    }

    private static string? LeafClause(SearchCondition leaf)
    {
        if (string.IsNullOrEmpty(leaf.Property) || leaf.Value is null) return null;

        var property = leaf.Property;

        // Full-text operators are a different SQL construct entirely, not an operator on a column.
        if (leaf.Comparison is SearchComparison.WordEqual or SearchComparison.WordStartsWith)
        {
            var word = Text(leaf.Value);
            if (string.IsNullOrEmpty(word)) return null;
            var term = leaf.Comparison == SearchComparison.WordStartsWith ? word + "*" : word;
            return $"CONTAINS({property}, '\"{Escape(term)}\"')";
        }

        if (leaf.Comparison is SearchComparison.Contains or SearchComparison.NotContains
                            or SearchComparison.StartsWith or SearchComparison.EndsWith
                            or SearchComparison.Wildcards)
        {
            var text = Text(leaf.Value);
            if (text is null) return null;

            var pattern = leaf.Comparison switch
            {
                SearchComparison.StartsWith => Like(text) + "%",
                SearchComparison.EndsWith   => "%" + Like(text),
                SearchComparison.Wildcards  => text.Replace('*', '%').Replace('?', '_'),
                _                           => "%" + Like(text) + "%",
            };

            var not = leaf.Comparison == SearchComparison.NotContains ? "NOT " : "";
            return $"{property} {not}LIKE '{Escape(pattern)}'";
        }

        var op = leaf.Comparison switch
        {
            SearchComparison.Equal              => "=",
            SearchComparison.NotEqual           => "<>",
            SearchComparison.LessThan           => "<",
            SearchComparison.GreaterThan        => ">",
            SearchComparison.LessThanOrEqual    => "<=",
            SearchComparison.GreaterThanOrEqual => ">=",
            _                                   => null,
        };
        if (op is null) return null;

        return $"{property} {op} {Literal(leaf.Value)}";
    }

    /// <summary>A value as SQL. Numbers stay unquoted — quoting one turns a size comparison into a
    /// string comparison, where 9 sorts after 10.</summary>
    private static string Literal(object value) => value switch
    {
        long l      => l.ToString(CultureInfo.InvariantCulture),
        int i       => i.ToString(CultureInfo.InvariantCulture),
        bool b      => b ? "TRUE" : "FALSE",
        DateTime d  => $"'{d.ToUniversalTime():yyyy-MM-dd HH:mm:ss}'",
        _           => $"'{Escape(Text(value) ?? string.Empty)}'",
    };

    private static string? Text(object? value) => value switch
    {
        null           => null,
        string s       => s,
        DateTime d     => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _              => value.ToString(),
    };

    /// <summary>Doubles single quotes — the only escape OLE DB string literals have.</summary>
    private static string Escape(string value) => value.Replace("'", "''");

    /// <summary>Neutralises LIKE's own wildcards in text meant to be matched literally, so a filename
    /// containing % or _ isn't read as a pattern.</summary>
    private static string Like(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
