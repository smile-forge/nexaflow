using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Localization;
using Nexaflow.Markdown.Editing;
using Nexaflow.Search;
using System.Globalization;

namespace Nexaflow.Features.Logs.ViewModels;

/// <summary>
/// The log tab as a searchable page. <c>?</c> reads the whole log a line at a time, off the page, through the same
/// <see cref="SearchRequest"/> every viewer reads and the same search over a file's lines (<see cref="FileFind"/>), so a term means here
/// exactly what it means in the text viewer, the editor and the markdown surface.
/// <para>
/// This is a THIRD, independent layer over the log — it neither reads nor writes the regex fade-filter or the custom highlight term. Those
/// are different questions: the filter dims lines that don't match (and is deliberately case-insensitive), the custom term is the user's
/// own persistent marker, and a search finds, counts and steps through occurrences of a word. Folding any two together would make one of
/// them lie.
/// </para>
/// </summary>
public sealed partial class LogViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    // ── Visible search state (the status-bar chip binds these) ────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>The search shown and what it found, marked on the lines drawn by the editor's own marks, the match gone to outlined and put in the middle of the page.</summary>
    private readonly FileFind _find;

    /// <summary>The line of the whole log, from nought, the search last went to — or -1, where it has gone nowhere.</summary>
    public long CurrentMatchLine => _find.LookedAtLine;

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName)  ? "the open log file"
      : IsLoadingHead                   ? $"the log file '{FileName}' — the most recent lines (earlier history is still loading)"
      :                                   $"the log file '{FileName}'";

    /// <summary>Mirrors the other document viewers' curve: a term or two is almost certainly a search; four is borderline. Past that
    /// <see cref="SearchScoring.LooksLikeProse"/> has already bowed out.</summary>
    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    // What is shown is the page's, so both paths start on its thread — where the log is taken as it stands — and read it off the page.
    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        => _shell.RunOnUiAsync(() => RunSearchAsync(request, display, ct));

    /// <summary>Narrows the match set to the agent's chosen lines — the count, the marks and next/previous all follow it, so "show me only the
    /// ones about auth" really does filter the view.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
        => _shell.RunOnUiAsync(() => Task.FromResult(_find.Narrow(hits.Select(h => long.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1))));

    // ── The scan ──────────────────────────────────────────────────────────────

    private async Task<SearchOutcome> RunSearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out _, out var error))
            return SearchOutcome.Unsupported(error);

        // Shown, the log growing or its history landing while it is read has it read again at once, so the count is of what it is now.
        var query = new LineQuery(request.Matches, request.Occurrences);
        var found = display ? await _find.FindAsync(query, SearchHitCap, ct) : await _find.ReadAsync(query, SearchHitCap, ct);
        if (display) CurrentSearchTerm = SearchSyntax.Format(request);

        return Outcome(found.Count, [.. found.First.Select(line => HitFor(line.Line, line.Text))]);
    }

    /// <summary>
    /// While the head is still streaming in only the tail is in the document, so say so rather than reporting a count that is about to
    /// change. We never await the head: it can be gigabytes, and <c>SearchAsync</c> is driven both from a keystroke and from the agent
    /// loop. The search runs again when it lands, so the number completes itself.
    /// </summary>
    private SearchOutcome Outcome(int total, IReadOnlyList<SearchHit> hits)
    {
        if (!IsLoadingHead)
            return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);

        var message = $"Searched the {LineCount} most recent line(s) — the earlier history of "
                    + $"'{FileName}' is still loading.";

        // Narrowed() would report hits.Count as the total, which under-counts once the cap bites.
        return new SearchOutcome(total, hits, message);
    }

    private static SearchHit HitFor(long line, string text) =>
        new(line.ToString(CultureInfo.InvariantCulture), Str.Format("Logs.Search.HitLabelFormat", line + 1), text);

    // ── Display ───────────────────────────────────────────────────────────────

    /// <summary>What the search found, or which of it is looked at, changed: the count and whether it shows said again.</summary>
    private void OnFindChanged(object? sender, EventArgs args)
    {
        SearchMatchCount = _find.Count;
        IsSearchActive   = _find.IsShowing;   // true even at zero matches: "no matches for X" is a result to show
    }

    /// <summary>
    /// Runs the active search again over the whole log — what the history landing and every append call for: the count is of the whole log
    /// as it is now, parked on the match it was on.
    /// </summary>
    internal Task RescanSearchAsync() => _find.AgainAsync();

    /// <summary>Moves what the search found on by <paramref name="lines"/> — as many as were put before them, when the history lands.</summary>
    private void MoveSearch(long lines) => _find.Shift(lines);

    // ── Navigation / dismissal (the status-bar chip) ──────────────────────────

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    [RelayCommand]
    private void FindNextMatch() => _find.Next();

    [RelayCommand]
    private void FindPreviousMatch() => _find.Previous();

    [RelayCommand]
    private void ClearSearch()
    {
        _find.Clear();
        CurrentSearchTerm = string.Empty;
    }
}
