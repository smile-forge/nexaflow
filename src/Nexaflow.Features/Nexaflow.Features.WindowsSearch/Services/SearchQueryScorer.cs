using System.Text.RegularExpressions;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Stateless scorer that assigns a 0–1 confidence that an input string is a
/// Windows Search query (glob, quoted term, prefix syntax, filter criteria)
/// rather than a conversation or path navigation.
/// </summary>
public static class SearchQueryScorer
{
    // Splits on whitespace but keeps quoted phrases as a single term
    private static readonly Regex TermSplitter =
        new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.Compiled);

    private static readonly Regex FilterPrefix =
        new(@"^(size|date|modified|before|after|larger|smaller):",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scoring formula:
    ///   base  = max(0, 0.85 - (termCount - 1) × 0.10)
    ///   bonus = searchLikeTermCount × (0.15 / termCount)
    ///   score = min(1.0, base + bonus)
    /// </summary>
    public static float Score(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0f;

        var terms      = TermSplitter.Matches(input.Trim());
        var termCount  = terms.Count;
        if (termCount == 0) return 0f;

        var searchLike = terms.Cast<Match>().Count(m => IsSearchLikeTerm(m.Value));
        var baseScore  = Math.Max(0f, 0.85f - (termCount - 1) * 0.10f);
        var bonus      = searchLike * (0.15f / termCount);
        return Math.Min(1.0f, baseScore + bonus);
    }

    private static bool IsSearchLikeTerm(string term)
        => (term.StartsWith('"') && term.EndsWith('"') && term.Length > 2)
        || term.Contains('*') || term.Contains('?')
        || term.StartsWith('+') || term.StartsWith('-')
        || FilterPrefix.IsMatch(term);
}
