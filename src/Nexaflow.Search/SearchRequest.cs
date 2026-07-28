using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Nexaflow.Search;

/// <summary>
/// One parsed search. Pages receive this rather than raw text so the query syntax is parsed exactly once
/// (by <see cref="SearchSyntax"/>) instead of drifting across implementations.
/// <para>
/// A query is the AND of its <see cref="Terms"/>. The single-term case — the overwhelming majority — is
/// still described by <see cref="Text"/>/<see cref="IsRegex"/>, so a page that only ever handles one
/// pattern can keep reading those and ignore the rest.
/// </para>
/// </summary>
/// <param name="Text">The pattern to match — the regex source when <paramref name="IsRegex"/>, else literal text.</param>
/// <param name="IsRegex">True when <paramref name="Text"/> is a regular expression.</param>
/// <param name="MatchCase">True for a case-sensitive match. Default is insensitive.</param>
public sealed record SearchRequest(string Text, bool IsRegex = false, bool MatchCase = false)
{
    private readonly IReadOnlyList<SearchTerm>? _terms;

    /// <summary>
    /// The AND-ed constraints. Defaults to the single term described by <see cref="Text"/> and
    /// <see cref="IsRegex"/>, so every existing caller keeps working unchanged.
    /// </summary>
    public IReadOnlyList<SearchTerm> Terms
    {
        get => _terms ?? [new SearchTerm(
            IsRegex ? SearchTermKind.Regex : SearchTermKind.Text, [Text], MatchCase)];
        init => _terms = value;
    }

    /// <summary>True when some term can only be judged against a filename — a page with no filenames to
    /// judge (a viewer searching its own body) must decline rather than quietly ignore it.</summary>
    public bool HasNameOnlyTerms => Terms.Any(t => t.NameOnly);

    /// <summary>
    /// Every term satisfied by <paramref name="name"/> alone.
    /// <para>
    /// A query with NO terms matches nothing. <c>All</c> over an empty sequence is vacuously true, which
    /// would silently mark every row the index returned as proven — the difference between "8 matches" and
    /// "2652 matches" on a broad seed.
    /// </para>
    /// </summary>
    public bool MatchesName(string name) =>
        Terms.Count > 0 && Terms.All(t => t.Matches(name, isName: true));

    /// <summary>
    /// Every term satisfied by the file as a whole — each term may be met by the name OR, unless it is
    /// name-scoped, by the contents. That is per-term on purpose: <c>*.txt report</c> should match a file
    /// NAMED "*.txt"-ish that MENTIONS "report", which a whole-query name-or-content test would miss.
    /// </summary>
    public bool MatchesFile(string name, string? content)
    {
        if (Terms.Count == 0) return false;   // no constraints matches nothing, never everything

        foreach (var term in Terms)
        {
            var byName    = term.Matches(name, isName: true);
            var byContent = !term.NameOnly && content is not null && term.Matches(content, isName: false);
            if (!byName && !byContent) return false;
        }
        return true;
    }

    /// <summary>The first term that a name can never satisfy — a name-scoped term the name failed. Such a
    /// row is a definite miss, because reading the file cannot rescue it.</summary>
    public bool NameRulesItOut(string name) =>
        Terms.Any(t => t.NameOnly && !t.Matches(name, isName: true));

    /// <summary>Comparison a literal search should use, so every page treats <see cref="MatchCase"/> alike.</summary>
    public StringComparison Comparison =>
        MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Options a regex search should use, so every page treats <see cref="MatchCase"/> alike.</summary>
    public RegexOptions RegexOptions =>
        MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

    /// <summary>
    /// Compiles <see cref="Text"/> as a regex. False (with <paramref name="error"/> set) when the pattern
    /// is malformed — return <see cref="SearchOutcome.Failed"/> rather than silently matching nothing, so
    /// the user learns their pattern was bad.
    /// </summary>
    public bool TryCompileRegex([NotNullWhen(true)] out Regex? regex, [NotNullWhen(false)] out string? error)
    {
        try
        {
            regex = new Regex(Text, RegexOptions);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> satisfies every term this text could satisfy — the one place
    /// literal-vs-regex is decided, so a page scanning its own rows honours both modes without re-deriving
    /// the rules. Name-scoped terms are excluded: a caller with a single body has no name to judge, and
    /// should have declined via <see cref="HasNameOnlyTerms"/> before getting here.
    /// </summary>
    public bool Matches(string candidate)
    {
        var judgeable = Terms.Where(t => !t.NameOnly && !t.IndexEnforced).ToList();
        return judgeable.Count > 0 && judgeable.All(t => t.Matches(candidate, isName: false));
    }

    /// <summary>False (with a reason) when any term's pattern can't compile.</summary>
    public bool TryValidate([NotNullWhen(false)] out string? error)
    {
        foreach (var term in Terms)
            if (!term.TryValidate(out error)) return false;

        error = null;
        return true;
    }
}
