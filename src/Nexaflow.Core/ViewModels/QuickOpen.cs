using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.ViewModels;

/// <summary>The resolved quick-open for the current input: which target Enter opens, and the ghost-text
/// remainder to show after the caret (null when there's nothing to complete).</summary>
public sealed record QuickOpenResult(QuickOpenTarget Target, string? Completion);

/// <summary>
/// Resolves the AI input's page/ribbon quick-open. Two ways in: a leading "/" enters quick-open and
/// prefix-matches by name (with completion); or a bare input that <em>exactly</em> equals a target name
/// (so typing a page name and pressing Enter opens it, without hijacking a normal AI question). Pure and
/// testable; the <c>PageQuickOpenHandler</c> supplies the candidate set and performs the open.
/// </summary>
public static class QuickOpen
{
    /// <summary>The best match for <paramref name="input"/>, or null to leave the bar to normal AI handling
    /// (a bare "/", a "/query" that matches nothing — e.g. a real <c>/regex/</c> — or plain non-matching text).</summary>
    public static QuickOpenResult? Resolve(string input, IReadOnlyList<QuickOpenTarget> targets)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0) return null;

        // Explicit quick-open: "/name" prefix-matches and completes.
        if (trimmed[0] == '/')
        {
            var q = trimmed[1..].Trim();
            if (q.Length == 0) return null;                 // bare "/", nothing to match yet

            var best = BestMatch(q, targets);
            if (best is null) return null;                  // no page matches → let regex/AI have "/…"

            var completion = best.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                ? best.Label[q.Length..]
                : null;
            return new QuickOpenResult(best, completion);
        }

        // No "/": engage only on an exact full-name match, so a normal AI question isn't hijacked.
        var exact = targets.FirstOrDefault(c => c.Label.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        return exact is null ? null : new QuickOpenResult(exact, null);
    }

    private static QuickOpenTarget? BestMatch(string query, IReadOnlyList<QuickOpenTarget> targets)
        => targets
            .Select(c => (Rank: Rank(c.Label, query), Target: c))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank!.Value)
            .ThenBy(x => x.Target.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Target)
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
