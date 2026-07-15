using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Core.ViewModels;

/// <summary>A quick-open target: a page or ribbon shortcut, with the action that opens it.</summary>
public sealed record QuickOpenCandidate(string Label, Action Open);

/// <summary>The resolved quick-open for the current input: which target Enter opens, and the ghost-text
/// remainder to show after the caret (null when there's nothing to complete).</summary>
public sealed record QuickOpenResult(QuickOpenCandidate Target, string? Completion);

/// <summary>
/// Resolves the AI input's page/ribbon quick-open — no popup, just the shell's standard status-dot symbol
/// and ghost-text completion. Two ways in: a leading "/" enters quick-open and prefix-matches by name; or a
/// bare input that <em>exactly</em> equals a target name (so typing a page name and pressing Enter opens it).
/// Pure and testable; the shell supplies the candidate set and performs the open.
/// </summary>
public static class QuickOpen
{
    /// <summary>The best match for <paramref name="input"/>, or null to leave the bar to normal AI handling
    /// (a bare "/", a "/query" that matches nothing — e.g. a real <c>/regex/</c> — or plain non-matching text).</summary>
    public static QuickOpenResult? Resolve(string input, IReadOnlyList<QuickOpenCandidate> candidates)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0) return null;

        // Explicit quick-open: "/name" prefix-matches and completes.
        if (trimmed[0] == '/')
        {
            var q = trimmed[1..].Trim();
            if (q.Length == 0) return null;                 // bare "/", nothing to match yet

            var best = BestMatch(q, candidates);
            if (best is null) return null;                  // no page matches → let regex/AI have "/…"

            var completion = best.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                ? best.Label[q.Length..]
                : null;
            return new QuickOpenResult(best, completion);
        }

        // No "/": engage only on an exact full-name match, so a normal AI question isn't hijacked.
        var exact = candidates.FirstOrDefault(c => c.Label.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        return exact is null ? null : new QuickOpenResult(exact, null);
    }

    private static QuickOpenCandidate? BestMatch(string query, IReadOnlyList<QuickOpenCandidate> candidates)
        => candidates
            .Select(c => (Rank: Rank(c.Label, query), Candidate: c))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank!.Value)
            .ThenBy(x => x.Candidate.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Candidate)
            .FirstOrDefault();

    /// <summary>Match rank: 0 = exact, 1 = prefix, 2 = word-start, 3 = substring, null = no match.</summary>
    internal static int? Rank(string label, string query)
    {
        if (query.Length == 0) return null;
        if (label.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (label.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        var idx = label.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return idx > 0 && label[idx - 1] == ' ' ? 2 : 3;
    }
}
