using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.IO.Common;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

public sealed class ParsedQuery
{
    public string RawInput    { get; init; } = string.Empty;
    /// <summary>True when only filenames should be searched (no content).</summary>
    public bool   IsGlob      { get; init; }
    /// <summary>OLE DB SQL WHERE fragment (without the SCOPE clause).</summary>
    public string WhereClause { get; init; } = string.Empty;
    /// <summary>
    /// Evaluates the query against a single file/folder for a live filesystem walk —
    /// used for globs (which never need the index) and as an off-index fallback.
    /// Matches on what a walk can see (name, size, modified); it cannot see file
    /// content, so content terms degrade to a filename match.
    /// </summary>
    public Func<FileProbe, bool> Matches { get; init; } = static _ => false;
}

/// <summary>Converts a raw user query into an OLE DB SQL WHERE clause for SystemIndex.</summary>
public static class SearchQueryParser
{
    private static readonly Regex QuotedWhole    = new(@"^""[^""]+""$",              RegexOptions.Compiled);
    private static readonly Regex PrefixSyntax   = new(@"(^|\s)[+\-]\S",            RegexOptions.Compiled);

    public static ParsedQuery Parse(string raw)
    {
        var trimmed = raw.Trim();

        // ── Quoted single term ────────────────────────────────────────────────
        if (QuotedWhole.IsMatch(trimmed))
        {
            var term = trimmed[1..^1];
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = false,
                WhereClause = $"CONTAINS(System.Search.Contents,'{EscapeSql(term)}')" +
                              $" OR System.FileName LIKE '%{EscapeLike(term)}%'",
                Matches     = p => NameHas(p, term)
            };
        }

