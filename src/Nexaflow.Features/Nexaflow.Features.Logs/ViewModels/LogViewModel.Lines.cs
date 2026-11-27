using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.Interaction;
using Nexaflow.Markdown.Editing;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Logs.ViewModels;

/// <summary>
/// What is marked on the log's lines — a wash for the levels asked for, the lines picked out, a term the reader asked to have marked, and a
/// veil over the lines a filter leaves out — each worked out from what is written on a line as it is drawn, so a log of any length costs a
/// screen of it; and the timestamps the time filter reads.
/// </summary>
public sealed partial class LogViewModel
{
    /// <summary>The most lines read from either end for the time the log spans: past that, one without timestamps is taken to have none there.</summary>
    private const int TimestampReach = 1000;

    // ── Level highlighting ────────────────────────────────────────────────────

    [ObservableProperty] private bool _highlightFatal;
    [ObservableProperty] private bool _highlightError;
    [ObservableProperty] private bool _highlightWarning;
    [ObservableProperty] private bool _highlightInfo;
    [ObservableProperty] private bool _highlightDebug;

    /// <summary>Each line of a level asked for, washed in that level's colour.</summary>
    private readonly LineMarks _levelMarks = new(ContentMarkKind.LineWash, order: -20);

    // ── Custom term ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _customHighlightTerm = string.Empty;

    /// <summary>Every occurrence of the reader's term, whatever its case — below what a search found, which reads over it.</summary>
    private readonly MatchMarks _termMarks = new("Log.Term", order: 900);

    // ── Regex filter ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _filterRegex  = string.Empty;
    [ObservableProperty] private bool   _isFilterActive;
    [ObservableProperty] private Regex? _activeFilter;

    // ── Time filter ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool      _hasTimestamps;
    [ObservableProperty] private DateTime? _filterStartDate;
    [ObservableProperty] private string    _filterStartTime = string.Empty;
    [ObservableProperty] private DateTime? _filterEndDate;
    [ObservableProperty] private string    _filterEndTime   = string.Empty;
    [ObservableProperty] private DateTime? _filterStart;
    [ObservableProperty] private DateTime? _filterEnd;

    /// <summary>A veil over each line the regex or the time filter leaves out — faded, never removed — over everything else marked on it.</summary>
    private readonly LineMarks _filterMarks = new(ContentMarkKind.Veil, order: 2000);

    // The timestamps the log spans, found when it is read, its history lands or it grows, so the context can report them.
    private DateTime? _detectedFirstTimestamp;
    private DateTime? _detectedLastTimestamp;

    // ── Line selection ────────────────────────────────────────────────────────

    /// <summary>The lines picked out, numbered as lines of the whole log from one.</summary>
    private readonly SortedSet<long> _selectedLines = [];
    [ObservableProperty] private int _selectedLineCount;

    /// <summary>The lines picked out, numbered as lines of the whole log from one.</summary>
    public IReadOnlySet<long> SelectedLineNumbers => _selectedLines;

    /// <summary>Each line picked out, washed.</summary>
    private readonly LineMarks _selectedMarks = new(ContentMarkKind.LineWash, order: -10);

    /// <summary>Hands the view its layers of marks, each showing what it shows now.</summary>
    private void Lined()
    {
        Editor.Marks.Add(_levelMarks);
        Editor.Marks.Add(_selectedMarks);
        Editor.Marks.Add(_termMarks);
        Editor.Marks.Add(_filterMarks);
        Editor.Marks.Add(_matchMarks);

        Reselected();
    }

    // ── Level highlighting ────────────────────────────────────────────────────

    partial void OnHighlightFatalChanged(bool value)   => ShowLevels();
    partial void OnHighlightErrorChanged(bool value)   => ShowLevels();
    partial void OnHighlightWarningChanged(bool value) => ShowLevels();
    partial void OnHighlightInfoChanged(bool value)    => ShowLevels();
    partial void OnHighlightDebugChanged(bool value)   => ShowLevels();

    partial void OnActiveParserChanged(ILogParser value)
    {
        ShowLevels();
        ShowFilters();
    }

    /// <summary>Washes each line of a level asked for — read by the parser the log was found to be written for — and no other.</summary>
    private void ShowLevels()
    {
        var levels = new Dictionary<LogLevel, string>();
        if (HighlightFatal)   levels[LogLevel.Fatal]   = "Log.Fatal";
        if (HighlightError)   levels[LogLevel.Error]   = "Log.Error";
        if (HighlightWarning) levels[LogLevel.Warning] = "Log.Warning";
        if (HighlightInfo)    levels[LogLevel.Info]    = "Log.Info";
        if (HighlightDebug)   levels[LogLevel.Debug]   = "Log.Debug";

        var parser = ActiveParser;
        _levelMarks.Show(levels.Count == 0 ? null : (_, text) => levels.GetValueOrDefault(parser.ParseLine(text).Level));
    }

