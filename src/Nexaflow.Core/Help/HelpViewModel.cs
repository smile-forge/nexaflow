using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Help;

/// <summary>
/// The help pane: one help page, rendered read-only, and a search that looks through it first and then through every
/// other help page. Opened beside a page by the shell's Help button (<see cref="HelpPaneController"/>), which keeps it
/// on whatever page the reader is using until they move within help themselves (<see cref="IsPinned"/>).
/// <para>
/// A searchable page, so <c>?term</c> in the AI bar searches help while it is focused; and its context is the help
/// text itself, so asked "how do I…" the assistant answers from the page in front of the reader.
/// </para>
/// </summary>
internal sealed partial class HelpViewModel : ObservableObject, IPageViewModel, ISearchable
{
    private const int SearchHitCap = 200;
    private const int ContextCap   = 16_000;
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(250);

    private readonly HelpLibrary _library;
    private readonly IShellServices _shell;
    private readonly Page _page;
    private HelpDocument _doc;
    private TextSearchMatcher? _matcher;
    private string? _searchedFor;          // the text the live search ran for
    private string? _lastAgentQuery;       // what search_page last looked for, for show_search_results
    private int _pendingStep;              // a match to step to once the page just shown has laid out
    private CancellationTokenSource? _pause;
    private bool _settingText;

    public HelpViewModel(HelpLibrary library, IShellServices shell, Page page)
    {
        _library = library;
        _shell   = shell;
        _page    = page;
        _doc     = null!;   // Show sets it below

        var pageParams = page.PageParams ?? [];
        Show(pageParams.GetValueOrDefault("topic"), fromUser: false);
        if (pageParams.GetValueOrDefault("query") is { Length: > 0 } query) SetQuery(query);
    }

    // ── What the view shows ───────────────────────────────────────────────

    [ObservableProperty] private string _markdown = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _searchError;
    [ObservableProperty] private bool _hasOtherResults;

    // The search chip binds these by convention (SearchStatusChip).
    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = "";

    public bool HasSearchMatches => SearchMatchCount > 0;
    public bool HasSearchText => SearchText.Length > 0;

    /// <summary>The other help pages the live search matched, most matches first.</summary>
    public ObservableCollection<HelpSearchResult> OtherResults { get; } = [];

    /// <summary>The help page shown: the page kind it explains, or <see cref="HelpLibrary.IndexTopic"/>.</summary>
    public string Topic => _doc.Topic.Topic;

    /// <summary>What the pane was last pointed at — a page kind, possibly one with no help (the index shows then).</summary>
    public string? RequestedTopic { get; private set; }

