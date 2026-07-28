using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Core.Services;

/// <summary>
/// The agent-side half of <see cref="ISearchable"/>: any searchable page automatically exposes these two
/// tools, so "find words in this document that look like adjectives" runs the page's own search rather
/// than the model guessing from a context blob.
/// <para>
/// The pair is deliberately split. <c>search_page</c> reads — it returns matches to the model and changes
/// nothing on screen, so the model can search broadly and then reason about (or filter) the results
/// privately. <c>show_search_results</c> writes — it narrows what the user actually sees to the subset the
/// model kept. Fusing them would force every exploratory search to flash results at the user.
/// </para>
/// Instances are per agent run: the hits from the last <c>search_page</c> are held so the model can refer
/// to them by id without echoing whole rows back.
/// </summary>
public sealed class SearchClientTools(ISearchable page)
{
    /// <summary>Cap on hits returned to the model in one call — enough to reason over, small enough to not
    /// swamp the context. The true total is always reported alongside.</summary>
    private const int DefaultMaxHits = 50;

    private IReadOnlyList<SearchHit> _lastHits = [];

    public IReadOnlyList<IClientTool> Tools =>
    [
        new DelegateClientTool(
            "search_page",
            $"Searches {page.SearchTargetDescription} and returns the matches to you WITHOUT changing what " +
            "the user sees. Use it to find or count things, or to gather candidates you will then filter. " +
            "Set regex=true to match with a .NET regular expression.",
            [
                new ClientToolParameter("query", "Text to find, or a .NET regex pattern when regex=true."),
                new ClientToolParameter("regex", "Treat query as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("match_case", "Case-sensitive match.", Required: false, Type: "boolean"),
                new ClientToolParameter("max", $"Maximum matches to return (default {DefaultMaxHits}).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            SearchAsync,
            parallelizable: true),

        new DelegateClientTool(
            "show_search_results",
            "Narrows what the user sees to a subset of the matches from your last search_page call, given " +
            "their ids. Use it after filtering results by meaning — e.g. search broadly, keep the relevant " +
            "ids, then show only those.",
            [
                new ClientToolParameter("ids", "Ids from search_page — a JSON array, or comma-separated."),
            ],
            ToolSafety.SafeOperation,
            ShowAsync),
    ];

    private async Task<ToolResult> SearchAsync(JsonObject args, CancellationToken ct)
    {
        var query = ToolArgs.Str(args, "query", "text", "pattern");
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("No query given", "The 'query' argument is required.");

        var request = new SearchRequest(
            query,
            IsRegex:   ToolArgs.Bool(args, "regex"),
            MatchCase: ToolArgs.Bool(args, "match_case"));

        var max     = Math.Clamp(ToolArgs.Int(args, "max", DefaultMaxHits), 1, 500);
        var outcome = await page.SearchAsync(request, display: false, ct);

        if (outcome.Failed)
            return ToolResult.Error(
                outcome.Message ?? "Search failed",
                outcome.Message ?? "This page could not run that search.");

        _lastHits = outcome.Hits;

        if (outcome.MatchCount == 0)
            return ToolResult.Ok($"No matches for '{query}'", outcome.Message ?? $"No matches for '{query}'.");

        var shown = outcome.Hits.Take(max).ToList();
        var sb    = new StringBuilder();
        sb.Append(outcome.MatchCount).Append(" match(es)");
        if (shown.Count < outcome.MatchCount) sb.Append(", showing ").Append(shown.Count);
        sb.Append(":\n");
        foreach (var h in shown)
        {
            sb.Append("id=").Append(h.Id).Append(" | ").Append(h.Label);
            if (!string.IsNullOrEmpty(h.Preview)) sb.Append(" | ").Append(h.Preview);
            sb.Append('\n');
        }

        return ToolResult.Ok($"{outcome.MatchCount} match(es) for '{query}'", sb.ToString());
    }

    private async Task<ToolResult> ShowAsync(JsonObject args, CancellationToken ct)
    {
        var ids = ReadIds(args);
        if (ids.Count == 0)
            return ToolResult.Error("No ids given", "The 'ids' argument is required — use ids from search_page.");

        // Resolve against the last search so the page gets its own hit objects back, not model-authored ones.
        var known    = _lastHits.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var resolved = ids.Where(known.ContainsKey).Select(id => known[id]).ToList();
        if (resolved.Count == 0)
            return ToolResult.Error(
                "No matching ids",
                "None of those ids came from the last search_page call. Run search_page first, then pass ids from its result.");

        var rendered = await page.ShowResultsAsync(resolved, ct);
        if (!rendered)
            return ToolResult.Ok(
                "Page can't show a filtered subset",
                "This page cannot display an arbitrary subset of matches. Describe the matches to the user instead.");

        var skipped = ids.Count - resolved.Count;
        var summary = $"Showing {resolved.Count} result(s)" + (skipped > 0 ? $" ({skipped} unknown id(s) ignored)" : "");
        return ToolResult.Ok(summary, summary + ".");
    }

    // The model may send ids either as a JSON array or as one comma-separated string.
    private static List<string> ReadIds(JsonObject args)
    {
        if (args.TryGetPropertyValue("ids", out var node) && node is JsonArray arr)
            return arr.Where(n => n is not null)
                      .Select(n => n!.ToString().Trim())
                      .Where(s => s.Length > 0)
                      .ToList();

        var raw = ToolArgs.Str(args, "ids", "id");
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