    // ── Custom term highlight ─────────────────────────────────────────────────

    partial void OnCustomHighlightTermChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _termMarks.Show(null);
            return;
        }

        _termMarks.Show((_, text) => Occurrences(text, value));
    }

    /// <summary>Where <paramref name="term"/> is written in <paramref name="text"/>, whatever its case.</summary>
    private static IReadOnlyList<(int Index, int Length)> Occurrences(string text, string term)
    {
        var found = new List<(int, int)>();
        for (var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(term, at + term.Length, StringComparison.OrdinalIgnoreCase))
            found.Add((at, term.Length));

        return found;
    }

    // ── Regex filter ─────────────────────────────────────────────────────────

    partial void OnFilterRegexChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ActiveFilter   = null;
            IsFilterActive = false;
            return;
        }

        try
        {
            ActiveFilter   = new Regex(value, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            IsFilterActive = true;
        }
        catch (ArgumentException)
        {
            // A pattern being typed is not one yet: the filter stays off until it is.
            ActiveFilter   = null;
            IsFilterActive = false;
        }
    }

    partial void OnActiveFilterChanged(Regex? value) => ShowFilters();
    partial void OnFilterStartChanged(DateTime? value) => ShowFilters();
    partial void OnFilterEndChanged(DateTime? value) => ShowFilters();

    /// <summary>Veils each line the regex filter or the time filter leaves out; with neither, none.</summary>
    private void ShowFilters()
    {
        var (filter, start, end, parser) = (ActiveFilter, FilterStart, FilterEnd, ActiveParser);
        if (filter is null && start is null && end is null)
        {
            _filterMarks.Show(null);
            return;
        }

        // An empty line has nothing to fade.
        _filterMarks.Show((_, text) => text.Length == 0 || Passes(text, filter, start, end, parser) ? null : "Log.Filter");
    }

    /// <summary>Whether a line reading <paramref name="text"/> is one the filters keep: matched by the regex, and stamped inside the range — a line with no stamp is kept.</summary>
    private static bool Passes(string text, Regex? filter, DateTime? start, DateTime? end, ILogParser parser)
    {
        if (filter is not null && !filter.IsMatch(text)) return false;
        if (start is null && end is null) return true;

        return parser.ParseLine(text).Timestamp is not { } stamp || ((start is null || stamp >= start) && (end is null || stamp <= end));
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterRegex    = string.Empty;
        ActiveFilter   = null;
        IsFilterActive = false;
    }

    // ── Time filter ───────────────────────────────────────────────────────────

    /// <summary>Applies the range the date and time fields say — a day without a time running to its end — and goes to the first line inside it.</summary>
    [RelayCommand]
    private async Task ApplyTimeFilter()
    {
        DateTime? start = null, end = null;

        if (FilterStartDate.HasValue)
        {
            var d = FilterStartDate.Value.Date;
            if (TimeSpan.TryParse(FilterStartTime, out var t)) d = d.Add(t);
            start = d;
        }

        if (FilterEndDate.HasValue)
        {
            var d = FilterEndDate.Value.Date;
            if (TimeSpan.TryParse(FilterEndTime, out var t)) d = d.Add(t);
            else d = d.AddDays(1).AddTicks(-1); // end of day if no time given
            end = d;
        }

        FilterStart = start;
        FilterEnd   = end;

        await GoToFirstLineInRangeAsync(start, end);
    }

    /// <summary>Goes to the first line stamped inside the range — found reading the log off the page, from its start, as far as the first.</summary>
    private async Task GoToFirstLineInRangeAsync(DateTime? start, DateTime? end)
    {
        if (start is null && end is null) return;

        var (lines, parser) = (_shown.Lines(), ActiveParser);
        using var found = new CancellationTokenSource();
        long? first = null;

        try
        {
            await Task.Run(() => lines((line, _, text) =>
            {
                if (parser.ParseLine(text).Timestamp is not { } stamp || (start is not null && stamp < start) || (end is not null && stamp > end)) return;

                first = line;
                found.Cancel();
            }, found.Token));
        }
        catch (OperationCanceledException) { }

        if (first is { } line && !_disposed) _shown.GoToLine(line, ShownPlace.Top);
    }

    [RelayCommand]
    private void ClearTimeFilter()
    {
        FilterStartDate = null;
        FilterStartTime = string.Empty;
        FilterEndDate   = null;
        FilterEndTime   = string.Empty;
        FilterStart     = null;
        FilterEnd       = null;
    }

    // ── Timestamp detection ───────────────────────────────────────────────────

    /// <summary>Whether the log is stamped: most of its first lines carry a timestamp the parser reads — and if so, the time it spans.</summary>
    private void DetectTimestamps()
    {
        var sample = _shown.LineTexts(0, 20);

        // Ask the parser that was just selected, not a fixed pattern — a JSON log carries its timestamp in a field, so a text-shaped probe
        // would decide the file has none and hide the time filter.
        var matches = sample.Count(line => line.Trim() is { Length: > 0 } text && ActiveParser.ParseLine(text).Timestamp.HasValue);

        HasTimestamps = sample.Count > 0 && matches >= Math.Max(3, sample.Count / 2);
        if (HasTimestamps) ScanForTimeRange();
    }

    /// <summary>The first and last timestamps the log carries, put in the date and time fields — without applying a filter.</summary>
    private void ScanForTimeRange()
    {
        var (head, tail) = (_shown.LineTexts(0, TimestampReach), _shown.LineTexts(Math.Max(0, _shown.LineCount - TimestampReach), TimestampReach));
        var (first, last) = (FirstStamp(head), FirstStamp(tail.Reverse()));
        _detectedFirstTimestamp = first;
        _detectedLastTimestamp  = last;

        if (first.HasValue)
        {
            FilterStartDate = first.Value.Date;
            FilterStartTime = first.Value.ToString("HH:mm:ss");
        }

        if (last.HasValue)
        {
            FilterEndDate = last.Value.Date;
            FilterEndTime = last.Value.ToString("HH:mm:ss");
        }
    }

    /// <summary>The first timestamp <paramref name="lines"/>, in the order given, carry.</summary>
    private DateTime? FirstStamp(IEnumerable<string> lines)
    {
        foreach (var line in lines)
            if (ActiveParser.ParseLine(line).Timestamp is { } stamp) return stamp;

        return null;
    }

    // ── Line selection ────────────────────────────────────────────────────────

    /// <summary>Picks out line <paramref name="lineNumber"/> of the whole log, counting from one, or lets it go where it was picked out — and copies what is picked out now.</summary>
    public void ToggleLineSelection(long lineNumber)
    {
        if (!_selectedLines.Remove(lineNumber)) _selectedLines.Add(lineNumber);

        Reselected();

        // Each click copies what is now selected; the copy lands when the clipboard takes it, and a busy one keeps the last.
        if (_selectedLines.Count > 0) _ = CopySelectedLinesAsync();
    }

    /// <summary>Whether line <paramref name="lineNumber"/> of the whole log, counting from one, is picked out.</summary>
    public bool IsLineSelected(long lineNumber) => _selectedLines.Contains(lineNumber);

    /// <summary>Moves the lines picked out on by <paramref name="lines"/> — as many as were put before them.</summary>
    private void MoveSelection(long lines)
    {
        if (lines == 0 || _selectedLines.Count == 0) return;

        var moved = _selectedLines.Select(line => line + lines).ToList();
        _selectedLines.Clear();
        _selectedLines.UnionWith(moved);
        Reselected();
    }

    /// <summary>Says the lines picked out again: their count, their wash, and the copy button.</summary>
    private void Reselected()
    {
        SelectedLineCount = _selectedLines.Count;
        OnPropertyChanged(nameof(SelectedLineNumbers));
        _selectedMarks.Show((line, _) => _selectedLines.Contains(_shown.LinesBefore + line + 1) ? "Log.SelectionLine" : null);
    }

    /// <summary>What is written on each line picked out, in file order.</summary>
    private IEnumerable<string> SelectedLineTexts() =>
        _selectedLines.Where(line => line >= 1 && line <= _shown.LineCount).Select(line => _shown.LineText(line - 1)).Where(text => text.Length > 0);

    [RelayCommand(CanExecute = nameof(HasSelectedLines))]
    private Task CopySelectedLines() => CopySelectedLinesAsync();

    private async Task CopySelectedLinesAsync() =>
        await _shell.Clipboard.WriteAsync(ComposedPayload.OfText(string.Join(Environment.NewLine, SelectedLineTexts())));

    private bool HasSelectedLines() => SelectedLineCount > 0;

    partial void OnSelectedLineCountChanged(int value) => CopySelectedLinesCommand.NotifyCanExecuteChanged();
}
