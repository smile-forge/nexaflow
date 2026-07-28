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
    private static readonly Regex FilterKeyword  = new(
        @"\b(size|date|modified|before|after|larger|smaller):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SizeFilter     = new(
        @"\b(size|larger|smaller):([><=]?)(\d+)(kb|mb|gb)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateFilter     = new(
        @"\b(date|modified|before|after):([><=]?)(\d{4}(?:-\d{2}(?:-\d{2})?)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        // ── Filter criteria (size:, date:, etc.) ─────────────────────────────
        if (FilterKeyword.IsMatch(trimmed))
        {
            var (where, match) = BuildFilter(trimmed);
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = false,
                WhereClause = where,
                Matches     = match
            };
        }

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
    public static ParsedQuery? FromTerms(IReadOnlyList<SearchTerm> terms)
    {
        var clauses = new List<string>();
        var nameOnly = true;

        foreach (var term in terms)
        {
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
            // The live-walk fallback can only see names, so it judges the query on the name alone.
            Matches     = p => request.MatchesName(p.Name),
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

    private static bool CompareLong(long actual, string op, long expected) => op switch
    {
        ">"  => actual >  expected,
        "<"  => actual <  expected,
        ">=" => actual >= expected,
        "<=" => actual <= expected,
        _    => actual == expected,
    };

    private static bool CompareDate(DateTime actual, string op, DateTime expected) => op switch
    {
        "<"  => actual <  expected,
        ">=" => actual >= expected,
        "<=" => actual <= expected,
        "="  => actual.Date == expected.Date,
        _    => actual >  expected,   // default '>' mirrors the SQL date default
    };

    /// <summary>Builds the SQL WHERE fragment and the parallel filesystem predicate
    /// for a size:/date:/… filter query. The two are kept in lockstep.</summary>
    private static (string Where, Func<FileProbe, bool> Match) BuildFilter(string input)
    {
        var clauses = new List<string>();
        var preds   = new List<Func<FileProbe, bool>>();

        foreach (Match m in SizeFilter.Matches(input))
        {
            var op      = m.Groups[2].Value is "" ? "=" : m.Groups[2].Value;
            var value   = long.Parse(m.Groups[3].Value);
            var unit    = m.Groups[4].Value.ToUpperInvariant();
            var bytes   = unit switch { "KB" => value * 1024L, "MB" => value * 1024L * 1024, "GB" => value * 1024L * 1024 * 1024, _ => value };
            var keyword = m.Groups[1].Value.ToLowerInvariant();
            if (keyword == "larger")  op = ">";
            if (keyword == "smaller") op = "<";
            clauses.Add($"System.Size {op} {bytes}");
            var o = op; var b = bytes;
            preds.Add(p => !p.IsDirectory && CompareLong(p.Size, o, b));
        }

        foreach (Match m in DateFilter.Matches(input))
        {
            var keyword = m.Groups[1].Value.ToLowerInvariant();
            var op      = m.Groups[2].Value is "" ? ">" : m.Groups[2].Value;
            var date    = m.Groups[3].Value;
            if (keyword == "before") op = "<";
            if (keyword == "after")  op = ">";
            var iso = date.Length == 4 ? $"{date}-01-01" : date.Length == 7 ? $"{date}-01" : date;
            clauses.Add($"System.DateModified {op} '{iso}'");
            var o = op; var when = DateTime.Parse(iso, CultureInfo.InvariantCulture);
            preds.Add(p => CompareDate(p.Modified, o, when));
        }

        // Any remaining plain tokens outside filter keywords
        var stripped = FilterKeyword.Replace(SizeFilter.Replace(DateFilter.Replace(input, ""), ""), "").Trim();
        if (!string.IsNullOrWhiteSpace(stripped))
        {
            foreach (var t in stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                clauses.Add($"System.FileName LIKE '%{EscapeLike(t)}%'");
                var term = t;
                preds.Add(p => NameHas(p, term));
            }
        }

        var where = clauses.Count > 0 ? string.Join(" AND ", clauses) : "1=1";
        Func<FileProbe, bool> match = preds.Count > 0
            ? p => preds.All(f => f(p))
            : static _ => true;   // matched the SQL "1=1"
        return (where, match);
    }
}
