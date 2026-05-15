using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsSearch.Services;

public sealed class ParsedQuery
{
    public string RawInput    { get; init; } = string.Empty;
    /// <summary>True when only filenames should be searched (no content).</summary>
    public bool   IsGlob      { get; init; }
    /// <summary>OLE DB SQL WHERE fragment (without the SCOPE clause).</summary>
    public string WhereClause { get; init; } = string.Empty;
}

/// <summary>Converts a raw user query into an OLE DB SQL WHERE clause for SystemIndex.</summary>
public static class SearchQueryParser
{
    private static readonly Regex QuotedWhole    = new(@"^""[^""]+""$",              RegexOptions.Compiled);
    private static readonly Regex GlobChars      = new(@"[\*\?]",                    RegexOptions.Compiled);
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
                              $" OR System.FileName LIKE '%{EscapeLike(term)}%'"
            };
        }

        // ── File glob (no spaces, contains * or ?) ────────────────────────────
        if (GlobChars.IsMatch(trimmed) && !trimmed.Contains(' '))
        {
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = true,
                WhereClause = $"System.FileName LIKE '{GlobToLike(trimmed)}'"
            };
        }

        // ── Prefix syntax (+term -term) ───────────────────────────────────────
        if (PrefixSyntax.IsMatch(trimmed))
        {
            var parts   = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<string>();
            foreach (var p in parts)
            {
                if (p.StartsWith('+') && p.Length > 1)
                    clauses.Add($"System.FileName LIKE '%{EscapeLike(p[1..])}%'");
                else if (p.StartsWith('-') && p.Length > 1)
                    clauses.Add($"System.FileName NOT LIKE '%{EscapeLike(p[1..])}%'");
                else
                    clauses.Add($"System.FileName LIKE '%{EscapeLike(p)}%'");
            }
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = false,
                WhereClause = string.Join(" AND ", clauses)
            };
        }

        // ── Filter criteria (size:, date:, etc.) ─────────────────────────────
        if (FilterKeyword.IsMatch(trimmed))
        {
            return new ParsedQuery
            {
                RawInput    = raw,
                IsGlob      = false,
                WhereClause = BuildFilterClauses(trimmed)
            };
        }

        // ── Plain terms (content + filename) ─────────────────────────────────
        var terms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var termClauses = terms.Select(t =>
            $"(CONTAINS(System.Search.Contents,'{EscapeSql(t)}')" +
            $" OR System.FileName LIKE '%{EscapeLike(t)}%')");
        return new ParsedQuery
        {
            RawInput    = raw,
            IsGlob      = false,
            WhereClause = string.Join(" AND ", termClauses)
        };
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
            WhereClause = $"({first.WhereClause}) AND ({second.WhereClause})"
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GlobToLike(string glob)
        => glob.Replace("%", "[%]")
               .Replace("_", "[_]")
               .Replace("'", "''")
               .Replace("*", "%")
               .Replace("?", "_");

    private static string EscapeSql(string s)  => s.Replace("'", "''");
    private static string EscapeLike(string s) => s.Replace("'", "''")
                                                    .Replace("%", "[%]")
                                                    .Replace("_", "[_]");

    private static string BuildFilterClauses(string input)
    {
        var clauses = new List<string>();

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
        }

        // Any remaining plain tokens outside filter keywords
        var stripped = FilterKeyword.Replace(SizeFilter.Replace(DateFilter.Replace(input, ""), ""), "").Trim();
        if (!string.IsNullOrWhiteSpace(stripped))
        {
            foreach (var t in stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                clauses.Add($"System.FileName LIKE '%{EscapeLike(t)}%'");
        }

        return clauses.Count > 0 ? string.Join(" AND ", clauses) : "1=1";
    }
}
