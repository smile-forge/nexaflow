using System.Globalization;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Nexaflow.Features.Common.Search;
using Nexaflow.Localization;
using Nexaflow.Markdown.Editing;
using Nexaflow.Search;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// Finding in the file, and replacing: the find bar, the shell's <c>?</c> search and the assistant's all run one query
/// (<see cref="SearchRequest"/>) over every line of the whole file — off the page, as it was written when the search began — and the matches
/// are marked on the page by the lines being drawn (<see cref="MatchMarks"/>), so a file of any length is marked at the cost of a screen.
/// </summary>
public sealed partial class TextViewModel
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    // ── Search state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private bool   _isSearchRunning;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    // ── Find & Replace bar ──────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isFindBarOpen;
    [ObservableProperty] private bool   _isReplaceVisible;
    [ObservableProperty] private string _findText    = string.Empty;
    [ObservableProperty] private string _replaceText = string.Empty;
    [ObservableProperty] private bool   _matchCase;

    /// <summary>Treat <c>*</c>/<c>?</c> in the find box as wildcards (word-bounded), not literal characters.
    /// On by default and mutually exclusive with <see cref="UseRegex"/> — a query is wildcards or regex,
    /// never both. Turned on when a literal "?" search is injected, so the box reproduces it.</summary>
    [ObservableProperty] private bool   _useWildcards = true;
    [ObservableProperty] private bool   _useRegex;

    private bool _suppressFindRun;   // guards the live re-run when the engine sets FindText/UseRegex back
    private CancellationTokenSource? _findDebounceCts;

    /// <summary>Raised when the bar opens so the view can focus the find box.</summary>
    public event Action? FindBarFocusRequested;

    /// <summary>What the search marks on the page: worked out over the lines being drawn, the match looked at outlined.</summary>
    private readonly MatchMarks _matchMarks = new();

    private IReadOnlyList<double> _miniMapMarks = [];

    /// <summary>Where the matches stand down the whole file (0–1), for the minimap's ticks. Watched through <c>PropertyChanged</c>.</summary>
    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set { _miniMapMarks = value; OnPropertyChanged(); }
    }

    // ── Search cancellation ───────────────────────────────────────────────────

    private CancellationTokenSource? _searchCts;
    private Func<CancellationToken, Task>? _lastSearch;

    /// <summary>The lines of the whole file (from nought) that hold at least one match, in order.</summary>
    private long[] _matchingLineNumbers = [];

    // The active query — the ONE authority for what matches, so the text viewer honours whole-word, wildcards and quoting exactly like
    // every other ISearchable page. Scanning, marking, navigation and replacing all read this.
    private SearchRequest? _activeRequest;

    // Kept only for regex-mode Replace's $1 expansion; null for a literal/wildcard query.
    private Regex? _activeRegex;
    private int _currentMatchIndex = -1;

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>A plain (simple) find — the raw text as one whole-word literal, so <c>*</c>/<c>?</c> are
    /// ordinary characters. Reflects itself into the bar and runs. Kept for the AI find tool and tests.</summary>
    public Task SearchConventionalAsync(string query, CancellationToken externalCt = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;
        var request = SimpleLiteralRequest(query);
        SurfaceSearchInBar(query, wildcards: false, regex: false);
        return RunSearchAsync(request, externalCt);
    }

    /// <summary>A regex find. Reflects itself into the bar and runs.</summary>
    public Task SearchRegexAsync(string pattern, CancellationToken externalCt = default)
    {
        var request = new SearchRequest(pattern, IsRegex: true, MatchCase: MatchCase);
        SurfaceSearchInBar(pattern, wildcards: false, regex: true);
        return RunSearchAsync(request, externalCt);
    }

    /// <summary>The whole find-box text as one literal term: whole-word (a phrase stays a phrase), Match-case
    /// applied, and <c>*</c>/<c>?</c> taken literally. <see cref="SearchTerm.Exact"/> is what suppresses the
    /// wildcards; the Display keeps the term rendering as the user typed it.</summary>
    private SearchRequest SimpleLiteralRequest(string text) =>
        new(text, MatchCase: MatchCase)
        {
            Terms = [new SearchTerm(SearchTermKind.Text, [text], MatchCase, Exact: true, Display: text)],
        };

    /// <summary>Builds the request the find box represents right now, honouring its three toggles: regex,
    /// wildcards (the shared syntax, so <c>needle*</c> and <c>"a phrase"</c> work), or a plain literal.</summary>
    private SearchRequest BuildFindRequest(string text)
    {
        if (UseRegex) return new SearchRequest(text, IsRegex: true, MatchCase: MatchCase);
        if (!UseWildcards) return SimpleLiteralRequest(text);

        var parsed = SearchSyntax.ParseRequest(text);
        return MatchCase
            ? parsed with { MatchCase = true, Terms = parsed.Terms.Select(t => t with { MatchCase = true }).ToList() }
            : parsed;
    }

    /// <summary>
    /// Runs a parsed query over the whole file and drives the on-screen search: the match count, the minimap ticks, the marks on the page and
    /// the caret to the first match. The find bar's text and toggles are set by the caller (a "?" injection or a driver), not here, so live
    /// typing isn't fought.
    /// </summary>
    private async Task RunSearchAsync(SearchRequest request, CancellationToken externalCt = default)
    {
        _lastSearch = ct => RunSearchAsync(request, ct);
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_searchCts.Token, externalCt);
        var ct = linked.Token;

        _activeRequest = request;
        // Only a single-regex query keeps a compiled Regex, for Replace's $1 expansion.
        _activeRegex = request.Terms is [{ Kind: SearchTermKind.Regex }]
            && request.TryCompileRegex(out var rx, out _) ? rx : null;

        IsFindBarOpen     = true;              // opens even for a no-match search
        CurrentSearchTerm = SearchSyntax.Format(request);

        IsSearchRunning = true;
        try
        {
            var (lines, count) = await ScanAsync(request, ct);
            _matchingLineNumbers = lines;

            MiniMapMarks       = MarksFor(lines);
            SearchMatchCount   = count;
            IsSearchActive     = count > 0;
            _currentMatchIndex = count > 0 ? 0 : -1;
            _matchMarks.Show(count > 0 ? request.Occurrences : null);
            if (count > 0) NavigateToMatchLine(lines[0]);
        }
        catch (OperationCanceledException) { }
        finally { IsSearchRunning = false; }
    }

    /// <summary>
    /// Every line of the whole file the request matches, and how many — read a line at a time off the page, from the file as it stood when
    /// the search began — with the first of them as hits, where <paramref name="collect"/> is asked for.
    /// </summary>
    private async Task<(long[] lines, int count)> ScanAsync(SearchRequest request, CancellationToken ct, List<SearchHit>? collect = null, int collectCap = 0)
    {
        var lines = FileLines();
        var matching = new List<long>();

        await Task.Run(() => lines((line, _, text) =>
        {
            if (!request.Matches(text)) return;

            matching.Add(line);
            if (collect is not null && collect.Count < collectCap) collect.Add(HitFor(line, text));
        }, ct), ct);

        return (matching.ToArray(), matching.Count);
    }

    private static SearchHit HitFor(long line, string text) =>
        new(line.ToString(CultureInfo.InvariantCulture), Str.Format("Text.Viewer.Search.LineFormat", line + 1), text);

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName) ? "the open text file" : $"the open text file '{FileName}'";

    /// <summary>A term or two is almost certainly a search; four is borderline. Past that <see cref="SearchScoring.LooksLikeProse"/> has
    /// already bowed out.</summary>
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
        // Validate against what a single-body page can run (filename filters, bad patterns) with the same
        // words every other ISearchable page uses.
        if (!TextSearchMatcher.TryCreate(request, out _, out var error))
            return SearchOutcome.Unsupported(error);

        if (!display)
        {
            // Agent-side read: scan without touching the marks, the minimap or the find bar.
            var hits = new List<SearchHit>();
            var (_, found) = await ScanAsync(request, ct, hits, SearchHitCap);
            return found == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, found);
        }

        // Inject the query into the find bar so it lights up exactly what the bar would, and turn on the
        // toggle that reproduces it — regex for a regex, wildcards for anything else (so the box stays
        // aligned when re-run).
        var single = request.Terms.Count == 1 ? request.Terms[0] : null;
        if (single is { Kind: SearchTermKind.Regex })
            SurfaceSearchInBar(single.Value, wildcards: false, regex: true);
        else
            SurfaceSearchInBar(SearchSyntax.Format(request), wildcards: true, regex: false);

        await RunSearchAsync(request, ct);

        return SearchMatchCount == 0
            ? SearchOutcome.None()
            : SearchOutcome.Found(VisibleHits(), SearchMatchCount);
    }

    /// <summary>
    /// Narrows the match set to the agent's chosen lines — the minimap, match count and next/previous navigation all follow it. The marks on
    /// the page still show every occurrence, since they are drawn from the query rather than the match list.
    /// </summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var lines = hits.Select(h => long.TryParse(h.Id, out var l) ? l : -1)
                        .Where(l => l >= 0)
                        .Distinct()
                        .OrderBy(l => l)
                        .ToArray();
        if (lines.Length == 0) return Task.FromResult(false);

        _matchingLineNumbers = lines;
        MiniMapMarks         = MarksFor(lines);
        SearchMatchCount     = lines.Length;
        IsSearchActive       = true;
        _currentMatchIndex   = 0;
        NavigateToMatchLine(lines[0]);
        return Task.FromResult(true);
    }

    /// <summary>The current match lines as hits, each previewed from what is written on it.</summary>
    private IReadOnlyList<SearchHit> VisibleHits()
    {
        var hits = new List<SearchHit>(Math.Min(_matchingLineNumbers.Length, SearchHitCap));
        foreach (var line in _matchingLineNumbers)
        {
            if (hits.Count >= SearchHitCap) break;
            hits.Add(HitFor(line, FileLineText(line)));
        }
        return hits;
    }

    /// <summary>What is written on line <paramref name="line"/> of the whole file, without its line end.</summary>
    private string FileLineText(long line)
    {
        if (FileOffsetOfLine(line) is not { } start) return string.Empty;

        var end = FileOffsetOfLine(line + 1) ?? FileLength;
        return FileText(start, (int)Math.Min(int.MaxValue, end - start)).TrimEnd('\r', '\n');
    }

    private IReadOnlyList<double> MarksFor(long[] lines)
    {
        long total = Math.Max(1, LineCount);
        var marks  = new List<double>(lines.Length);
        foreach (var l in lines) marks.Add((double)l / total);
        return marks;
    }

    private Task ReRunSearchAsync() => _lastSearch?.Invoke(CancellationToken.None) ?? Task.CompletedTask;

    [RelayCommand]
    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _matchMarks.Show(null);
        MiniMapMarks         = [];
        SearchMatchCount     = 0;
        IsSearchActive       = false;
        CurrentSearchTerm    = string.Empty;
        _lastSearch          = null;
        _currentMatchIndex   = -1;
        _matchingLineNumbers = [];
        _activeRequest       = null;
        _activeRegex         = null;
    }

    // Next/Previous find the match after/before the CARET — the first match whose file line is past the caret's, so a plain click-then-F3
    // lands on the next occurrence like every editor. The match list is file-line-sorted, so a binary search picks the neighbour, and going
    // to it brings the window there where the file is too long for one document.
    [RelayCommand]
    private void FindNext() => StepFromCaret(forward: true);

    [RelayCommand]
    private void FindPrevious() => StepFromCaret(forward: false);

    private void StepFromCaret(bool forward)
    {
        var n = _matchingLineNumbers.Length;
        if (n == 0) return;

        var caretLine = FileLineOf(Editor.Caret);
        int idx;
        if (forward)
        {
            idx = LowerBound(_matchingLineNumbers, caretLine + 1);   // first match strictly after the caret
            if (idx >= n) idx = 0;                                   // past the last → wrap to the first
        }
        else
        {
            idx = LowerBound(_matchingLineNumbers, caretLine) - 1;   // last match strictly before the caret
            if (idx < 0) idx = n - 1;                                // before the first → wrap to the last
        }

        _currentMatchIndex = idx;
        NavigateToMatchLine(_matchingLineNumbers[idx]);
    }

    // First index whose value is >= target (the match list is ascending file lines).
    private static int LowerBound(long[] arr, long target)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (arr[mid] < target) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>
    /// Goes to the first match on line <paramref name="line"/> of the whole file: the caret at its start, the match outlined as the one looked
    /// at, and the line brought onto the page.
    /// </summary>
    private void NavigateToMatchLine(long line)
    {
        if (MatchOnLine(line) is not { } match)
        {
            GoToFileLine(line, ShownPlace.Nearest);
            return;
        }

        Editor.MoveCaretTo(match.Start);
        _matchMarks.LookAt(match);
        Editor.ScrollToLine(Document.Current.LineOf(match.Start), ShownPlace.Nearest);
    }

    /// <summary>The first match on line <paramref name="line"/> of the whole file, where it stands in the document — the window brought there first.</summary>
    private (int Start, int Length)? MatchOnLine(long line)
    {
        if (_activeRequest is null || FileOffsetOfLine(line) is not { } at) return null;

        var document = Document.Current;
        var (from, to) = document.LineAt(DocumentOffset(at));
        document = Document.Current;

        foreach (var (index, length) in _activeRequest.Occurrences(document.Slice(from, to - from).TrimEnd('\r', '\n')))
            return (from + index, length);

        return null;
    }

    // ── Find & Replace bar ────────────────────────────────────────────────────

    // Live re-run when the user edits the find text or flips a toggle (guarded so the engine writing FindText/UseRegex/UseWildcards back
    // doesn't recurse). Wildcards and regex are mutually exclusive: turning one on turns the other off. That sibling-set is itself
    // suppressed, so it neither recurses nor schedules a second search.
    partial void OnFindTextChanged(string value)     { if (!_suppressFindRun) DebouncedRunFind(); }
    partial void OnMatchCaseChanged(bool value)      { if (!_suppressFindRun) DebouncedRunFind(); }

    partial void OnUseWildcardsChanged(bool value)
    {
        if (_suppressFindRun) return;                 // programmatic set (injection sets both correctly)
        if (value && UseRegex) SetSuppressed(() => UseRegex = false);
        DebouncedRunFind();
    }

    partial void OnUseRegexChanged(bool value)
    {
        if (_suppressFindRun) return;
        if (value && UseWildcards) SetSuppressed(() => UseWildcards = false);
        DebouncedRunFind();
    }

    private void SetSuppressed(Action set)
    {
        _suppressFindRun = true;
        try { set(); }
        finally { _suppressFindRun = false; }
    }

    private async void DebouncedRunFind()
    {
        _findDebounceCts?.Cancel();
        var cts = _findDebounceCts = new CancellationTokenSource();
        try { await Task.Delay(250, cts.Token); }
        catch (OperationCanceledException) { return; }
        if (_disposed || cts.IsCancellationRequested) return;
        try { await RunFindAsync(); } catch { }
    }

    // The user typed in the box (or flipped a toggle): build the request the box represents and run it.
    // No bar reflection here — the box's own text/toggles are the source, so we mustn't fight the user.
    private Task RunFindAsync()
    {
        var q = FindText;
        if (string.IsNullOrWhiteSpace(q)) { CancelSearch(); return Task.CompletedTask; }
        return RunSearchAsync(BuildFindRequest(q));
    }

    // Reflects a driver-run search back into the bar so it lights up the same UI, and sets the toggle that
    // reproduces it (regex OR wildcards). Suppressed so setting these doesn't trigger a redundant re-run.
    private void SurfaceSearchInBar(string term, bool wildcards, bool regex)
    {
        _suppressFindRun = true;
        if (FindText != term) FindText = term;
        UseWildcards = wildcards;
        UseRegex     = regex;
        _suppressFindRun = false;
        IsFindBarOpen = true;
    }

    [RelayCommand] private void OpenFind()    => OpenBar(replace: false);
    [RelayCommand] private void OpenReplace() => OpenBar(replace: true);

    /// <summary>Toolbar Find button: opens the bar, or closes it if already open.</summary>
    [RelayCommand]
    private void ToggleFind()
    {
        if (IsFindBarOpen) CloseFindBar();
        else OpenBar(replace: false);
    }

    private void OpenBar(bool replace)
    {
        IsReplaceVisible = replace;
        var picked = Editor.HasSelection ? Editor.SelectedText() : null;
        if (string.IsNullOrEmpty(FindText) && !string.IsNullOrEmpty(picked) && !picked.Contains('\n'))
        {
            _suppressFindRun = true; FindText = picked; _suppressFindRun = false;
        }
        IsFindBarOpen = true;
        FindBarFocusRequested?.Invoke();
        if (!string.IsNullOrWhiteSpace(FindText)) _ = RunFindAsync();
    }

    [RelayCommand]
    private void CloseFindBar()
    {
        IsFindBarOpen    = false;
        IsReplaceVisible = false;
        CancelSearch();
    }

    /// <summary>
    /// Replaces every match of the find bar's query — what it marks, whole words, wildcards and quotes as it reads them — across the whole file,
    /// a regex's groups expanded into the replacement.
    /// </summary>
    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        if (!await EnsureEditingAsync()) return;

        var request = _activeRequest ?? BuildFindRequest(FindText);
        var (regex, replacement) = (_activeRegex, ReplaceText);

        var n = ReplaceAcross(0, Math.Max(0, FileLineCount - 1), text =>
            request.Occurrences(text).Select(match => (match.Index, match.Length,
                regex is null ? replacement : regex.Match(text, match.Index, match.Length).Result(replacement))));

        if (IsSearchActive) await ReRunSearchAsync();
        _shell.ShowNotification(n == 0
            ? Str.Get("Text.Viewer.Find.NothingToReplace")
            : Str.Format("Text.Viewer.Find.ReplacedFormat", n));
    }

    [RelayCommand]
    private async Task ReplaceCurrent()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        if (!IsSearchActive) await RunFindAsync();
        if (!IsSearchActive || _matchingLineNumbers.Length == 0) return;
        if (!await EnsureEditingAsync()) return;

        var line = _matchingLineNumbers[Math.Clamp(_currentMatchIndex < 0 ? 0 : _currentMatchIndex, 0, _matchingLineNumbers.Length - 1)];

        if (MatchOnLine(line) is { } match)
        {
            var replacement = ReplaceText;
            if (UseRegex && _activeRegex is not null) // expand $1… against the matched text
                replacement = _activeRegex.Replace(Document.Current.Slice(match.Start, match.Length), replacement, 1);

            Document.Write(ContentChange.Write(match.Start, match.Length, replacement));
        }

        await ReRunSearchAsync();
        FindNext();
    }

    /// <summary>
    /// Writes what <paramref name="replacements"/> says for each line from <paramref name="fromLine"/> to <paramref name="toLine"/> of the whole
    /// file, counting from nought — one step that can be taken back for the file, or for each window onto a file too long for one — and says
    /// how many it replaced.
    /// </summary>
    private int ReplaceAcross(long fromLine, long toLine, Func<string, IEnumerable<(int Index, int Length, string With)>> replacements)
    {
        if (fromLine > toLine || FileOffsetOfLine(fromLine) is not { } at) return 0;

        var stop = FileOffsetOfLine(toLine + 1) ?? FileLength;
        var count = 0;

        while (at < stop)
        {
            // Over a file too long for one document, a window at a time from where the last left off; the window stands on whole lines.
            if (_window is { } window) window.MoveTo(at + (window.Size / 2));

            var (start, document) = (_window?.Start ?? 0, Document.Current);
            var (from, to) = ((int)(at - start), (int)Math.Min(stop - start, document.Length));
            var writes = new List<ContentWrite>();
            var grown = 0L;

            for (var line = document.LineOf(from); line < document.LineCount; line++)
            {
                var (lineStart, lineEnd) = document.LineAt(document.OffsetOf(line));
                if (lineStart >= to) break;
                if (lineStart < from) continue;

                foreach (var (index, length, with) in replacements(document.Slice(lineStart, lineEnd - lineStart).TrimEnd('\r', '\n')))
                {
                    writes.Add(new ContentWrite(lineStart + index, length, with));
                    grown += with.Length - length;
                    count++;
                }
            }

            if (writes.Count > 0) Document.Write(new ContentChange(writes, writes[0].Start));

            at = start + to + grown;
            stop += grown;
            if (_window is null) break;
        }

        return count;
    }
}
