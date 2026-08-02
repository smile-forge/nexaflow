using System.Globalization;

namespace Nexaflow.Search;

/// <summary>
/// Something a <see cref="SearchCondition"/> can be tested against — a file on disk, a row, a record.
/// <para>
/// Returning false from <see cref="TryGetProperty"/> means "I don't know this property", which is not the
/// same as "it doesn't match" and must never collapse into it. A folder walk knows a file's size and
/// dates but not its author; answering "no" for the author would hide files that do match, and answering
/// "yes" would show files that don't.
/// </para>
/// </summary>
public interface ISearchSubject
{
    bool TryGetProperty(string property, out object? value);
}

/// <summary>
/// Evaluates a parsed condition against a subject, in three states: matched, not matched, and
/// <em>undecidable</em>.
/// <para>
/// The third is the one that matters. A backend that can only answer part of a query must be able to say
/// so, because both ways of pretending otherwise are wrong — and the failure is invisible either way,
/// since a query that returns everything and a query that returns nothing both look like working code.
/// </para>
/// </summary>
public static class SearchConditionEvaluator
{
    /// <summary>
    /// True/false when the subject settles the condition, null when it can't — an unknown property, an
    /// unsupported operator, or a value whose type doesn't line up.
    /// <para>
    /// Branches use three-valued logic, so an undecidable part only spoils the answer when it would have
    /// changed it: <c>false AND unknown</c> is false, because no value of the unknown makes it true.
    /// </para>
    /// </summary>
    public static bool? Evaluate(SearchCondition condition, ISearchSubject subject)
    {
        switch (condition.Kind)
        {
            case SearchConditionKind.And:
            {
                var unknown = false;
                foreach (var child in condition.Children)
                {
                    var r = Evaluate(child, subject);
                    if (r is false) return false;      // settled regardless of the rest
                    if (r is null) unknown = true;
                }
                return unknown ? null : true;
            }

            case SearchConditionKind.Or:
            {
                var unknown = false;
                foreach (var child in condition.Children)
                {
                    var r = Evaluate(child, subject);
                    if (r is true) return true;
                    if (r is null) unknown = true;
                }
                return unknown ? null : false;
            }

            case SearchConditionKind.Not:
            {
                if (condition.Children.Count != 1) return null;
                return Evaluate(condition.Children[0], subject) is { } inner ? !inner : null;
            }

            default:
                return EvaluateLeaf(condition, subject);
        }
    }

    private static bool? EvaluateLeaf(SearchCondition leaf, ISearchSubject subject)
    {
        if (leaf.Property is null) return null;
        if (leaf.Comparison == SearchComparison.Unsupported) return null;
        if (!subject.TryGetProperty(leaf.Property, out var actual)) return null;
        if (actual is null || leaf.Value is null) return null;

        return leaf.Comparison switch
        {
            SearchComparison.Equal              => Compare(actual, leaf.Value) is { } c ? c == 0 : TextEquals(actual, leaf.Value),
            SearchComparison.NotEqual           => Compare(actual, leaf.Value) is { } c ? c != 0 : !TextEquals(actual, leaf.Value),
            SearchComparison.LessThan           => Compare(actual, leaf.Value) is { } c ? c <  0 : null,
            SearchComparison.GreaterThan        => Compare(actual, leaf.Value) is { } c ? c >  0 : null,
            SearchComparison.LessThanOrEqual    => Compare(actual, leaf.Value) is { } c ? c <= 0 : null,
            SearchComparison.GreaterThanOrEqual => Compare(actual, leaf.Value) is { } c ? c >= 0 : null,

            SearchComparison.StartsWith      => Text(actual)?.StartsWith(Text(leaf.Value) ?? "", Ci),
            SearchComparison.EndsWith        => Text(actual)?.EndsWith(Text(leaf.Value) ?? "", Ci),
            SearchComparison.Contains        => Text(actual)?.Contains(Text(leaf.Value) ?? "", Ci),
            SearchComparison.NotContains     => Text(actual) is { } t ? !t.Contains(Text(leaf.Value) ?? "", Ci) : null,
            SearchComparison.Wildcards       => MatchesWildcards(Text(actual), Text(leaf.Value)),

            // Word matching is what a full-text index does. A substring test is NOT the same question —
            // it would make "math" match "mathematics" — so a walk that can't tokenise says so instead.
            SearchComparison.WordEqual       => WordMatch(Text(actual), Text(leaf.Value), prefix: false),
            SearchComparison.WordStartsWith  => WordMatch(Text(actual), Text(leaf.Value), prefix: true),

            _ => null,
        };
    }

    private const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    private static string? Text(object? value) => value switch
    {
        null            => null,
        string s        => s,
        DateTime d      => d.ToString("o", CultureInfo.InvariantCulture),
        IFormattable f  => f.ToString(null, CultureInfo.InvariantCulture),
        _               => value.ToString(),
    };

    private static bool TextEquals(object actual, object expected) =>
        string.Equals(Text(actual), Text(expected), Ci);

    /// <summary>Orders two values of compatible type, or null when they aren't comparable — never a
    /// fallback to string ordering, which would make "9" greater than "10".</summary>
    private static int? Compare(object actual, object expected)
    {
        if (TryLong(actual, out var a) && TryLong(expected, out var b)) return a.CompareTo(b);
        if (actual is DateTime da && expected is DateTime db)           return da.CompareTo(db);
        if (actual is string sa && expected is string sb)               return string.Compare(sa, sb, Ci);
        return null;
    }

    private static bool TryLong(object value, out long result)
    {
        switch (value)
        {
            case long l:   result = l; return true;
            case int i:    result = i; return true;
            case short s:  result = s; return true;
            case uint u:   result = u; return true;
            case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
            default:       result = 0; return false;
        }
    }

    private static bool? MatchesWildcards(string? actual, string? pattern)
    {
        if (actual is null || pattern is null) return null;

        // Anchored, so "*.txt" is a whole-name test rather than a substring one.
        var regex = "^" + string.Concat(pattern.Select(c => c switch
        {
            '*' => ".*",
            '?' => ".",
            _   => System.Text.RegularExpressions.Regex.Escape(c.ToString()),
        })) + "$";

        try { return System.Text.RegularExpressions.Regex.IsMatch(actual, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Whole-word match over a body of text — the closest an unindexed walk can get to what the
    /// index does. Splits on non-letter/digit rather than whitespace so punctuation ends a word.</summary>
    private static bool? WordMatch(string? actual, string? word, bool prefix)
    {
        if (actual is null || string.IsNullOrEmpty(word)) return null;

        var start = 0;
        for (var i = 0; i <= actual.Length; i++)
        {
            var boundary = i == actual.Length || !char.IsLetterOrDigit(actual[i]);
            if (!boundary) continue;

            if (i > start)
            {
                var span = actual.AsSpan(start, i - start);
                var hit  = prefix
                    ? span.StartsWith(word, Ci)
                    : span.Equals(word, Ci);
                if (hit) return true;
            }
            start = i + 1;
        }
        return false;
    }
}
