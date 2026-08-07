using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.AIChat.ViewModels.Timeline;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One open conversation as an <see cref="ISearchable"/> page: "?" matches the text of every message in
/// the thread — yours and the assistant's — marks the bubbles that hit, and steps the view through them.
/// <para>
/// It <em>marks</em> rather than filters, which is the design decision worth stating. A conversation is a
/// thread that is read in order: hiding the messages that missed would leave replies with nothing to reply
/// to and answers with no question above them. So a hit gets a wash, and ‹ › scrolls between them.
/// </para>
/// <para>
/// A hit's id is its position in the thread, because that is the only handle a rendered bubble has — an
/// assistant message carries text, not an id. Any rebuild of the thread (a load, a rewind) therefore drops
/// the search rather than renumbering onto bubbles that were never matched.
/// </para>
/// </summary>
public partial class ConversationViewModel : ISearchable
{
    /// <summary>Timeline positions of the current hits, in thread order.</summary>
    private readonly List<int> _searchHits = [];
    private int _currentHit = -1;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Timeline index the view should bring into view, or -1 for none. Reset to -1 between steps
    /// so stepping onto the same hit twice still moves the thread.</summary>
    [ObservableProperty] private int _scrollToTimelineIndex = -1;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Messages.Count == 0
            ? "this conversation (it has no messages yet)"
            : $"the {Messages.Count} message(s) in this conversation, by what was said";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // Marshalled even when not displaying: Timeline is the bound thread, and the agent reads it from
        // its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        if (Timeline.Count == 0)
            return SearchOutcome.None("This conversation has no messages yet.");

        var hits = new List<int>();
        for (var i = 0; i < Timeline.Count; i++)
            if (TextOf(Timeline[i]) is { } text && matcher.Matches(text)) hits.Add(i);

        if (display) Apply(hits, SearchSyntax.Format(request));

        return hits.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(hits));
    }

    /// <summary>Marks exactly the messages the agent chose and scrolls to the first — the same view change
    /// the user's own search makes, so "I've highlighted those three" is true when it says so.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var chosen = hits.Select(h => int.TryParse(h.Id, out var i) ? i : -1)
                             .Where(i => i >= 0 && i < Timeline.Count && TextOf(Timeline[i]) is not null)
                             .Distinct()
                             .OrderBy(i => i)
                             .ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });

    // ── Marking + stepping ────────────────────────────────────────────────────

    private void Apply(IReadOnlyList<int> hits, string term)
    {
        _searchHits.Clear();
        _searchHits.AddRange(hits);

        for (var i = 0; i < Timeline.Count; i++) Mark(Timeline[i], _searchHits.Contains(i));

        CurrentSearchTerm = term;
        SearchMatchCount  = _searchHits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        _currentHit       = _searchHits.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) ScrollTo(_searchHits[0]);
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_searchHits.Count == 0) return;
        _currentHit = ((_currentHit + delta) % _searchHits.Count + _searchHits.Count) % _searchHits.Count;
        ScrollTo(_searchHits[_currentHit]);
    }

    /// <summary>Reset first: the property is what the view watches, so stepping back onto the same index
    /// would otherwise raise nothing and the thread would sit still.</summary>
    private void ScrollTo(int timelineIndex)
    {
        ScrollToTimelineIndex = -1;
        ScrollToTimelineIndex = timelineIndex;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits.Clear();
        _currentHit = -1;
        foreach (var item in Timeline) Mark(item, false);
        IsSearchActive        = false;
        SearchMatchCount      = 0;
        CurrentSearchTerm     = string.Empty;
        ScrollToTimelineIndex = -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The searchable text of a timeline entry, or null for one that isn't a message — a tool
    /// batch or the live activity line is machinery, not something that was said.</summary>
    private static string? TextOf(object item) => item switch
    {
        TimelineUserMessage u      => u.Text,
        TimelineAssistantMessage a => a.Text,
        _                          => null,
    };

    private static void Mark(object item, bool hit)
    {
        switch (item)
        {
            case TimelineUserMessage u:      u.IsSearchHit = hit; break;
            case TimelineAssistantMessage a: a.IsSearchHit = hit; break;
        }
    }

    private IReadOnlyList<SearchHit> HitsFor(IEnumerable<int> indices) =>
        indices.Select(i => new SearchHit(
                   i.ToString(),
                   Timeline[i] is TimelineUserMessage ? "you" : "assistant",
                   Preview(TextOf(Timeline[i]) ?? string.Empty)))
               .ToList();

    private static string Preview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 160 ? flat : flat[..160] + "…";
    }
}
