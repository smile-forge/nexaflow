using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.ViewModels;

/// <summary>The resolved quick-open for the current input: which target Enter opens, and the ghost-text
/// remainder to show after the caret (null when there's nothing to complete).</summary>
public sealed record QuickOpenResult(QuickOpenTarget Target, string? Completion);

/// <summary>
/// Resolves the AI input's page/ribbon quick-open. The leading "/" is the handler's <c>Symbol</c>, stripped
/// by the router before it gets here, so <paramref name="prefixed"/> distinguishes the two modes: when true
/// (the user typed "/"), prefix-match by name and offer a completion; when false (a bare input), engage only
/// on an <em>exact</em> full-name match, so a page name opens on Enter without hijacking a normal AI question.
/// Pure and testable; the <c>PageQuickOpenHandler</c> supplies the candidate set and performs the open.
/// </summary>
public static class QuickOpen
{
    /// <summary>The best match for the (symbol-stripped) <paramref name="query"/>, or null to leave the bar to
    /// normal AI handling (an empty prefixed query, a "/query" that matches no page, or plain non-matching text).</summary>
    public static QuickOpenResult? Resolve(string query, bool prefixed, IReadOnlyList<QuickOpenTarget> targets)
    {
        var q = query.Trim();
        if (q.Length == 0) return null;

        // Explicit quick-open ("/name"): prefix-match and complete the remainder.
        if (prefixed)
        {
            var best = BestMatch(q, targets);
            if (best is null) return null;                  // names no page → let AI have "/…"

            var completion = best.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                ? best.Label[q.Length..]
                : null;
            return new QuickOpenResult(best, completion);
        }

        // Bare input: engage only on an exact full-name match, so a normal AI question isn't hijacked.
        var exact = targets.FirstOrDefault(c => c.Label.Equals(q, StringComparison.OrdinalIgnoreCase));
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
