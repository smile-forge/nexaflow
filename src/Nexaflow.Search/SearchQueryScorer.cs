using System.Text.RegularExpressions;

namespace Nexaflow.Search;

/// <summary>
/// Stateless scorer that assigns a 0–1 confidence that an input string is a
/// file-search query (glob, quoted term, prefix syntax, filter criteria)
/// rather than a conversation or path navigation.
/// <para>
/// Shared rather than owned by the Windows Search feature: it is the default
/// <c>ISearchable.ScoreQuery</c> for any page whose search is over files, and both the
/// file browser and the Search tab lean on it to tell a glob from a question.
/// </para>
/// </summary>
public static class SearchQueryScorer
{
    private static readonly Regex TermSplitter =
        new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.Compiled);

    private static readonly Regex FilterPrefix =
        new(@"^(size|date|modified|before|after|larger|smaller):",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LooksLikeSentence =
        new(@"[\.!\?]$|,|\b(a|the|if|could|can|because|although|however)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CommonVerbs =
        new(@"\b(is|are|was|were|be|been|being|have|has|had|do|does|did|fix|make|want|need|trying)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QueryKeywords =
        new(@"\b(how|what|why|best|top|price|fix|error|vs|compare)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FileGlobChars =
        new(@"^[\w\-\./\\*?]+$", RegexOptions.Compiled);

    private static readonly Regex PropertyFilter =
        new(@"^(kind|type|ext|name|folder|path|content|body|subject|title|" +
             @"author|creator|from|to|cc|bcc|date|modified|created|accessed|" +
             @"sent|received|before|after|size|larger|smaller|album|artist|" +
             @"genre|duration|rating|tag|tags|store|hasattachment|isread|importance):[^\s]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static float Score(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0f;

        input = input.Trim();

        var terms = TermSplitter.Matches(input);
        var termCount = terms.Count;
        if (termCount == 0) return 0f;

        float score = 0f;

        // --- 1. Length score (flat top at 1–3 terms, taper above) ---
        if (termCount <= 3)
            score += 0.4f;
        else if (termCount <= 6)
            score += 0.4f * (1f - (termCount - 3) / 3f);
        else
            score -= 0.2f;

        // --- 1b. File glob pattern boost ---
        bool hasFileGlob = terms.Cast<Match>().Any(m => IsFileGlobTerm(m.Value));
        if (hasFileGlob)
            score += 0.5f;

        // --- 1c. AQS special-syntax boost ---
        bool hasAqsSyntax = terms.Cast<Match>().Any(m => IsAqsSpecialTerm(m.Value));
        if (hasAqsSyntax)
            score += 0.4f;

        // --- 2. Search-like tokens ---
        var searchLike = terms.Cast<Match>().Count(m => IsSearchLikeTerm(m.Value));
        score += 0.25f * (searchLike / (float)termCount);

        // --- 3. Keyword boost ---
        if (QueryKeywords.IsMatch(input))
            score += 0.2f;

        // --- 4. Penalize sentence structure ---
        if (LooksLikeSentence.IsMatch(input))
            score -= 0.3f;

        // --- 5. Penalize verb-heavy text ---
        var verbMatches = CommonVerbs.Matches(input).Count;
        if (verbMatches > 1)
            score -= Math.Min(0.3f, verbMatches * 0.1f);

        // --- 6. Penalize long inputs ---
        if (input.Length > 80)
            score -= 0.2f;

        // Clamp
        return Math.Clamp(score, 0f, 1f);
    }

    private static bool IsAqsSpecialTerm(string term)
    {
        if (term.StartsWith('"') && term.EndsWith('"') && term.Length > 2)
            return true;

        if (term.Length > 1 && (term[0] == '+' || term[0] == '-') && char.IsLetterOrDigit(term[1]))
            return true;

        return PropertyFilter.IsMatch(term);
    }

    private static bool IsFileGlobTerm(string term)
    {
        if (!FileGlobChars.IsMatch(term)) return false;

        if (term.Contains('*')) return true;

        if (term.Contains('?'))
        {
            var idx = term.IndexOf('?');
            // trailing '?' on a plain word (e.g. "how?") is not a file glob
            return idx < term.Length - 1 || term.Contains('.');
        }

        return false;
    }

    private static bool IsSearchLikeTerm(string term)
    {
        return (term.StartsWith('"') && term.EndsWith('"') && term.Length > 2)
            || term.Contains('*') || term.Contains('?')
            || term.StartsWith('+') || term.StartsWith('-')
            || term.Contains(':') // broader than just known filters
            || FilterPrefix.IsMatch(term);
    }
}
