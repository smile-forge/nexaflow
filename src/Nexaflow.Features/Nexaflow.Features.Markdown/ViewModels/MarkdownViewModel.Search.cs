using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Visuals.Text.Markdown;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// The markdown editor as a searchable page. It searches whatever the page is currently showing: the raw
/// source when the source box is open, the rendered text otherwise — a "?" search lights up what the user
/// is actually looking at, and never switches the view out from under them.
/// <para>
/// The rendered highlight is done by the view (it owns the live <c>FlowDocument</c>); the source highlight
/// is a selection in the source box, driven from here. An AI-invoked search (display: false) reads the
/// document's source and simply reports where the matches are, changing nothing on screen.
/// </para>
/// </summary>
public sealed partial class MarkdownViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    // ── Visible search state (the toolbar chip binds these) ───────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // A live search belongs to the surface it ran against; switching surfaces abandons it rather than
    // leaving a stale chip over a view with no highlights.
    partial void OnSourceOnlyChanged(bool value) { if (IsSearchActive) ClearSearch(); }

    // ── View collaboration ────────────────────────────────────────────────────
    // The rendered surface's highlighting lives in the view (it holds the FlowDocument); the source box's
    // selection is driven from here. Both are set by MarkdownView; absent in a headless test, where only
    // the source path and the non-display (agent) path are exercised.

    /// <summary>Runs the matcher against the rendered surface, highlighting it, and returns the matches.
    /// Set by the view (public because the search hooks are page surface, like <see cref="SourceSelectionRequested"/>).</summary>
    public Func<TextSearchMatcher, IReadOnlyList<RenderedMatch>>? FindInRendered { get; set; }
    public Action<int>? StepRendered { get; set; }
    public Action?      ClearRendered { get; set; }

    /// <summary>The rendered surface's normalised match positions, for the minimap.</summary>
    public Func<IReadOnlyList<double>>? RenderedMarkPositions { get; set; }

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Normalised (0–1) positions of the matches for the minimap tick strip.</summary>
    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set { _miniMapMarks = value; OnPropertyChanged(); }
    }

    /// <summary>Asks the source box to select and reveal a span of the raw markdown.</summary>
    public event Action<int, int>? SourceSelectionRequested;

    private bool _renderedActive;                       // the live search is painting the rendered surface
    private (int Offset, int Length)[] _sourceMatches = [];
    private int _currentSource = -1;

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName) ? "the open markdown document" : $"the open markdown document '{FileName}'";

    /// <summary>Same curve as the other document viewers: a term or two is almost certainly a search.</summary>
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

        // Not on screen: the AI is reading the document. Search the source — that is what the file IS, and
        // what read_document returns — and report the matches without touching the view.
        if (!display)
            return SourceOutcome(matcher, ct);

        return await _shell.RunOnUiAsync(() => Task.FromResult(ShowInActiveSurface(matcher, request, ct)));
    }

    /// <summary>Searches the raw source for the agent — line-numbered hits, no UI.</summary>
    private SearchOutcome SourceOutcome(TextSearchMatcher matcher, CancellationToken ct)
    {
        var hits  = new List<SearchHit>();
        var total = 0;
        foreach (var line in matcher.ScanLines(Markdown ?? string.Empty))
        {
            ct.ThrowIfCancellationRequested();
            total++;
            if (hits.Count < SearchHitCap)
                hits.Add(new SearchHit(
                    line.Index.ToString(CultureInfo.InvariantCulture), $"line {line.Index + 1}", line.Text.Trim()));
        }
        return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);
    }

    // Highlights the matches in whichever surface is showing. Runs on the UI thread (the source box and the
    // rendered FlowDocument are both view state).
    private SearchOutcome ShowInActiveSurface(TextSearchMatcher matcher, SearchRequest request, CancellationToken ct)
    {
        ClearHighlights();
        CurrentSearchTerm = SearchSyntax.Format(request);
        IsSearchActive    = true;   // true at zero too: "no matches for X" is a result worth showing

        if (SourceOnly) return ShowInSource(matcher, ct);
        return ShowInRendered(matcher);
    }

    private SearchOutcome ShowInSource(TextSearchMatcher matcher, CancellationToken ct)
    {
        var source  = Markdown ?? string.Empty;
        var matches = new List<(int Offset, int Length)>();
        var hits    = new List<SearchHit>();

        foreach (var line in matcher.ScanLines(source))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (index, length) in matcher.Occurrences(line.Text))
            {
                matches.Add((line.Offset + index, length));
                if (hits.Count < SearchHitCap)
                    hits.Add(new SearchHit(
                        matches.Count.ToString(CultureInfo.InvariantCulture),
                        $"line {line.Index + 1}", line.Text.Trim()));
            }
        }

        _renderedActive  = false;
        _sourceMatches   = [.. matches];
        _currentSource   = matches.Count > 0 ? 0 : -1;
        SearchMatchCount = matches.Count;
        MiniMapMarks     = SourceMarks(matches);
        if (matches.Count > 0) Reveal(matches[0]);
        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, matches.Count);
    }

    private SearchOutcome ShowInRendered(TextSearchMatcher matcher)
    {
        _renderedActive = true;
        var matches = FindInRendered?.Invoke(matcher) ?? [];
        SearchMatchCount = matches.Count;
        MiniMapMarks     = RenderedMarkPositions?.Invoke() ?? [];

        var hits = matches.Take(SearchHitCap)
            .Select(m => new SearchHit(m.Ordinal.ToString(CultureInfo.InvariantCulture), $"match {m.Ordinal + 1}", m.Preview))
            .ToList();
        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, matches.Count);
    }

    // Source-mode minimap ticks: each match's source line as a fraction of the document's line count.
    private IReadOnlyList<double> SourceMarks(IReadOnlyList<(int Offset, int Length)> matches)
    {
        var source = Markdown ?? string.Empty;
        var total  = Math.Max(1, source.Count(c => c == '\n') + 1);
        var marks  = new List<double>(matches.Count);
        foreach (var (offset, _) in matches)
        {
            var line = 0;
            for (var i = 0; i < offset && i < source.Length; i++) if (source[i] == '\n') line++;
            marks.Add((double)line / total);
        }
        return marks;
    }

    // ShowResultsAsync is left at the interface default: a single-document page highlights in place rather
    // than rendering an arbitrary subset, so it has nothing honest to narrow to.

    // ── Navigation / dismissal (the toolbar chip) ─────────────────────────────

    private void Reveal((int Offset, int Length) match) =>
        SourceSelectionRequested?.Invoke(match.Offset, match.Length);

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_renderedActive) { StepRendered?.Invoke(delta); return; }
        if (_sourceMatches.Length == 0) return;
        _currentSource = (_currentSource + delta + _sourceMatches.Length) % _sourceMatches.Length;
        Reveal(_sourceMatches[_currentSource]);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        ClearHighlights();
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        IsSearchActive    = false;
    }

    private void ClearHighlights()
    {
        if (_renderedActive) ClearRendered?.Invoke();
        _renderedActive = false;
        _sourceMatches  = [];
        _currentSource  = -1;
        MiniMapMarks    = [];
    }
}
