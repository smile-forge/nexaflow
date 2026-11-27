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
/// <see cref="SearchRequest"/> every viewer reads, so a term means here exactly what it means in the text viewer, the editor and the
/// markdown surface.
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

    /// <summary>The lines of the whole log the search found, from nought, in order.</summary>
    private long[] _matchLines = [];

    private int _currentMatchIndex = -1;

    /// <summary>The search shown, kept so the history landing or the log growing can run it again.</summary>
    private SearchRequest? _activeSearch;

    /// <summary>The lines the agent narrowed the search to, whose matches alone are marked — or null, every line found.</summary>
    private HashSet<long>? _narrowedTo;

    /// <summary>What the search found, marked on the lines drawn, with the match gone to outlined.</summary>
    private readonly MatchMarks _matchMarks = new();

    /// <summary>The line of the whole log, from nought, the search last went to — or -1, where it has gone nowhere.</summary>
    public long CurrentMatchLine => _currentMatchIndex >= 0 && _currentMatchIndex < _matchLines.Length ? _matchLines[_currentMatchIndex] : -1;

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

    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
        => _shell.RunOnUiAsync(() => Task.FromResult(NarrowTo(hits)));

    // ── The scan ──────────────────────────────────────────────────────────────

    private async Task<SearchOutcome> RunSearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out _, out var error))
            return SearchOutcome.Unsupported(error);

        var hits = new List<SearchHit>();
        var read = Document.Current;
        var lines = await ScanAsync(request, ct, hits);

        if (display)
        {
            _activeSearch = request;
            _narrowedTo = null;
            Apply(lines, SearchSyntax.Format(request));

            // The log grew, or its history landed, while it was read: run again over what it is now, so the count is not of what it was.
            if (!ReferenceEquals(read, Document.Current)) await RescanSearchAsync();
        }

        return Outcome(lines.Length, hits);
    }

    /// <summary>
    /// Every line of the whole log the request matches, from nought — read a line at a time off the page, from the log as it stood when
    /// the search began — with the first of them as hits, where <paramref name="collect"/> is asked for.
    /// </summary>
    private async Task<long[]> ScanAsync(SearchRequest request, CancellationToken ct, List<SearchHit>? collect = null)
    {
        var lines = _shown.Lines();
        var matching = new List<long>();

        await Task.Run(() => lines((line, _, text) =>
        {
            if (!request.Matches(text)) return;

            matching.Add(line);
            if (collect is not null && collect.Count < SearchHitCap) collect.Add(HitFor(line, text));
        }, ct), ct);

        return [.. matching];
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

    /// <param name="parkedOn">The line of the whole log, from nought, to stay parked on rather than going to the first match — what a search run
    /// again as a live tail grows does, so the reader is not dragged back up the log on every append.</param>
    private void Apply(long[] lines, string term, long? parkedOn = null)
    {
        _matchLines        = lines;
        _currentMatchIndex = lines.Length == 0 ? -1 : parkedOn is { } parked ? Parked(lines, parked) : 0;
        SearchMatchCount   = lines.Length;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show

        if (_activeSearch is { } request && lines.Length > 0)
            _matchMarks.Show((line, text) => _narrowedTo is { } kept && !kept.Contains(_shown.LinesBefore + line) ? [] : request.Occurrences(text));
        else
            _matchMarks.Show(null);

        if (lines.Length > 0 && parkedOn is null) GoToMatch(lines[0]);
    }

    /// <summary>Where among <paramref name="lines"/>, in order, line <paramref name="parked"/> stands — or the first after it, or the last.</summary>
    private static int Parked(long[] lines, long parked)
    {
        var found = Array.BinarySearch(lines, parked);
        return Math.Min(lines.Length - 1, found >= 0 ? found : ~found);
    }

    /// <summary>Narrows the match set to the agent's chosen lines — the count, the marks and next/previous all follow it, so "show me only the
    /// ones about auth" really does filter the view.</summary>
    private bool NarrowTo(IReadOnlyList<SearchHit> hits)
    {
        var lines = hits.Select(h => long.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1)
                        .Where(l => l >= 0)
                        .Distinct()
                        .Order()
                        .ToArray();
        if (lines.Length == 0) return false;

        _narrowedTo = [.. lines];
        Apply(lines, CurrentSearchTerm);
        return true;
    }

    /// <summary>
    /// Runs the active search again over the whole log — what the history landing and every append call for: the count is of the whole log
    /// as it is now, parked on the match it was on.
    /// </summary>
    internal async Task RescanSearchAsync()
    {
        if (!IsSearchActive || _activeSearch is not { } request || _narrowedTo is not null) return;

        var parked = Math.Max(0, CurrentMatchLine);
        var lines = await ScanAsync(request, CancellationToken.None);
        if (!ReferenceEquals(request, _activeSearch)) return;

        Apply(lines, CurrentSearchTerm, parkedOn: parked);
    }

    /// <summary>Moves what the search found on by <paramref name="lines"/> — as many as were put before them, when the history lands.</summary>
    private void MoveSearch(long lines)
    {
        if (lines == 0) return;

        _matchLines = [.. _matchLines.Select(line => line + lines)];
        if (_narrowedTo is { } kept) _narrowedTo = [.. kept.Select(line => line + lines)];
    }

    // ── Navigation / dismissal (the status-bar chip) ──────────────────────────

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    [RelayCommand]
    private void FindNextMatch() => StepMatch(+1);

    [RelayCommand]
    private void FindPreviousMatch() => StepMatch(-1);

    private void StepMatch(int delta)
    {
        if (_matchLines.Length == 0) return;
        _currentMatchIndex = (_currentMatchIndex + delta + _matchLines.Length) % _matchLines.Length;
        GoToMatch(_matchLines[_currentMatchIndex]);
    }

    /// <summary>
    /// Goes to the first match on line <paramref name="line"/> of the whole log, from nought: the caret at its start, the match outlined, and
    /// the line in the middle of the page — a window onto a long log brought there first.
    /// </summary>
    private void GoToMatch(long line)
    {
        if (_shown.OffsetOfLine(line) is not { } at) return;

        var offset = _shown.DocumentOffset(at);
        var document = Document.Current;
        var (start, end) = document.LineAt(offset);
        var first = _activeSearch?.Occurrences(document.Slice(start, end - start)) is [var (index, length), ..] ? (start + index, length) : ((int, int)?)null;

        Editor.MoveCaretTo(first?.Item1 ?? start);
        _matchMarks.LookAt(first);
        Editor.ScrollToLine(document.LineOf(start), ShownPlace.Centre);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _matchLines        = [];
        _currentMatchIndex = -1;
        _activeSearch      = null;
        _narrowedTo        = null;
        _matchMarks.Show(null);
        SearchMatchCount   = 0;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;
    }
}
