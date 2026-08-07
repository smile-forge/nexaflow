using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// The conversation browser as an <see cref="ISearchable"/> page: "?" searches the saved conversations —
/// their titles and the text of every message in them — and lists the ones that matched.
/// <para>
/// Message text, not just titles. A conversation's title is a generated slug; nobody remembers it. What
/// people remember is what was said, and "the chat where we worked out the retry backoff" is the only way
/// anyone actually looks for one.
/// </para>
/// <para>
/// The search <b>ignores the date filter</b>, and that is the deliberate part. The filter defaults to the
/// last 7 days, so searching within it would quietly answer "did we discuss this recently" — and its empty
/// result would read as "we never discussed this". A search says which conversation, whenever it was;
/// dismissing it hands the date filter back.
/// </para>
/// </summary>
public partial class AiChatViewModel : ISearchable
{
    /// <summary>Hits returned to the agent. The count it is told is the real total.</summary>
    private const int SearchHitCap = 200;

    private SearchRequest? _searchRequest;
    private HashSet<string>? _pinnedIds;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers by listing
    /// only the matches, so every row is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    /// <summary>True while a "?" search is deciding what the list shows, instead of the date filter.</summary>
    internal bool IsSearchFiltering => _searchRequest is not null || _pinnedIds is not null;

    /// <summary>Which records the list should show — the search's answer when one is running, otherwise
    /// the date range. Called by <c>ApplyFilter</c>, which owns the rebuild either way.</summary>
    private IEnumerable<ConversationRecord> RecordsToList()
    {
        if (_pinnedIds is not null)     return _allRecords.Where(r => _pinnedIds.Contains(r.Id));
        if (_searchRequest is { } query) return _allRecords.Where(r => Matches(query, r));

        var cutoff = FilterCutoff();
        return _allRecords.Where(r => ConversationPurgeTask.LastActivity(r) >= cutoff);
    }

    private static bool Matches(SearchRequest query, ConversationRecord record) =>
        query.Matches(record.Title) || record.Messages.Any(m => query.Matches(m.Text));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        $"the {_allRecords.Count} saved AI conversation(s), by title or by what was said in them";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // A conversation has a title, not a filename — a glob term has nothing here it could constrain.
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to conversations — search their titles or what was said."));

        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        if (request.Terms.Count == 0)
            return Task.FromResult(SearchOutcome.Unsupported("Nothing to search for."));

        // Marshalled even when not displaying: Items is the bound row list, and the agent reads it from
        // its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        if (_allRecords.Count == 0)
            return SearchOutcome.None("There are no saved conversations to search.");

        var matches = _allRecords
            .Where(r => Matches(request, r))
            .OrderByDescending(ConversationPurgeTask.LastActivity)
            .ToList();

        if (display) Apply(request, null, SearchSyntax.Format(request), matches.Count);

        if (matches.Count == 0) return SearchOutcome.None();

        return SearchOutcome.Found(
            matches.Take(SearchHitCap).Select(r => HitFor(r, request)).ToList(), matches.Count);
    }

    /// <summary>Lists exactly the conversations the agent chose, by id.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var ids = hits.Select(h => h.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            var kept = _allRecords.Count(r => ids.Contains(r.Id));
            if (kept == 0) return Task.FromResult(false);

            Apply(null, ids, CurrentSearchTerm.Length == 0 ? $"{kept} selected" : CurrentSearchTerm, kept);
            return Task.FromResult(true);
        });
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    private void Apply(SearchRequest? request, HashSet<string>? pinned, string term, int count)
    {
        _searchRequest = request;
        _pinnedIds     = pinned;
        ApplyFilter();                 // the one rebuild — it reads RecordsToList()

        CurrentSearchTerm = term;
        SearchMatchCount  = count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
    }

    [RelayCommand]
    private void ClearSearch()
    {
        var wasSearching = IsSearchFiltering;
        _searchRequest = null;
        _pinnedIds     = null;

        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;

        if (wasSearching) ApplyFilter();   // back to the date range
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// A filtering page has no "next match" — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The preview names the message that matched, not the conversation's first line — otherwise
    /// every hit in a long thread previews as "hi".</summary>
    private static SearchHit HitFor(ConversationRecord record, SearchRequest query)
    {
        var hit = record.Messages.FirstOrDefault(m => query.Matches(m.Text));
        var body = hit is null
            ? $"{record.Messages.Count} message(s)"
            : $"{(hit.IsUser ? "you" : "assistant")}: {Preview(hit.Text)}";
        return new SearchHit(record.Id, record.Title, body);
    }

    private static string Preview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 140 ? flat : flat[..140] + "…";
    }
}