        // ── File glob (no spaces, contains * or ?) ────────────────────────────
        if (Glob.ContainsGlobChars(trimmed) && !trimmed.Contains(' '))
        {
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = true,
                WhereClause = $"System.FileName LIKE '{Glob.ToSqlLike(trimmed)}'",
                Matches     = p => Glob.IsMatch(p.Name, trimmed)
            };
        }

        // ── Prefix syntax (+term -term) ───────────────────────────────────────
        if (PrefixSyntax.IsMatch(trimmed))
        {
            var parts   = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<string>();
            var preds   = new List<Func<FileProbe, bool>>();
            foreach (var p in parts)
            {
                if (p.StartsWith('+') && p.Length > 1)
                {
                    var term = p[1..];
                    clauses.Add($"System.FileName LIKE '%{EscapeLike(term)}%'");
                    preds.Add(fp => NameHas(fp, term));
                }
                else if (p.StartsWith('-') && p.Length > 1)
                {
                    var term = p[1..];
                    clauses.Add($"System.FileName NOT LIKE '%{EscapeLike(term)}%'");
                    preds.Add(fp => !NameHas(fp, term));
                }
                else
                {
                    clauses.Add($"System.FileName LIKE '%{EscapeLike(p)}%'");
                    preds.Add(fp => NameHas(fp, p));
                }
            }
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = false,
                WhereClause = string.Join(" AND ", clauses),
                Matches     = fp => preds.All(f => f(fp))
            };
        }

        // Property constraints (size:, modified:, kind:, …) are NOT handled here. They are recognised
        // while the query is tokenised, parsed by Windows' own AQS parser into a SearchCondition, and
        // projected by FromTerms below — which is the only path that also gives the folder walk something
        // it can evaluate. This overload sees only what a bare string can express.

        // ── Plain terms (content + filename) ─────────────────────────────────
        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var termClauses = terms.Select(t =>
            Glob.ContainsGlobChars(t)
                ? $"System.FileName LIKE '{Glob.ToSqlLike(t)}'"
                : $"(CONTAINS(System.Search.Contents,'{EscapeSql(t)}')" +
                  $" OR System.FileName LIKE '%{EscapeLike(t)}%')");
        return new ParsedQuery
        {
            RawInput    = raw,
            IsGlob      = false,
            WhereClause = string.Join(" AND ", termClauses),
            Matches     = p => terms.All(t =>
                Glob.ContainsGlobChars(t) ? Glob.IsMatch(p.Name, t) : NameHas(p, t))
        };
    }

    /// <summary>
    /// Builds one index query from a whole compound search: a term's alternatives OR'd, the terms AND'd —
    /// so <c>*.txt|*.md /ma(ths|gic)/ ocr</c> asks for "(.txt or .md) and (maths or magic) and ocr" in a
    /// single query rather than several.
    /// <para>
    /// Null when no term could be narrowed on at all. A term that individually can't be seeded is dropped
    /// rather than failed: the rest still narrow and the caller's post-filter applies the full query
    /// afterwards, so dropping only ever widens — which is safe, where narrowing would lose real results.
    /// </para>
    /// </summary>
    public static ParsedQuery? FromTerms(
        IReadOnlyList<SearchTerm> terms, IAqsTranslator? aqs = null)
    {
        var clauses = new List<string>();
        var nameOnly = true;

        foreach (var term in terms)
        {
            // A structured constraint is the backend's own language — hand it to the translator whole
            // rather than trying to seed it like text.
            if (term.Kind == SearchTermKind.Structured)
            {
                // The condition was parsed when the term was recognised; this is just the SQL projection
                // of it. The walk projection of the same tree is the Matches predicate below.
                var condition = term.Condition ?? aqs?.Parse(term.Value);
                var where     = condition is null ? null : SearchConditionSql.ToWhereClause(condition);
                if (where is null) continue;      // unexpressible: drop it, which widens — never narrows
                clauses.Add($"({where})");
                nameOnly = false;
                continue;
            }

            var seeds = AqsRegexTranslator.SeedsFor(term);
            if (seeds is null || seeds.Count == 0) continue;

            var alternatives = seeds.Select(s => ClauseFor(term, s)).ToList();
            clauses.Add(alternatives.Count == 1
                ? alternatives[0]
                : "(" + string.Join(" OR ", alternatives) + ")");

            if (!term.NameOnly) nameOnly = false;
        }

        if (clauses.Count == 0) return null;

        var request = new SearchRequest(terms[0].Value, terms[0].Kind == SearchTermKind.Regex) { Terms = terms };
        return new ParsedQuery
        {
            RawInput    = string.Join(" ", terms.Select(t => t.Label)),
            IsGlob      = nameOnly,
            WhereClause = string.Join(" AND ", clauses),
            // The walk's projection of the same terms. Unlike a row from the index, nothing here has been
            // pre-applied — so this evaluates every term, including a property constraint, against what
            // the walk can actually observe. A constraint it can't answer is undecidable, and an
            // undecidable query is not a match: showing every file would be the wrong answer, stated
            // confidently. (MatchesName would do exactly that — see the note on it.)
            Matches     = p => request.Evaluate(p.AsSearchSubject(), p.Name) == true,
        };
    }

    // A name-scoped term constrains the filename; anything else may match the name or the contents.
    //
    // A regex seed is only a FRAGMENT of what the pattern wants — "math[sy]" seeds "math" — and CONTAINS
    // matches whole WORDS, so an exact-word clause would never return a document containing "maths" and the
    // post-filter would never get the chance to confirm it. A prefix match ("math*") restores it. Safe here
    // precisely because a seeded query is always re-filtered afterwards; a plain literal term is NOT
    // re-filtered, so widening that one would hand back rows the user never asked for.
    private static string ClauseFor(SearchTerm term, string seed)
    {
        if (term.NameOnly) return $"System.FileName LIKE '{Glob.ToSqlLike(seed)}'";

        // A glob asks about the name AND the contents, in two different languages: LIKE understands the
        // glob as-is, while CONTAINS is word-based and can only be given the glob's longest literal run,
        // prefix-matched. Widening the content half is safe because a glob is always post-filtered.
        if (term.IsGlob)
        {
            var name    = $"System.FileName LIKE '{Glob.ToSqlLike(seed)}'";
            var literal = LongestLiteralRun(seed);

            return literal is null
                ? name       // nothing to search text for ("*") — the name is the whole question
                : $"({name} OR CONTAINS(System.Search.Contents,'\"{EscapeSql(literal)}*\"'))";
        }

        var contents = term.Kind == SearchTermKind.Regex
            ? $"CONTAINS(System.Search.Contents,'\"{EscapeSql(seed)}*\"')"
            : $"CONTAINS(System.Search.Contents,'{EscapeSql(seed)}')";

        return $"({contents} OR System.FileName LIKE '%{EscapeLike(seed)}%')";
    }

    /// <summary>
    /// Combines two parsed queries with AND so each constraint is preserved.
    /// The merged query re-queries Windows Search — it does not filter client-side.
    /// </summary>
    public static ParsedQuery Merge(ParsedQuery first, ParsedQuery second)
        => new()
        {
            RawInput    = $"{first.RawInput.Trim()} {second.RawInput.Trim()}",
            IsGlob      = false,
            WhereClause = $"({first.WhereClause}) AND ({second.WhereClause})",
            Matches     = p => first.Matches(p) && second.Matches(p)
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string EscapeSql(string s)  => s.Replace("'", "''");
    private static string EscapeLike(string s) => s.Replace("'", "''")
                                                    .Replace("%", "[%]")
                                                    .Replace("_", "[_]");

    private static bool NameHas(FileProbe p, string sub) =>
        p.Name.Contains(sub, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The longest wildcard-free run in a glob — what a word-based index can actually be asked for.
    /// Null when there is nothing substantial enough to narrow on, in which case the caller must not
    /// invent a content clause.
    /// </summary>
    private static string? LongestLiteralRun(string glob)
    {
        var best = glob.Split(['*', '?'], StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim('.'))          // ".txt" tokenises to "txt" anyway
                       .Where(s => s.Length >= 2)
                       .OrderByDescending(s => s.Length)
                       .FirstOrDefault();

        return string.IsNullOrEmpty(best) ? null : best;
    }
}
