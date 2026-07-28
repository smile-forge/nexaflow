using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Seeds the index for a regex search: finds the longest run of literal text the pattern <em>must</em>
/// contain, to be used as an ordinary search term. The index narrows; the regex decides.
/// <para>
/// The term is deliberately a plain one, not a filename glob. <see cref="SearchQueryParser"/> turns a plain
/// term into <c>CONTAINS(System.Search.Contents, …) OR System.FileName LIKE …</c>, so contents are searched
/// — whereas anything glob-shaped takes its <c>IsGlob</c> branch and matches filenames only. A regex meant
/// for file contents would then never see a single candidate, which is precisely the bug this replaced:
/// <c>?magic</c> found 34 files and <c>?/magic/</c> found none.
/// </para>
/// <para>
/// Only literals that every match must contain are eligible — text inside an alternation, or governed by a
/// quantifier that allows zero, is skipped. That keeps the seeded query a true superset, so the verifier's
/// post-filter can never be denied a row the regex would have matched.
/// </para>
/// </summary>
public static class AqsRegexTranslator
{
    /// <summary>A one-character term narrows nothing and costs a full index sweep; below this we'd rather
    /// admit the pattern can't be seeded than pull back the whole corpus.</summary>
    private const int MinUsefulTermLength = 2;

    /// <summary>
    /// The index seeds for one term, or null when it can't be narrowed on. An OR is only sound if EVERY
    /// alternative yields a seed — dropping one would silently narrow the query to the rest, losing files
    /// the missing branch would have matched.
    /// </summary>
    public static IReadOnlyList<string>? SeedsFor(SearchTerm term)
    {
        // A glob already speaks the index's own language — use the source form, not its regex translation.
        if (term.NameOnly) return term.SourceForms;

        if (term.Kind != SearchTermKind.Regex) return term.Alternatives;

        var seeds = new List<string>(term.Alternatives.Count);
        foreach (var pattern in term.Alternatives)
        {
            var seed = ToIndexQuery(pattern);
            if (seed is null) return null;
            seeds.Add(seed);
        }
        return seeds;
    }

    /// <summary>
    /// The index term for <paramref name="pattern"/>, or null when it contains no literal run long enough
    /// to narrow on (<c>\d{4}</c>, <c>^.+$</c>). A null means "this pattern cannot be seeded" — callers
    /// should say so rather than searching the entire corpus.
    /// </summary>
    public static string? ToIndexQuery(string pattern)
    {
        var best = MandatoryLiterals(pattern)
            .Where(s => s.Length >= MinUsefulTermLength && !s.Contains(' '))
            .OrderByDescending(s => s.Length)
            .FirstOrDefault();

        return string.IsNullOrEmpty(best) ? null : best;
    }

    /// <summary>
    /// Every run of literal text the pattern requires. Exposed for testing the superset property directly:
    /// a returned run must appear in every string the regex matches.
    /// </summary>
    public static IReadOnlyList<string> MandatoryLiterals(string pattern)
    {
        var runs    = new List<string>();
        var current = new System.Text.StringBuilder();
        var i       = 0;

        void Flush()
        {
            if (current.Length > 0) runs.Add(current.ToString());
            current.Clear();
        }

        while (i < pattern.Length)
        {
            var c = pattern[i];

            // A group with a single alternative and no zero-quantifier is mandatory, so its literal content
            // belongs to the run either side of it: "ma(ths)" must seed "maths", not "ma" — the difference
            // between a query that narrows and one that drags back the whole index.
            if (c == '(')
            {
                var start = i;
                SkipBalanced(pattern, ref i);
                var body = InnerOf(pattern[start..i]);

                var quantified = i < pattern.Length && pattern[i] is '*' or '?' ||
                                 (i < pattern.Length && pattern[i] == '{' && AllowsZero(pattern, i));

                if (!quantified && body is not null && IsPlainLiteral(body))
                {
                    current.Append(Unescape(body));
                    SkipQuantifier(pattern, ref i);
                    continue;
                }

                Flush();
                SkipQuantifier(pattern, ref i);
                continue;
            }

            // A character class is a choice — nothing inside it is guaranteed.
            if (c == '[')
            {
                Flush();
                SkipBalanced(pattern, ref i);
                SkipQuantifier(pattern, ref i);
                continue;
            }

            // Alternation at this level makes everything around it optional — nothing here is mandatory.
            if (c == '|') return [];

            if (c is '^' or '$') { Flush(); i++; continue; }

            // A single-character construct, plus whatever quantifier follows it.
            string? literal;
            if (c == '\\')
            {
                if (i + 1 >= pattern.Length) { Flush(); break; }
                var esc = pattern[i + 1];
                i += 2;
                literal = esc is 'd' or 'w' or 's' or 'D' or 'W' or 'S' or 'b' or 'B' ? null : esc.ToString();
            }
            else
            {
                i++;
                literal = c == '.' ? null : c.ToString();
            }

            // "a?" and "a*" allow zero occurrences, so that character isn't mandatory and the run ends
            // before it. "a+" requires at least one, so it stays.
            var quantifier = i < pattern.Length ? pattern[i] : '\0';
            if (quantifier is '*' or '?' || (quantifier == '{' && AllowsZero(pattern, i)))
            {
                Flush();
                SkipQuantifier(pattern, ref i);
                continue;
            }

            if (literal is null) Flush();
            else                 current.Append(literal);

            SkipQuantifier(pattern, ref i);
        }

        Flush();
        return runs;
    }

