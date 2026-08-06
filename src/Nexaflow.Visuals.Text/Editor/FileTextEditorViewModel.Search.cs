using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// The editor surface as a searchable page. Declaring <see cref="ISearchable"/> here rather than on
/// <c>CodeViewModel</c> is deliberate: the plain editor and the "As Code" editor are the same buffer with a
/// different panel beside it, so they should answer <c>?</c> identically and neither should own a private
/// copy of the scan.
/// </summary>
public partial class FileTextEditorViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Ceiling on painted highlight spans. A one-character pattern over a large buffer produces
    /// tens of thousands of geometries; past a screenful they cost render time and tell the user nothing.</summary>
    private const int HighlightCap = 5000;

    // ── Visible search state (the status-bar chip binds these) ────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    private int[] _matchLines = [];
    private int   _currentMatchIndex = -1;

    private IReadOnlyList<(int Offset, int Length)> _searchHighlights = [];

    /// <summary>Spans the view paints behind matches. Watched via <c>PropertyChanged</c>, like the Text viewer.</summary>
    public IReadOnlyList<(int Offset, int Length)> SearchHighlights
    {
        get => _searchHighlights;
        private set { _searchHighlights = value; OnPropertyChanged(); }
    }

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Normalised (0–1) vertical positions of the matches, for the minimap tick strip. Watched via
    /// <c>PropertyChanged</c>, like <see cref="SearchHighlights"/>.</summary>
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

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        var body  = Document.Text;
        var lines = new List<int>();
        var hits  = new List<SearchHit>();
        var spans = new List<(int Offset, int Length)>();

        foreach (var line in matcher.ScanLines(body))
        {
            ct.ThrowIfCancellationRequested();
            lines.Add(line.Index);

            if (hits.Count < SearchHitCap)
                hits.Add(HitFor(line.Index, line.Text));

            if (display && spans.Count < HighlightCap)
                foreach (var (index, length) in matcher.Occurrences(line.Text))
                {
                    if (spans.Count >= HighlightCap) break;
                    spans.Add((line.Offset + index, length));
                }
        }

        // Agent-side read: the matches are data, so nothing on screen may move.
        if (!display)
            return Task.FromResult(lines.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, lines.Count));

        Apply(lines, spans, SearchSyntax.Format(request));
        return Task.FromResult(lines.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, lines.Count));
    }

    /// <summary>
    /// Narrows the match set to the agent's chosen lines — the count, the highlights and next/previous
    /// navigation all follow it, so "show me only the ones about auth" really does filter the view.
    /// </summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var lines = hits.Select(h => int.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1)
                        .Where(l => l >= 0)
                        .Distinct()
                        .OrderBy(l => l)
                        .ToList();
        if (lines.Count == 0) return Task.FromResult(false);

        // Re-derive the spans for exactly these lines: the highlights must agree with the narrowed set,
        // not keep painting the matches the agent discarded.
        var kept = new HashSet<int>(lines);
        List<(int Offset, int Length)> spans = _searchHighlights.Count == 0 ? [] : SpansOn(kept);

        Apply(lines, spans, CurrentSearchTerm);
        return Task.FromResult(true);
    }

    // ── Shared display path ───────────────────────────────────────────────────

    private void Apply(IReadOnlyList<int> lines, IReadOnlyList<(int Offset, int Length)> spans, string term)
    {
        _matchLines        = [.. lines];
        _currentMatchIndex = lines.Count > 0 ? 0 : -1;
        SearchHighlights   = spans;
        MiniMapMarks       = MarksFor(lines);
        SearchMatchCount   = lines.Count;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show
        if (lines.Count > 0) RequestScrollToLine(lines[0] + 1);
    }

    /// <summary>The already-computed highlight spans that fall on <paramref name="lines"/>.</summary>
    private List<(int Offset, int Length)> SpansOn(HashSet<int> lines)
    {
        var kept = new List<(int Offset, int Length)>();
        foreach (var span in _searchHighlights)
        {
            var line = Document.GetLineByOffset(Math.Clamp(span.Offset, 0, Document.TextLength)).LineNumber - 1;
            if (lines.Contains(line)) kept.Add(span);
        }
        return kept;
    }

    private static SearchHit HitFor(int line, string text) =>
        new(line.ToString(CultureInfo.InvariantCulture), $"line {line + 1}", text.TrimEnd('\r'));

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
        RequestScrollToLine(_matchLines[_currentMatchIndex] + 1);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _matchLines        = [];
        _currentMatchIndex = -1;
        SearchHighlights   = [];
        MiniMapMarks       = [];
        SearchMatchCount   = 0;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;
    }
}
