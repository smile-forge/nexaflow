using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Scratchpad.ViewModels;

/// <summary>
/// The board as an <see cref="ISearchable"/> page: "?" matches a post-it's content, <b>hides the notes that
/// missed</b>, paints the matched words inside the ones that didn't, and pans the canvas to each in turn.
/// <para>
/// Hiding rather than marking is what a corkboard needs. The notes are scattered across an infinite canvas
/// at whatever zoom the user left it — a mark on a note two screens away is a mark nobody sees. Clearing
/// the notes that missed leaves the answer, and the pan puts it under the eye.
/// </para>
/// <para>
/// Panning and not zooming: the scale is the user's, and quietly changing it to fit a result would lose the
/// working view they set up. The board moves; how close it is stays theirs.
/// </para>
/// </summary>
public sealed partial class ScratchpadViewModel : ISearchable
{
    /// <summary>The current hits, in board order (top-left first) — the order the chip steps through.</summary>
    private readonly List<PostItViewModel> _searchHits = [];
    private int _currentHit = -1;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>The note the view should bring into view, or null. Set to null between steps so stepping
    /// onto the same note twice still moves the board.</summary>
    [ObservableProperty] private PostItViewModel? _scrollToNote;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Notes.Count == 0
            ? "the scratchpad board (it has no notes yet)"
            : $"the {Notes.Count} post-it note(s) on this board, by note content";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // Marshalled even when not displaying: Notes is the bound board, and the agent reads it from its
        // own thread.
        return OnUi(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        if (Notes.Count == 0)
            return SearchOutcome.None("There are no notes on this board to search.");

        // The recycle bin is deliberately out of scope: a binned note is not on the board, and hiding the
        // board to show one would be a strange answer to "find this".
        var hits = InBoardOrder(Notes.Where(n => matcher.Matches(n.Content)));

        if (display) Apply(hits, matcher, SearchSyntax.Format(request));

        return hits.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(hits));
    }

    /// <summary>Narrows the board to exactly the notes the agent chose.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var ids = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return Task.FromResult(false);

        return OnUi(() =>
        {
            var chosen = InBoardOrder(Notes.Where(n => ids.Contains(n.Note.Id.ToString())));
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, _matcher, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    /// <summary>The query the visible notes are painted with, kept so a narrowed result stays painted.</summary>
    private TextSearchMatcher? _matcher;

    private void Apply(IReadOnlyList<PostItViewModel> hits, TextSearchMatcher? matcher, string term)
    {
        _searchHits.Clear();
        _searchHits.AddRange(hits);
        _matcher = matcher;

        foreach (var note in Notes)
        {
            var hit = _searchHits.Contains(note);
            note.IsSearchHit      = hit;
            note.IsHiddenBySearch = !hit;
            note.SearchMatcher    = hit ? matcher : null;
        }

        CurrentSearchTerm = term;
        SearchMatchCount  = _searchHits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        _currentHit       = _searchHits.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) MoveTo(_searchHits[0]);
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_searchHits.Count == 0) return;
        _currentHit = ((_currentHit + delta) % _searchHits.Count + _searchHits.Count) % _searchHits.Count;
        MoveTo(_searchHits[_currentHit]);
    }

    /// <summary>Reset first: the property is what the view watches, so stepping back onto the same note
    /// would otherwise raise nothing and the board would sit still.</summary>
    private void MoveTo(PostItViewModel note)
    {
        ScrollToNote = null;
        ScrollToNote = note;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits.Clear();
        _matcher    = null;
        _currentHit = -1;
        foreach (var note in Notes)
        {
            note.IsSearchHit      = false;
            note.IsHiddenBySearch = false;
            note.SearchMatcher    = null;
        }
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        ScrollToNote      = null;
    }

    // ── Board movement ────────────────────────────────────────────────────────

    /// <summary>
    /// Pans so <paramref name="note"/> sits in the middle of a <paramref name="viewW"/>×<paramref name="viewH"/>
    /// viewport, at the current scale. Takes the viewport as arguments for the same reason
    /// <see cref="ZoomToFitWithViewport"/> does: the size belongs to the control, the arithmetic doesn't.
    /// </summary>
    public void CenterOnWithViewport(PostItViewModel note, double viewW, double viewH)
    {
        OffsetX = viewW / 2 - (note.X + note.Width  / 2) * Scale;
        OffsetY = viewH / 2 - (note.Y + note.Height / 2) * Scale;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reading order across the board — top row first, left to right — so "next match" walks the
    /// board the way an eye would rather than in whatever order the notes were created.</summary>
    private static List<PostItViewModel> InBoardOrder(IEnumerable<PostItViewModel> notes) =>
        notes.OrderBy(n => n.Y).ThenBy(n => n.X).ToList();

    /// <summary>The shell is optional here (the board runs without one in tests), so a missing one means
    /// "already on the right thread" rather than "can't search".</summary>
    private Task<T> OnUi<T>(Func<Task<T>> work) => _shellServices?.RunOnUiAsync(work) ?? work();

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<PostItViewModel> notes) =>
        notes.Select(n => new SearchHit(
                  n.Note.Id.ToString(),   // the id the scratchpad read tools already speak
                  n.RecycleBinLabel,  // the note's first line — the closest thing it has to a title
                  Preview(n.Content)))
             .ToList();

    private static string Preview(string content)
    {
        var flat = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 160 ? flat : flat[..160] + "…";
    }
}
