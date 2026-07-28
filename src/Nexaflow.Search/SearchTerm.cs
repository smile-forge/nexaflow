using System.Text.RegularExpressions;

namespace Nexaflow.Search;

/// <summary>What a term is matched against.</summary>
public enum SearchTermKind
{
    /// <summary>A regular expression.</summary>
    Regex,

    /// <summary>Literal text, matched as a substring.</summary>
    Text,

    /// <summary>
    /// A structured property constraint in the backend's own query language — <c>kind:document</c>,
    /// <c>size:&gt;1mb</c>, <c>modified:last week</c>. Only the backend can evaluate it, so it is enforced
    /// by the query rather than by any client-side filter.
    /// </summary>
    Structured,
}

/// <summary>
/// One constraint within a query. A query is the AND of its terms; a term with several
/// <see cref="Alternatives"/> is the OR of those — so <c>*.txt|*.md /ma(ths|gic)/ ocr</c> is
/// "(a .txt or .md file) AND (matching that pattern) AND (mentioning ocr)".
/// </summary>
/// <param name="Kind">How to interpret <paramref name="Alternatives"/>.</param>
/// <param name="Alternatives">One or more values, OR'd together.</param>
/// <param name="MatchCase">Case sensitivity.</param>
/// <param name="NameOnly">
/// True when this term can only sensibly be judged against a file's name — a filename glob, say. Kept as a
/// flag rather than a term kind so this library needs no glob implementation of its own: a recogniser
/// contributes the already-anchored pattern and marks it name-scoped.
/// </param>
/// <param name="Display">How the term was originally written, for messages. Falls back to the value.</param>
/// <param name="Sources">
/// The user's original alternatives when <paramref name="Alternatives"/> is a translation of them — a glob
/// arrives here already converted to an anchored regex, and a backend with its own query language (an index
/// that speaks globs natively) needs the glob back, not the translation.
/// </param>
/// <param name="Condition">
/// The parsed form of a <see cref="SearchTermKind.Structured"/> term. Present so the constraint can be
/// answered by a backend that isn't the index — without it, a structured term is an opaque string that
/// only the query language understands, and anything else must either drop it or pretend.
/// </param>
public sealed record SearchTerm(
    SearchTermKind Kind,
    IReadOnlyList<string> Alternatives,
    bool MatchCase = false,
    bool NameOnly = false,
    string? Display = null,
    IReadOnlyList<string>? Sources = null,
    SearchCondition? Condition = null)
{
    /// <summary>What the user actually wrote, per alternative — the translation when there was none.</summary>
    public IReadOnlyList<string> SourceForms => Sources ?? Alternatives;

    /// <summary>
    /// True when only the backend can evaluate this term, so the query itself enforces it and no
    /// client-side filter may re-test it. Re-testing would fail every row: nothing in a filename or a
    /// file's text can tell you whether <c>size:&gt;1mb</c> holds.
    /// </summary>
    public bool IndexEnforced => Kind == SearchTermKind.Structured;

    /// <summary>The single value, for the common one-alternative case.</summary>
    public string Value => Alternatives.Count > 0 ? Alternatives[0] : string.Empty;

    /// <summary>How to show this term to a user — what they typed, where that was kept.</summary>
    public string Label => Display ?? string.Join("|", Alternatives);

    private StringComparison Comparison =>
        MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// True when <paramref name="candidate"/> satisfies any alternative. <paramref name="isName"/> tells
    /// the term whether it is looking at a filename, so a name-scoped term is never applied to a body.
    /// <para>
    /// <b>Only valid for rows an index returned.</b> A structured term reports true here because the
    /// query that produced the row already applied it. Anywhere else — a folder walk, a page filtering
    /// its own rows — that answer is a lie, and <see cref="Evaluate"/> is the method to use.
    /// </para>
    /// </summary>
    public bool Matches(string candidate, bool isName)
    {
        // Already guaranteed by the query that returned this row — re-testing it here can only fail it.
        if (IndexEnforced) return true;

        if (Alternatives.Count == 0) return false;
        if (NameOnly && !isName) return false;

        return Kind == SearchTermKind.Regex
            ? Alternatives.Any(p => SafeRegex(p, candidate))
            : Alternatives.Any(t => candidate.Contains(t, Comparison));
    }

    private bool SafeRegex(string pattern, string candidate)
    {
        try
        {
            return Regex.IsMatch(candidate, pattern,
                MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Judges this term against <paramref name="subject"/> with nothing taken on trust — for a folder
    /// walk, or anywhere else no index has pre-applied it. Null means undecidable: an unknown property,
    /// an operator this model can't express, or a structured term that was never parsed.
    /// <para>
    /// The tri-state is the whole point. A walk that can't answer <c>author:john</c> must say so rather
    /// than pick a side, because both sides are wrong and neither looks like a bug.
    /// </para>
    /// </summary>
    public bool? Evaluate(ISearchSubject subject, string candidate, bool isName = true)
    {
        if (Kind != SearchTermKind.Structured)
            return Matches(candidate, isName);

        return Condition is null ? null : SearchConditionEvaluator.Evaluate(Condition, subject);
    }

    /// <summary>False (with a reason) when the pattern can't compile — reported rather than silently
    /// matching nothing.</summary>
    public bool TryValidate([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        error = null;
        if (Kind != SearchTermKind.Regex) return true;

        foreach (var pattern in Alternatives)
        {
            try { _ = new Regex(pattern); }
            catch (ArgumentException ex) { error = $"{Label} — {ex.Message}"; return false; }
        }
        return true;
    }
}
