using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Markdown.Editing;
using Nexaflow.Search;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// The editor surface as a searchable page. Declaring <see cref="ISearchable"/> here rather than on
/// <c>CodeViewModel</c> is deliberate: the plain editor and the "As Code" editor are the same buffer with a
/// different panel beside it, so they should answer <c>?</c> identically and neither should own a private
/// copy of the scan. What it finds is shown by the editor's own marks (<see cref="ContentEditor.Found"/>), the match
/// being looked at outlined.
/// </summary>
public partial class FileTextEditorViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Ceiling on the matches marked. A one-character pattern over a large file finds tens of thousands; past a
    /// screenful they tell the reader nothing.</summary>
    private const int HighlightCap = 5000;

    // ── Visible search state (the status-bar chip binds these) ────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>The lines with a match, counting from nought, in order.</summary>
    private int[] _matchLines = [];

    /// <summary>For each of <see cref="_matchLines"/>, which of the places marked is its first — -1 for a line past the ceiling.</summary>
    private int[] _matchPlaces = [];

    private int _currentMatchIndex = -1;

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Normalised (0–1) vertical positions of the matches, for the minimap tick strip. Watched via
    /// <c>PropertyChanged</c>.</summary>
    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set { _miniMapMarks = value; OnPropertyChanged(); }
    }

    private IReadOnlyList<double> MarksFor(IReadOnlyList<int> lines)
    {
        var total = Math.Max(1, LineCount);
        var marks = new List<double>(lines.Count);
        foreach (var line in lines) marks.Add((double)line / total);
        return marks;
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    /// <summary>Overridden by a subclass that knows what kind of file it is showing (As Code says "code file").</summary>
    public virtual string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName) ? "the file open in the editor" : $"the file '{FileName}' open in the editor";

    /// <summary>Mirrors the Text viewer's curve: a term or two is almost certainly a search; four is
    /// borderline. Past that <see cref="SearchScoring.LooksLikeProse"/> has already bowed out.</summary>
    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return SearchOutcome.Unsupported(error);

        // A line at a time over the version shown, off this thread: nothing is made one string, so a file read over its blocks is searched
        // as one held whole is.
        var document = Document.Current;
        var scan = await Task.Run(() => Scanned(document, matcher, display, ct), ct);

        // Agent-side read: the matches are data, so nothing on screen may move.
        if (display) Apply(scan.Lines, scan.Firsts, scan.Places, SearchSyntax.Format(request));

        return scan.Lines.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(scan.Hits, scan.Lines.Count);
    }

    /// <summary>
    /// Every line of <paramref name="document"/> <paramref name="matcher"/> matches — every term of the query on it — with a hit for the
    /// first of them, and, where they are to be shown, where each match on them stands and which of those is each line's first.
    /// </summary>
    private static (List<int> Lines, List<int> Firsts, List<SearchHit> Hits, List<(int Start, int Length)> Places) Scanned(
        ContentDocument document, TextSearchMatcher matcher, bool display, CancellationToken ct)
    {
        var lines  = new List<int>();
        var firsts = new List<int>();
        var hits   = new List<SearchHit>();
        var places = new List<(int Start, int Length)>();

        for (var line = 0; line < document.LineCount; line++)
        {
            ct.ThrowIfCancellationRequested();

            var (start, end) = document.LineAt(document.OffsetOf(line));
            var text = document.Slice(start, end - start).TrimEnd('\r', '\n');
            if (!matcher.Matches(text)) continue;

            lines.Add(line);
            firsts.Add(display && places.Count < HighlightCap ? places.Count : -1);
            if (hits.Count < SearchHitCap) hits.Add(HitFor(line, text));

            if (display)
                foreach (var (index, length) in matcher.Occurrences(text))
                {
                    if (places.Count >= HighlightCap) break;
                    places.Add((start + index, length));
                }
        }

        return (lines, firsts, hits, places);
    }

    /// <summary>
    /// Narrows the match set to the agent's chosen lines — the count, the marks and next/previous
    /// navigation all follow it, so "show me only the ones about auth" really does filter the view.
    /// </summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var chosen = hits.Select(h => int.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1)
                         .Where(l => l >= 0)
                         .ToHashSet();
        if (chosen.Count == 0) return Task.FromResult(false);

        // The marks are narrowed to exactly these lines: they must agree with the narrowed set, not keep marking the matches the
        // agent discarded.
        var document = Document.Current;
        var lines  = chosen.Order().ToList();
        var places = Editor.Found.Places.Where(place => chosen.Contains(document.LineOf(place.Start))).ToList();
        var firsts = lines.Select(line => places.FindIndex(place => document.LineOf(place.Start) == line)).ToList();

        Apply(lines, firsts, places, CurrentSearchTerm);
        return Task.FromResult(true);
    }

    // ── Shared display path ───────────────────────────────────────────────────

    private void Apply(IReadOnlyList<int> lines, IReadOnlyList<int> firsts, IReadOnlyList<(int Start, int Length)> places, string term)
    {
        _matchLines        = [.. lines];
        _matchPlaces       = [.. firsts];
        _currentMatchIndex = lines.Count > 0 ? 0 : -1;
        Editor.Found.Show(places, _currentMatchIndex >= 0 ? _matchPlaces[0] : -1);
        MiniMapMarks       = MarksFor(lines);
        SearchMatchCount   = lines.Count;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show
        if (lines.Count > 0) MoveCaretToLine(lines[0] + 1);
    }

    private static SearchHit HitFor(int line, string text) =>
        new(line.ToString(CultureInfo.InvariantCulture), $"line {line + 1}", text);

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
        Editor.Found.Show(Editor.Found.Places, _matchPlaces[_currentMatchIndex]);
        MoveCaretToLine(_matchLines[_currentMatchIndex] + 1);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _matchLines        = [];
        _matchPlaces       = [];
        _currentMatchIndex = -1;
        Editor.Found.Show([], -1);
        MiniMapMarks       = [];
        SearchMatchCount   = 0;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;
    }
}