    // The body of "(…)", or null when it isn't a plain single-alternative group we can look inside.
    private static string? InnerOf(string group)
    {
        if (group.Length < 3 || group[0] != '(' || group[^1] != ')') return null;

        var body = group[1..^1];
        if (body.StartsWith("?:")) body = body[2..];
        else if (body.StartsWith('?')) return null;      // lookaround / named — not a plain group

        // Alternation means neither branch is guaranteed.
        var depth = 0;
        for (var j = 0; j < body.Length; j++)
        {
            if (body[j] == '\\') { j++; continue; }
            if (body[j] == '(') depth++;
            else if (body[j] == ')') depth--;
            else if (body[j] == '|' && depth == 0) return null;
        }
        return body;
    }

    // True when every character is a literal (or an escaped literal) — no wildcards, classes or quantifiers.
    private static bool IsPlainLiteral(string body)
    {
        for (var j = 0; j < body.Length; j++)
        {
            var c = body[j];
            if (c == '\\')
            {
                if (j + 1 >= body.Length) return false;
                if (body[j + 1] is 'd' or 'w' or 's' or 'D' or 'W' or 'S' or 'b' or 'B') return false;
                j++;
                continue;
            }
            if (c is '.' or '[' or ']' or '(' or ')' or '*' or '+' or '?' or '{' or '}' or '^' or '$' or '|')
                return false;
        }
        return body.Length > 0;
    }

    private static string Unescape(string body)
    {
        var sb = new System.Text.StringBuilder(body.Length);
        for (var j = 0; j < body.Length; j++)
        {
            if (body[j] == '\\' && j + 1 < body.Length) j++;
            sb.Append(body[j]);
        }
        return sb.ToString();
    }

    private static bool AllowsZero(string pattern, int braceIndex)
    {
        var close = pattern.IndexOf('}', braceIndex);
        if (close < 0) return true;                        // malformed — assume the worst
        var body = pattern[(braceIndex + 1)..close];
        return body.StartsWith('0') || body.StartsWith(',');
    }

    private static void SkipQuantifier(string pattern, ref int i)
    {
        if (i >= pattern.Length) return;
        if (pattern[i] is '*' or '+' or '?') i++;
        else if (pattern[i] == '{')
        {
            var close = pattern.IndexOf('}', i);
            i = close < 0 ? pattern.Length : close + 1;
        }
        if (i < pattern.Length && pattern[i] == '?') i++;   // non-greedy marker
    }

    // Skips a (...) or [...] construct, honouring escapes and nesting. Always advances.
    private static void SkipBalanced(string pattern, ref int i)
    {
        var open  = pattern[i];
        var close = open == '(' ? ')' : ']';
        var depth = 0;

        for (var j = i; j < pattern.Length; j++)
        {
            if (pattern[j] == '\\') { j++; continue; }
            if (pattern[j] == open) depth++;
            else if (pattern[j] == close)
            {
                depth--;
                if (depth == 0) { i = j + 1; return; }
            }
        }
        i = pattern.Length;   // unbalanced: consume the rest rather than spin
    }
}
