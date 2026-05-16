using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Stateless scorer that assigns a 0–1 confidence that an input string is a
/// Windows Search query (glob, quoted term, prefix syntax, filter criteria)
/// rather than a conversation or path navigation.
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

    public static float Score(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0f;

        input = input.Trim();

        var terms = TermSplitter.Matches(input);
        var termCount = terms.Count;
        if (termCount == 0) return 0f;

        float score = 0f;

        // --- 1. Length score (peak at ~2–4 terms) ---
        if (termCount <= 6)
            score += 0.4f * (1f - Math.Abs(termCount - 3) / 3f); // bell-ish curve
        else
            score -= 0.2f;

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

    private static bool IsSearchLikeTerm(string term)
    {
        return (term.StartsWith('"') && term.EndsWith('"') && term.Length > 2)
            || term.Contains('*') || term.Contains('?')
            || term.StartsWith('+') || term.StartsWith('-')
            || term.Contains(':') // broader than just known filters
            || FilterPrefix.IsMatch(term);
    }
}