    /// <summary>True once the reader has moved within help themselves — a link, a result, the index. The pane then stays
    /// where they took it rather than following the page they are using; the Help button unpins it.</summary>
    public bool IsPinned { get; private set; }

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        if (!_settingText) _ = SearchAfterPauseAsync(value);
    }

    // ── View collaboration ────────────────────────────────────────────────
    // The live FlowDocument is the view's, so it highlights and steps; set by HelpView. Absent in a headless test,
    // where only the agent path and the other-pages list are exercised.

    public Func<TextSearchMatcher, IReadOnlyList<RenderedMatch>>? FindInRendered { get; set; }
    public Action<int>? StepRendered { get; set; }
    public Action? ClearRendered { get; set; }

    /// <summary>Raised when a new page is shown, so the view can re-apply the live search once it has laid out.</summary>
    public event Action? DocumentShown;

    // ── Navigation ────────────────────────────────────────────────────────

    /// <summary>Shows the help for <paramref name="topic"/> (a page kind; null for the index), then searches it for
    /// <paramref name="query"/> if one is given. <paramref name="fromUser"/> marks a move the reader made inside help,
    /// which pins the pane; a re-point from the shell unpins it.</summary>
    public void Navigate(string? topic, string? query, bool fromUser)
    {
        // The shell re-sends a tab's own params when its tab is clicked again: nothing has changed.
        if (!fromUser && query is null && string.Equals(topic ?? "", RequestedTopic ?? "", StringComparison.OrdinalIgnoreCase))
            return;

        Show(topic, fromUser);
        if (query is { Length: > 0 }) SetQuery(query);
        else if (_matcher is not null) _ = RefreshOtherResultsAsync(_matcher);   // same search, a different page to leave out
        DocumentShown?.Invoke();
    }

    private void Show(string? topic, bool fromUser)
    {
        var requested = string.Equals(topic, HelpLibrary.IndexTopic, StringComparison.OrdinalIgnoreCase) ? null : topic;
        _doc           = _library.Load(requested);
        RequestedTopic = requested;
        IsPinned       = fromUser;
        Title          = _doc.Topic.Title;
        Markdown       = _doc.Markdown;
        OnPropertyChanged(nameof(Topic));

        _page.Title      = Str.Format("Help.Tab.TitleFormat", Title);
        _page.PageParams = string.IsNullOrWhiteSpace(requested) ? [] : new() { ["topic"] = requested };
        _page.Breadcrumbs.Clear();
        _page.Breadcrumbs.Add(new BreadcrumbSegment { Label = Str.Get("Help.Tab.Title"), Navigate = () => Navigate(null, null, fromUser: true) });
        if (_doc.Topic.Topic != HelpLibrary.IndexTopic)
            _page.Breadcrumbs.Add(new BreadcrumbSegment { Label = Title });
    }

    /// <summary>A link clicked in the page: another help page opens here; anything else goes to the browser.</summary>
    public bool FollowLink(string url)
    {
        if (HelpLibrary.TopicFromLink(url) is not { } topic) return false;
        Navigate(topic, null, fromUser: true);
        return true;
    }

    /// <summary>A picture the shown page links to, out of the language pack.</summary>
    public ImageSource? ResolveImage(string src) => _library.ResolveImage(_doc.Topic, src);

    [RelayCommand]
    private void ShowIndex() => Navigate(null, null, fromUser: true);

    [RelayCommand]
    private void OpenResult(HelpSearchResult? result)
    {
        if (result is not null) Navigate(result.Topic.Topic, null, fromUser: true);
    }

    // ── Search ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void FindNextMatch() => StepRendered?.Invoke(+1);

    [RelayCommand]
    private void FindPreviousMatch() => StepRendered?.Invoke(-1);

    [RelayCommand]
    private void ClearSearch()
    {
        _pause?.Cancel();
        SetTextQuietly("");
        ResetSearch();
    }

    /// <summary>Enter in the search box: search now if the text has changed since the last search, else step to the
    /// next (<paramref name="back"/>: previous) match.</summary>
    public void Step(bool back)
    {
        if (!string.Equals(SearchText, _searchedFor, StringComparison.Ordinal))
        {
            _pause?.Cancel();
            RunSearch(SearchText);
            return;
        }
        StepRendered?.Invoke(back ? -1 : +1);
    }

    /// <summary>Re-runs the live search on the page just shown, now that it has laid out, and steps to the match a
    /// result asked for.</summary>
    public void ReapplySearch()
    {
        if (_matcher is null) return;
        SearchMatchCount = FindInRendered?.Invoke(_matcher).Count ?? 0;
        if (_pendingStep > 0 && SearchMatchCount > 1)
            StepRendered?.Invoke(Math.Min(_pendingStep, SearchMatchCount - 1));
        _pendingStep = 0;
    }

    private void SetQuery(string query)
    {
        SetTextQuietly(query);
        RunSearch(query);
    }

    private void SetTextQuietly(string text)
    {
        _settingText = true;
        try { SearchText = text; }
        finally { _settingText = false; }
    }

    private async Task SearchAfterPauseAsync(string text)
    {
        _pause?.Cancel();
        var pause = _pause = new CancellationTokenSource();
        try { await Task.Delay(TypingPause, pause.Token); }
        catch (OperationCanceledException) { return; }
        await _shell.RunOnUiAsync(() => RunSearch(text));
    }

    // This page first — highlighted in place — then every other help page, listed below the box.
    private void RunSearch(string text)
    {
        _searchedFor = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ResetSearch();
            return;
        }

        var request = SearchSyntax.ParseRequest(text);
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
        {
            ResetSearch();
            SearchError = error;
            return;
        }

        SearchError       = null;
        _matcher          = matcher;
        CurrentSearchTerm = SearchSyntax.Format(request);
        IsSearchActive    = true;   // true at zero too: "no matches for X" is an answer worth showing
        SearchMatchCount  = FindInRendered?.Invoke(matcher).Count ?? 0;
        _ = RefreshOtherResultsAsync(matcher);
    }

    private async Task RefreshOtherResultsAsync(TextSearchMatcher matcher)
    {
        var exclude = Topic;
        await _library.Index.EnsureBuiltAsync();
        var results = await Task.Run(() => _library.Index.Search(matcher, exclude));
        await _shell.RunOnUiAsync(() =>
        {
            if (!ReferenceEquals(matcher, _matcher)) return;   // a newer search has taken over
            OtherResults.Clear();
            foreach (var result in results) OtherResults.Add(result);
            HasOtherResults = OtherResults.Count > 0;
        });
    }

    private void ResetSearch()
    {
        _matcher     = null;
        _pendingStep = 0;
        ClearRendered?.Invoke();
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = "";
        SearchError       = null;
        OtherResults.Clear();
        HasOtherResults = false;
    }

    // ── ISearchable ───────────────────────────────────────────────────────

    public string SearchTargetDescription => "Nexaflow's help pages: the one shown first, then every other";

    /// <summary>Same curve as the document viewers: a term or two is almost certainly a search.</summary>
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

        var query = SearchSyntax.Format(request);
        _lastAgentQuery = query;
        if (display)
            await _shell.RunOnUiAsync(() => SetQuery(query));

        await _library.Index.EnsureBuiltAsync();
        ct.ThrowIfCancellationRequested();

        // Ids are "<topic>#<ordinal>": which help page, and which match on it in reading order.
        var hits = _library.Index.Hits(matcher, Topic, SearchHitCap, out var total);
        if (total == 0) return SearchOutcome.None();
        return SearchOutcome.Found(
            hits.Select(h => new SearchHit($"{h.Topic.Topic}#{h.Ordinal}", h.Topic.Title, h.Preview)).ToList(), total);
    }

    public async Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        if (hits.Count == 0 || ParseHitId(hits[0].Id) is not { } target) return false;
        await _shell.RunOnUiAsync(() =>
        {
            _pendingStep = target.Ordinal;
            if (string.Equals(target.Topic, Topic, StringComparison.OrdinalIgnoreCase))
            {
                if (_lastAgentQuery is not null && _matcher is null) SetQuery(_lastAgentQuery);
                ReapplySearch();
            }
            else
            {
                Navigate(target.Topic, _lastAgentQuery, fromUser: true);
            }
        });
        return true;
    }

    private static (string Topic, int Ordinal)? ParseHitId(string id)
    {
        var hash = id.LastIndexOf('#');
        return hash > 0 && int.TryParse(id[(hash + 1)..], out var ordinal) ? (id[..hash], ordinal) : null;
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var text = HelpPlainText.Extract(_doc.Markdown);
        if (text.Length > ContextCap) text = text[..ContextCap] + "\n…";
        var standIn = _doc.MissingTopic is { } missing ? $" (there is no help for '{missing}' yet, so this is the list of help pages)" : "";
        return $"Nexaflow help, page '{Title}'{standIn}. It reads:\n{text}";
    }

    public string? GetAiSystemPromptGuidance() =>
        "This pane is Nexaflow's built-in help, open beside the page it explains. Answer questions about what that page " +
        "can do, and how, from the help text in your context; search the other help pages (search_page) when this one " +
        "does not cover it. Quote the steps the help gives rather than inventing controls.";
}
