using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Features.Rtf.ViewModels;

/// <summary>
/// The rich-text page as a searchable page. It searches the words the document draws — never its RTF, so a search for
/// "par" does not match every paragraph break. A "?" search lights the matches up on the page; one the assistant runs
/// for itself reads the same words, paragraph by paragraph, and leaves the page as it was.
/// </summary>
public sealed partial class RtfViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    // ── Visible search state (the toolbar chip binds these) ───────────────────

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── View collaboration ────────────────────────────────────────────────────
    // The surface highlights its own drawn words; the view hands these over. Absent in a headless test, where only the
    // agent's path is exercised.

    /// <summary>Runs the matcher against the drawn document, highlighting it, and returns the matches.</summary>
    internal Func<TextSearchMatcher, IReadOnlyList<RenderedMatch>>? FindInDocument { get; set; }
    internal Action<int>? StepDocument { get; set; }
    internal Action? ClearDocument { get; set; }

    /// <summary>Where on the page, top to bottom from 0 to 1, each match the surface lit up is.</summary>
    internal Func<IReadOnlyList<double>>? DocumentMarkPositions { get; set; }

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Normalised (0–1) positions of the matches for the minimap tick strip.</summary>
    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set
        {
            _miniMapMarks = value;
            OnPropertyChanged();
        }
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => $"the open rich-text document '{FileName}'";

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

        if (!display) return Searched(matcher, ct);

        return await _shell.RunOnUiAsync(() => Task.FromResult(Shown(matcher, request)));
    }

    // ShowResultsAsync is left at the interface default: a document highlights in place rather than rendering an arbitrary
    // subset of itself, so it has nothing honest to narrow to.

    /// <summary>The agent's search: each paragraph holding a match, by its number — the numbering rtf_read_text reads by.</summary>
    private SearchOutcome Searched(TextSearchMatcher matcher, CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        var total = 0;

        foreach (var line in matcher.ScanLines(Words))
        {
            ct.ThrowIfCancellationRequested();
            total++;

            if (hits.Count < SearchHitCap)
                hits.Add(new SearchHit(
                    line.Index.ToString(CultureInfo.InvariantCulture), $"paragraph {line.Index + 1}", line.Text.Trim()));
        }

        return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);
    }

    /// <summary>The reader's search: the matches lit up on the page. Runs on the UI thread, where the surface is.</summary>
    private SearchOutcome Shown(TextSearchMatcher matcher, SearchRequest request)
    {
        ClearHighlights();
        CurrentSearchTerm = SearchSyntax.Format(request);
        IsSearchActive = true;   // true at zero too: "no matches for X" is a result worth showing

        var matches = FindInDocument?.Invoke(matcher) ?? [];
        SearchMatchCount = matches.Count;
        MiniMapMarks = DocumentMarkPositions?.Invoke() ?? [];

        var hits = matches.Take(SearchHitCap)
            .Select(m => new SearchHit(m.Ordinal.ToString(CultureInfo.InvariantCulture), $"match {m.Ordinal + 1}", m.Preview))
            .ToList();

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, matches.Count);
    }

    // ── Navigation / dismissal (the toolbar chip) ─────────────────────────────

    [RelayCommand]
    private void FindNextMatch() => StepDocument?.Invoke(+1);

    [RelayCommand]
    private void FindPreviousMatch() => StepDocument?.Invoke(-1);

    [RelayCommand]
    private void ClearSearch()
    {
        ClearHighlights();
        SearchMatchCount = 0;
        CurrentSearchTerm = string.Empty;
        IsSearchActive = false;
    }

    private void ClearHighlights()
    {
        ClearDocument?.Invoke();
        MiniMapMarks = [];
    }
}
