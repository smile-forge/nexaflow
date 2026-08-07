using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Search;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Running a page search over a knowledge graph: node names, and the source behind them.
/// <para>
/// It exists because <see cref="GraphQuery"/>'s two entry points answer slightly narrower questions than a
/// page search asks. <c>Search</c> is a case-insensitive substring match with its own ranking, and
/// <c>Grep</c> compiles its pattern <c>IgnoreCase</c> — both right for the assistant's tools, neither able
/// to honour a case-sensitive query on its own. Rather than teach the graph library about
/// <see cref="SearchRequest"/>, the widening rule the search contract already prescribes is applied here:
/// ask the backend the widest question it understands, then re-filter with the real one.
/// </para>
/// </summary>
internal static class GraphTextSearch
{
    /// <summary>The pattern to hand a regex engine for this request — the user's own when they wrote one,
    /// their text escaped when they did not. Someone typing <c>List&lt;int&gt;</c> is looking for that, not
    /// for a broken pattern.</summary>
    internal static string PatternFor(SearchRequest request) =>
        request.IsRegex ? request.Text : Regex.Escape(request.Text.Trim());

    /// <summary>Compiles the request, or null when the pattern is malformed (the caller reports it).</summary>
    internal static Regex? TryCompile(SearchRequest request)
    {
        try
        {
            return new Regex(PatternFor(request),
                request.MatchCase ? RegexOptions.CultureInvariant
                                  : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// Nodes whose label or id satisfies the request.
    /// <para>
    /// A plain, case-insensitive query goes to <see cref="GraphQuery.Search"/> so it keeps that ranking —
    /// exact label, then prefix, then substring — which is the order someone exploring wants and which a
    /// regex scan would throw away. Anything the ranked search cannot express (a pattern, or case
    /// sensitivity) is matched here instead, in the graph's own node order.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<GraphNode> Names(KnowledgeGraph graph, SearchRequest request, Regex regex)
    {
        if (!request.IsRegex && !request.MatchCase)
            return GraphQuery.Search(graph, request.Text.Trim());

        return [.. graph.Nodes
            .Where(n => regex.IsMatch(n.Label ?? string.Empty) || regex.IsMatch(n.Id))
            .OrderBy(n => GraphQuery.TypeRank(n.Type))
            .ThenBy(n => n.Label?.Length ?? int.MaxValue)
            .ThenBy(n => n.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Source lines satisfying the request, across every code node in the graph.
    /// <para>
    /// <see cref="GraphQuery.Grep"/> always compiles <c>IgnoreCase</c>, so a case-sensitive request asks it
    /// the insensitive (wider) question and re-filters the lines here. Widening is safe because it can only
    /// over-match; the re-filter restores exactness. The cap applies to the wide pass, so a case-sensitive
    /// search can be capped by matches it then discards — which is why the caller reports a capped count as
    /// a floor rather than a total.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<GraphQuery.GrepHit> InSource(
        KnowledgeGraph graph, SearchRequest request, Regex regex, GraphQuery.ReadLines read, int cap)
    {
        var wide = GraphQuery.Grep(graph, PatternFor(request), read, fromId: null, hops: 2, limit: cap);
        return request.MatchCase ? [.. wide.Where(h => regex.IsMatch(h.Text))] : wide;
    }
}
