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
/// copy of the scan. The search is the shared one over the file's lines (<see cref="FileFind"/>): what it finds is marked by the editor's own
/// marks (<see cref="ContentEditor.Found"/>), the match being looked at outlined, and it runs again once writing pauses.
/// </summary>
public partial class FileTextEditorViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    // ── Visible search state (the status-bar chip binds these) ────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>The file as the search reads it: the whole of it, a line at a time.</summary>
    private readonly ShownFile _searched;

    /// <summary>The search shown, and what it found.</summary>
    private readonly FileFind _find;

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Normalised (0–1) vertical positions of the matches, for the minimap tick strip. Watched via
    /// <c>PropertyChanged</c>.</summary>
    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set { _miniMapMarks = value; OnPropertyChanged(); }
    }

    private IReadOnlyList<double> MarksFor(IReadOnlyList<long> lines)
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

        // Agent-side read: the matches are data, so nothing on screen may move.
        var query = new LineQuery(matcher.Matches, matcher.Occurrences);
        var found = display ? await _find.FindAsync(query, SearchHitCap, ct) : await _find.ReadAsync(query, SearchHitCap, ct);
        if (display) CurrentSearchTerm = SearchSyntax.Format(request);

        return found.Lines.Length == 0 ? SearchOutcome.None() : SearchOutcome.Found([.. found.First.Select(HitFor)], found.Lines.Length);
    }

    /// <summary>
    /// Narrows the match set to the agent's chosen lines — the count, the marks and next/previous
    /// navigation all follow it, so "show me only the ones about auth" really does filter the view.
    /// </summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        Task.FromResult(_find.Narrow(hits.Select(h => long.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1)));

    /// <summary>What the search found, or which of it is looked at, changed — run, stepped, run again as the file was written, narrowed, let go.</summary>
    private void OnFindChanged(object? sender, EventArgs args)
    {
        SearchMatchCount = _find.Count;
        IsSearchActive   = _find.IsShowing;   // true even at zero matches: "no matches for X" is a result to show

        // Stepping through what was found moves no tick: they are worked out again only for other lines found.
        if (ReferenceEquals(_find.Lines, _ticked)) return;
        _ticked      = _find.Lines;
        MiniMapMarks = MarksFor(_ticked);
    }

    /// <summary>The lines the minimap's ticks were worked out for.</summary>
    private IReadOnlyList<long>? _ticked;

    private static SearchHit HitFor((long Line, string Text) hit) =>
        new(hit.Line.ToString(CultureInfo.InvariantCulture), $"line {hit.Line + 1}", hit.Text);

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
