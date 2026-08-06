using System.Collections.Concurrent;
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
/// <param name="Exact">
/// True when the user quoted this term. Quoting means "take this exactly", so <c>*</c> and <c>?</c> are
/// ordinary characters rather than wildcards — <c>"partial*"</c> looks for an asterisk, where bare
/// <c>partial*</c> is a prefix match. It is a flag on the term rather than something
/// <see cref="Matches"/> infers from <see cref="Display"/>, because the quotes are stripped before any
/// matcher sees the value and re-deriving intent from a display string is how the escape hatch stops
/// working the moment a term is built any other way.
/// </param>
public sealed record SearchTerm(
    SearchTermKind Kind,
    IReadOnlyList<string> Alternatives,
    bool MatchCase = false,
    bool NameOnly = false,
    string? Display = null,
    IReadOnlyList<string>? Sources = null,
    SearchCondition? Condition = null,
    bool Exact = false)
{

    /// <summary>What the user actually wrote, per alternative — the translation when there was none.</summary>
    public IReadOnlyList<string> SourceForms => Sources ?? Alternatives;

    /// <summary>
    /// True when only the backend can evaluate this term, so the query itself enforces it and no
    /// client-side filter may re-test it. Re-testing would fail every row: nothing in a filename or a
    /// file's text can tell you whether <c>size:&gt;1mb</c> holds.
    /// </summary>
    public bool IndexEnforced => Kind == SearchTermKind.Structured;

    /// <summary>
    /// True when this is a literal term carrying wildcards (<c>*term*</c>, <c>fig?</c>) — matched with
    /// word-bounded wildcards against name and content alike. False for a quoted term (<see cref="Exact"/>),
    /// where the wildcards are ordinary characters, and for a regex term, whose engine owns its own <c>*</c>.
    /// <para>
    /// Public because a backend that can't run wildcards natively (a full-text index, whose <c>CONTAINS</c>
    /// only prefixes) needs to know a term is a <em>fragment</em> to seed and re-filter, rather than an exact
    /// word it can answer outright.
    /// </para>
    /// </summary>
    public bool HasWildcards =>
        !Exact && Kind == SearchTermKind.Text && Alternatives.Any(a => a.IndexOfAny(['*', '?']) >= 0);

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
            : Alternatives.Any(t => MatchesText(candidate, t));
    }

    /// <summary>
    /// Every span of <paramref name="candidate"/> this term matches, in document order.
    /// <para>
    /// It exists so a page that <em>paints</em> matches can't disagree with the one that <em>counts</em>
    /// them: both come from here, off the same pattern. A highlight sitting beside the word instead of on
    /// it reads as a rendering bug, and a highlight where nothing matched reads as a search bug — neither
    /// is findable if the two are computed separately.
    /// </para>
    /// <para>
    /// Empty for a term nothing in a body can satisfy: one the index already enforced, or one scoped to a
    /// filename (there is no name in the text being painted).
    /// </para>
    /// </summary>
    public IEnumerable<(int Index, int Length)> Occurrences(string candidate)
    {
        if (IndexEnforced || NameOnly) yield break;

        foreach (var alternative in Alternatives)
        {
            if (alternative.Length == 0) continue;

            if (Kind == SearchTermKind.Regex)
            {
                foreach (var span in RegexOccurrences(alternative, candidate)) yield return span;
                continue;
            }

            if (UsesWildcards(alternative))
            {
                foreach (Match m in WordPattern(alternative, MatchCase).Matches(candidate))
                    if (m.Length > 0) yield return (m.Index, m.Length);
                continue;
            }

            foreach (var span in WholeWordOccurrences(candidate, alternative, Comparison))
                yield return span;
        }
    }

    /// <summary>Matches one literal alternative: a wildcard pattern when the user wrote one, else the word
    /// it spells.</summary>
    private bool MatchesText(string candidate, string alternative)
    {
        if (string.IsNullOrEmpty(alternative)) return false;

        return UsesWildcards(alternative)
            ? WordPattern(alternative, MatchCase).IsMatch(candidate)
            : WholeWordOccurrences(candidate, alternative, Comparison).Any();
    }

    /// <summary>
    /// True when this alternative should be read as a wildcard pattern. Quoting suppresses it, because
    /// quoting means "take this exactly" — <c>"partial*"</c> is a search for an asterisk.
    /// </summary>
    private bool UsesWildcards(string alternative) =>
        !Exact && alternative.IndexOfAny(Wildcards) >= 0;

    private static readonly char[] Wildcards = ['*', '?'];

    /// <summary>
    /// Every whole-word occurrence of <paramref name="needle"/> in <paramref name="text"/> — bounded at
    /// both ends by something that isn't a letter or digit.
    /// <para>
    /// A literal term means the word it spells. Substring matching makes <c>needle</c> find "needless",
    /// which is not what was asked and not what the index does either — <c>CONTAINS</c> is word-based, so
    /// substring matching here also made the post-filter disagree with the query that fed it. Anyone who
    /// wants the looser match says so with <c>*needle*</c>.
    /// </para>
    /// <para>
    /// Boundary-checked rather than tokenised, so a quoted phrase works the same way: "the lost dog"
    /// matches inside a sentence but not inside "the lost dogma".
    /// </para>
    /// </summary>
    private static IEnumerable<(int Index, int Length)> WholeWordOccurrences(
        string text, string needle, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(needle)) yield break;

        var from = 0;
        while (from <= text.Length - needle.Length)
        {
            var at = text.IndexOf(needle, from, comparison);
            if (at < 0) yield break;

            var end      = at + needle.Length;
            var beforeOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var afterOk  = end == text.Length || !char.IsLetterOrDigit(text[end]);

            if (beforeOk && afterOk) yield return (at, needle.Length);

            from = at + 1;
        }
    }

    private static IEnumerable<(int Index, int Length)> RegexOccurrences(string pattern, string candidate)
    {
        MatchCollection matches;
        try { matches = Regex.Matches(candidate, pattern, RegexOptions.IgnoreCase); }
        catch (ArgumentException) { yield break; }

        foreach (Match m in matches)
            if (m.Length > 0) yield return (m.Index, m.Length);
    }

    // ── Wildcards ────────────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<(string Pattern, bool MatchCase), Regex> _wordPatterns = new();

    /// <summary>
    /// A wildcard term as a regex that is still <em>word-bounded</em>: <c>fig*</c> is a word starting
    /// "fig", <c>*fig</c> one ending in it, <c>*fig*</c> one containing it, and <c>f?g</c> a three-letter
    /// word. Plain <c>fig</c> never reaches here and stays the word it spells.
    /// <para>
    /// The wildcards expand to letters and digits only, not to <c>.</c> — that is the whole difference
    /// between this and <c>Glob.ToRegexPattern</c>, which anchors a pattern to a whole <em>file name</em>.
    /// An unconstrained <c>.*</c> would let <c>*fig*</c> span a whole line and satisfy the word boundaries
    /// trivially at its ends, which reads as "found it" while having matched something else entirely.
    /// </para>
    /// </summary>
    private static Regex WordPattern(string pattern, bool matchCase) =>
        _wordPatterns.GetOrAdd((pattern, matchCase), static key =>
        {
            const string WordChar = @"[\p{L}\p{N}]";
            var sb = new System.Text.StringBuilder($"(?<!{WordChar})");

            foreach (var c in key.Pattern)
                sb.Append(c switch
                {
                    '*' => WordChar + "*",
                    '?' => WordChar,
                    _   => Regex.Escape(c.ToString()),
                });

            sb.Append($"(?!{WordChar})");
            return new Regex(sb.ToString(), key.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        });

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
