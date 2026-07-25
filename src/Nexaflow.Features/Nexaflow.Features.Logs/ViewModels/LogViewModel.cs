using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Nexaflow.Features.Logs.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}

public sealed partial class LogViewModel : ObservableObject, IDisposable, IPageViewModel
{
    // ── File ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _filePath     = string.Empty;
    [ObservableProperty] private string _fileName     = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int    _lineCount;

    // ── Document ──────────────────────────────────────────────────────────────

    public TextDocument Document { get; } = new();

    // ── Encoding ──────────────────────────────────────────────────────────────

    [ObservableProperty] private EncodingOption _selectedEncoding;
    public List<EncodingOption> AvailableEncodings { get; } =
    [
        new("UTF-8",          Encoding.UTF8),
        new("UTF-16 LE",      Encoding.Unicode),
        new("UTF-16 BE",      Encoding.BigEndianUnicode),
        new("Latin-1",        Encoding.Latin1),
        new("System Default", Encoding.Default),
    ];

    // ── Parser ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ILogParser _activeParser = new RawTextLogParser();

    // ── Loading ───────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoadingHead;
    private Encoding _activeEncoding = Encoding.UTF8;
    private long     _tailStartByte;
    private long     _lastKnownFileSize;
    private CancellationTokenSource? _headLoadCts;

    // ── Monitoring ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isMonitoring    = true;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isAutoScrolling = true;
    private string                  _pendingAppendContent = string.Empty;
    private IFileWatch?             _watch;
    private readonly IShellServices _shell;

    // ── Level highlighting ────────────────────────────────────────────────────

    [ObservableProperty] private bool _highlightFatal;
    [ObservableProperty] private bool _highlightError;
    [ObservableProperty] private bool _highlightWarning;
    [ObservableProperty] private bool _highlightInfo;
    [ObservableProperty] private bool _highlightDebug;

    // ── Custom term ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _customHighlightTerm = string.Empty;

    private IReadOnlyList<(int offset, int length)> _customTermHighlights = [];
    public IReadOnlyList<(int offset, int length)> CustomTermHighlights
    {
        get => _customTermHighlights;
        private set { _customTermHighlights = value; OnPropertyChanged(); }
    }

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

    // Detected timestamp span of the loaded lines, cached on load/head/append (all on the UI thread) so
    // GetContext can report it without reading the thread-affine document off the UI thread.
    private DateTime? _detectedFirstTimestamp;
    private DateTime? _detectedLastTimestamp;

    // ── Navigation ────────────────────────────────────────────────────────────

    // Set to a document offset to trigger scroll; code-behind watches and centres.
    [ObservableProperty] private int _scrollToOffset = -1;

    // ── Line selection ────────────────────────────────────────────────────────

    private readonly HashSet<int> _selectedLineNumbers = [];
    [ObservableProperty] private int _selectedLineCount;

    public IReadOnlySet<int> SelectedLineNumbers => _selectedLineNumbers;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fires when the view should scroll to the bottom (new appended content).</summary>
    public event EventHandler? ScrollToBottomRequested;

    // ──────────────────────────────────────────────────────────────────────────

    public LogViewModel(string filePath, IShellServices shell)
    {
        _shell = shell;
        _selectedEncoding = AvailableEncodings[0];
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath) || !VirtualFileSystem.Instance.Exists(FilePath)) return;

        try
        {
            _headLoadCts?.Cancel();
            _headLoadCts = new CancellationTokenSource();

            _activeEncoding = await DetectEncodingAsync(ct);

            var fileSize = VirtualFileSystem.Instance.GetLength(FilePath);
            _lastKnownFileSize = fileSize;
            FileSizeText       = FormatSize(fileSize);

            if (fileSize <= 4096)
            {
                // Small file: read entirely
                string text;
                await using (var rs = VirtualFileSystem.Instance.OpenRead(FilePath))
                using (var sr = new StreamReader(rs, _activeEncoding))
                    text = await sr.ReadToEndAsync(ct);
                Document.Text  = text;
                _tailStartByte = 0;
            }
            else
            {
                // Large file: tail-first
                await LoadTailAsync(fileSize, ct);
            }

            SelectParser();
            DetectTimestamps();
            LineCount = Document.LineCount;

            if (IsMonitoring) StartMonitoring();

            // Background-load the head for large files
            if (_tailStartByte > 0)
                _ = LoadHeadAsync(_headLoadCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Document.Text = $"Error loading file: {ex.Message}";
        }
    }

    private async Task LoadTailAsync(long fileSize, CancellationToken ct)
    {
        using var fs = VirtualFileSystem.Instance.OpenRead(FilePath);

        // Seek 4 KB back from end; find first \n to start on a clean line
        var seekPoint = fileSize - 4096;
        fs.Seek(seekPoint, SeekOrigin.Begin);
        var buf  = new byte[4096];
        var read = await fs.ReadAsync(buf.AsMemory(0, 4096), ct);

        var nlIdx      = Array.IndexOf(buf, (byte)'\n', 0, read);
        _tailStartByte = nlIdx >= 0 ? seekPoint + nlIdx + 1 : seekPoint;

        // Load from tailStart to EOF
        fs.Seek(_tailStartByte, SeekOrigin.Begin);
        using var reader    = new StreamReader(fs, _activeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var tailContent     = await reader.ReadToEndAsync(ct);
        Document.Text       = tailContent;
        IsLoadingHead       = true;
    }

    private async Task LoadHeadAsync(CancellationToken ct)
    {
        if (_tailStartByte <= 0) { IsLoadingHead = false; return; }

        try
        {
            // Read in 64 KB chunks to avoid a single massive allocation
            var sb         = new StringBuilder((int)Math.Min(_tailStartByte, 64 * 1024 * 1024));
            var buf        = new byte[65536];
            var totalBytes = 0L;

            using var fs = VirtualFileSystem.Instance.OpenRead(FilePath);
            while (totalBytes < _tailStartByte)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(buf.Length, _tailStartByte - totalBytes);
                var n      = await fs.ReadAsync(buf.AsMemory(0, toRead), ct);
                if (n == 0) break;
                sb.Append(_activeEncoding.GetString(buf, 0, n));
                totalBytes += n;
            }

            var headContent = sb.ToString();

            // The awaits above resumed on the UI thread (LoadAsync started there), so update directly.
            Document.Insert(0, headContent);
            IsLoadingHead = false;
            LineCount     = Document.LineCount;
            ScanForCustomHighlights();
            if (HasTimestamps) ScanForTimeRange(); // refresh with oldest entries now available
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { IsLoadingHead = false; }
        catch                              { IsLoadingHead = false; }
    }

    // ── Encoding detection ────────────────────────────────────────────────────

    private async Task<Encoding> DetectEncodingAsync(CancellationToken ct)
    {
        // Use the shared sniffer (BOM + UTF-8 validity scan + UTF-16 heuristic) so every viewer detects
        // encoding identically. The probe is small; offload the synchronous read off the UI thread.
        return await Task.Run(() =>
        {
            using var fs = VirtualFileSystem.Instance.OpenRead(FilePath);
            return EncodingDetector.Detect(fs).Encoding;
        }, ct);
    }

    // ── Parser selection ──────────────────────────────────────────────────────

    private void SelectParser()
    {
        var firstLine = Document.LineCount > 0
            ? Document.GetText(Document.GetLineByNumber(1))
            : string.Empty;

        // Use file header bytes for confidence voting
        byte[] header = [];
        try
        {
            using var fs = VirtualFileSystem.Instance.OpenRead(FilePath);
            header       = new byte[Math.Min(16, fs.Length)];
            _ = fs.Read(header, 0, header.Length);
        }
        catch { /* best effort */ }

        ActiveParser = LogParserRegistry.SelectParser(header, firstLine);
    }

    // ── Timestamp detection ───────────────────────────────────────────────────

    private void DetectTimestamps()
    {
        if (Document.LineCount == 0) { HasTimestamps = false; return; }

        var sampleCount = Math.Min(20, Document.LineCount);
        var matches     = 0;

        // Ask the parser that was just selected, not a fixed regex — a JSON log carries its timestamp in a
        // field, so a text-shaped probe would decide the file has none and hide the time filter.
        for (var i = 1; i <= sampleCount; i++)
        {
            var line = Document.GetLineByNumber(i);
            var text = Document.GetText(line.Offset, line.Length).Trim();
            if (text.Length > 0 && ActiveParser.ParseLine(text).Timestamp.HasValue) matches++;
        }

        HasTimestamps = matches >= Math.Max(3, sampleCount / 2);
        if (HasTimestamps) ScanForTimeRange();
    }

    // Scans all loaded lines for the first and last parseable timestamps and
    // pre-populates the date/time filter fields (without applying the filter).
    private void ScanForTimeRange()
    {
        var (first, last) = ScanTimestampRange();
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

    // Finds the first and last parseable timestamps across the loaded lines (document read — UI thread).
    private (DateTime? First, DateTime? Last) ScanTimestampRange()
    {
        DateTime? first = null, last = null;

        for (var i = 1; i <= Document.LineCount && !first.HasValue; i++)
        {
            try
            {
                var dl     = Document.GetLineByNumber(i);
                var parsed = ActiveParser.ParseLine(Document.GetText(dl.Offset, dl.Length));
                if (parsed.Timestamp.HasValue) first = parsed.Timestamp.Value;
            }
            catch { break; }
        }

        for (var i = Document.LineCount; i >= 1 && !last.HasValue; i--)
        {
            try
            {
                var dl     = Document.GetLineByNumber(i);
                var parsed = ActiveParser.ParseLine(Document.GetText(dl.Offset, dl.Length));
                if (parsed.Timestamp.HasValue) last = parsed.Timestamp.Value;
            }
            catch { break; }
        }

        return (first, last);
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        _watch?.Dispose();
        if (!File.Exists(FilePath)) return;

        _watch = _shell.WatchFile(FilePath, OnFileChanged);
    }

    // Invoked by the shell on this workspace's UI thread when the watched file changes.
    private async void OnFileChanged()
    {
        if (!IsMonitoring) return;

        long newSize;
        try   { newSize = new FileInfo(FilePath).Length; }
        catch { return; }

        if (newSize <= _lastKnownFileSize) return;

        // Read only newly appended bytes
        var newByteCount = (int)(newSize - _lastKnownFileSize);
        var buf          = new byte[newByteCount];
        try
        {
            using var fs   = VirtualFileSystem.Instance.OpenRead(FilePath);
            fs.Seek(_lastKnownFileSize, SeekOrigin.Begin);
            var read       = await fs.ReadAsync(buf.AsMemory(0, newByteCount));
            var newContent = _activeEncoding.GetString(buf, 0, read);
            _lastKnownFileSize = newSize;
            FileSizeText       = FormatSize(newSize);

            if (!IsPaused)
            {
                Document.Insert(Document.TextLength, newContent);
                LineCount = Document.LineCount;
                ScanForCustomHighlights();
                if (IsAutoScrolling) ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _pendingAppendContent += newContent;
                _lastKnownFileSize     = newSize;
            }
        }
        catch { /* file may be locked briefly */ }
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        if (IsMonitoring) StartMonitoring();
        else { _watch?.Dispose(); _watch = null; }
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (!value && _pendingAppendContent.Length > 0)
        {
            Document.Insert(Document.TextLength, _pendingAppendContent);
            _pendingAppendContent = string.Empty;
            LineCount = Document.LineCount;
            ScanForCustomHighlights();
            if (IsAutoScrolling) ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Level highlighting ────────────────────────────────────────────────────

    // Property-changed partials just raise PropertyChanged so the code-behind
    // can rebuild the colorizer's EnabledLevels set.
    partial void OnHighlightFatalChanged(bool value)   => OnPropertyChanged(nameof(HighlightFatal));
    partial void OnHighlightErrorChanged(bool value)   => OnPropertyChanged(nameof(HighlightError));
    partial void OnHighlightWarningChanged(bool value) => OnPropertyChanged(nameof(HighlightWarning));
    partial void OnHighlightInfoChanged(bool value)    => OnPropertyChanged(nameof(HighlightInfo));
    partial void OnHighlightDebugChanged(bool value)   => OnPropertyChanged(nameof(HighlightDebug));

    // ── Custom term highlight ─────────────────────────────────────────────────

    partial void OnCustomHighlightTermChanged(string value) => ScanForCustomHighlights();

    private void ScanForCustomHighlights()
    {
        var term = CustomHighlightTerm;
        if (string.IsNullOrWhiteSpace(term)) { CustomTermHighlights = []; return; }

        var hits  = new List<(int, int)>();
        var text  = Document.Text;
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            hits.Add((idx, term.Length));
            start = idx + term.Length;
        }
        CustomTermHighlights = hits;
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
        catch
        {
            // Invalid regex — keep filter active with null so code-behind handles gracefully
            ActiveFilter   = null;
            IsFilterActive = false;
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterRegex    = string.Empty;
        ActiveFilter   = null;
        IsFilterActive = false;
    }

    // ── Time filter ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyTimeFilter()
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

        ScrollToFirstFilteredLine();
    }

    private void ScrollToFirstFilteredLine()
    {
        for (var i = 1; i <= Document.LineCount; i++)
        {
            try
            {
                var dl     = Document.GetLineByNumber(i);
                var text   = Document.GetText(dl.Offset, dl.Length);
                var parsed = ActiveParser.ParseLine(text);
                if (!parsed.Timestamp.HasValue) continue;

                var ts     = parsed.Timestamp.Value;
                var passes = true;
                if (FilterStart.HasValue && ts < FilterStart.Value) passes = false;
                if (FilterEnd.HasValue   && ts > FilterEnd.Value)   passes = false;

                if (passes) { ScrollToOffset = dl.Offset; return; }
            }
            catch { break; }
        }
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

    // ── Line selection ────────────────────────────────────────────────────────

    public void ToggleLineSelection(int lineNumber)
    {
        if (!_selectedLineNumbers.Remove(lineNumber))
            _selectedLineNumbers.Add(lineNumber);

        SelectedLineCount = _selectedLineNumbers.Count;
        OnPropertyChanged(nameof(SelectedLineNumbers));
        AutoCopySelectedLines();
    }

    private void AutoCopySelectedLines()
    {
        if (_selectedLineNumbers.Count == 0) return;

        var lines = _selectedLineNumbers
            .OrderBy(n => n)
            .Select(n =>
            {
                try
                {
                    var dl = Document.GetLineByNumber(n);
                    return Document.GetText(dl.Offset, dl.Length);
                }
                catch { return string.Empty; }
            })
            .Where(l => l.Length > 0);

        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); }
        catch { /* clipboard may be locked by another process */ }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLines))]
    private void CopySelectedLines()
    {
        var lines = _selectedLineNumbers
            .OrderBy(n => n)
            .Select(n =>
            {
                try
                {
                    var dl = Document.GetLineByNumber(n);
                    return Document.GetText(dl.Offset, dl.Length);
                }
                catch { return string.Empty; }
            })
            .Where(l => l.Length > 0);

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    private bool HasSelectedLines() => SelectedLineCount > 0;

    partial void OnSelectedLineCountChanged(int value) => CopySelectedLinesCommand.NotifyCanExecuteChanged();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public string GetContext()
    {
        if (string.IsNullOrEmpty(FilePath)) return "Log viewer: no file loaded.";

        var sb = new StringBuilder();
        sb.Append($"Log file '{FileName}' ({FileSizeText}) at '{FilePath}'. ");
        sb.Append($"{LineCount} line(s) loaded");
        if (IsLoadingHead)
            sb.Append(" — currently only the tail (most recent lines); the head is still streaming in");
        sb.Append($". Format: {ActiveParser.FormatName}. ");

        if (_detectedFirstTimestamp.HasValue && _detectedLastTimestamp.HasValue)
            sb.Append($"Timestamps span {_detectedFirstTimestamp:yyyy-MM-dd HH:mm:ss} to {_detectedLastTimestamp:yyyy-MM-dd HH:mm:ss}. ");
        else if (HasTimestamps)
            sb.Append("Lines carry timestamps. ");
        else
            sb.Append("No timestamps detected. ");

        if (IsMonitoring)
            sb.Append(IsPaused ? "The live tail is paused. " : "Live-tailing the file for new lines. ");
        else
            sb.Append("Live monitoring is off. ");

        var active = new List<string>();
        if (IsFilterActive && !string.IsNullOrWhiteSpace(FilterRegex))
            active.Add($"regex filter /{FilterRegex}/");
        if (FilterStart.HasValue || FilterEnd.HasValue)
            active.Add($"time filter {(FilterStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "…")} .. {(FilterEnd?.ToString("yyyy-MM-dd HH:mm:ss") ?? "…")}");
        var levels = EnabledHighlightLevels();
        if (levels.Count > 0)
            active.Add($"level highlight: {string.Join(", ", levels)}");
        if (!string.IsNullOrWhiteSpace(CustomHighlightTerm))
            active.Add($"term highlight \"{CustomHighlightTerm}\"");

        sb.Append(active.Count > 0
            ? $"Active view filters/highlights — {string.Join("; ", active)} (non-matching lines are faded, not removed). "
            : "No filters or highlights active. ");

        if (SelectedLineCount > 0)
            sb.Append($"{SelectedLineCount} line(s) selected. ");

        sb.Append("Use read_tail / read_lines / search_log to read the log content; "
                + "set_regex_filter / set_time_filter / highlight_levels / set_tail_paused mirror the toolbar controls.");
        return sb.ToString();
    }

    /// <summary>File-path scope boundary — keeps two log tabs on different files distinguishable when pinned.</summary>
    public string? GetSecurityContext() => FilePath;

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) — the log content the model couldn't reach before ──
        new DelegateClientTool(
            "read_tail",
            "Read the most recent (bottom) lines of the loaded log — the live tail. For a large file only the "
          + "already-loaded portion is available until the head finishes streaming in.",
            [ new ClientToolParameter("lines", "How many trailing lines to return (default 100).", Required: false, Type: "number") ],
            ToolSafety.SafeOperation,
            (args, _) => OnUi(() =>
            {
                var total = Document.LineCount;
                if (total == 0) return ToolResult.Ok("empty", "(the log is empty)");
                var n     = Math.Clamp(ToolArgs.Int(args, "lines", 100), 1, 5000);
                var start = Math.Max(1, total - n + 1);
                return ToolResult.Ok($"read lines {start}–{total} of {total}", ReadLineRange(start, total));
            })),

        new DelegateClientTool(
            "read_lines",
            "Read a specific 1-based line range from the loaded log.",
            [
                new ClientToolParameter("start", "First line number (1-based)."),
                new ClientToolParameter("count", "How many lines to read (default 100).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) => OnUi(() =>
            {
                var total = Document.LineCount;
                if (total == 0) return ToolResult.Ok("empty", "(the log is empty)");
                var start = Math.Clamp(ToolArgs.Int(args, "start", 1), 1, total);
                var count = Math.Clamp(ToolArgs.Int(args, "count", 100), 1, 5000);
                var end   = Math.Min(total, start + count - 1);
                return ToolResult.Ok($"read lines {start}–{end} of {total}", ReadLineRange(start, end));
            })),

        new DelegateClientTool(
            "search_log",
            "Search the whole loaded log for a substring or regular expression; returns matching lines with "
          + "their line numbers (capped). Reads across the entire loaded document, not just the visible viewport.",
            [
                new ClientToolParameter("query", "Text or regular expression to find."),
                new ClientToolParameter("regex", "Treat 'query' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("max", "Maximum matches to return (default 100).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) => OnUi(() =>
            {
                var query = ToolArgs.Str(args, "query", "pattern", "search", "term");
                if (string.IsNullOrEmpty(query)) return ToolResult.Error("No 'query' provided.");

                Regex? rx = null;
                if (ToolArgs.Bool(args, "regex"))
                {
                    try   { rx = new Regex(query, RegexOptions.IgnoreCase); }
                    catch (Exception ex) { return ToolResult.Error($"Invalid regular expression: {ex.Message}"); }
                }

                var max   = Math.Clamp(ToolArgs.Int(args, "max", 100), 1, 1000);
                var total = Document.LineCount;
                var hits  = new List<string>();
                for (var i = 1; i <= total && hits.Count < max; i++)
                {
                    var dl   = Document.GetLineByNumber(i);
                    var text = Document.GetText(dl.Offset, dl.Length);
                    var ok   = rx is not null ? rx.IsMatch(text) : text.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (ok) hits.Add($"{i}: {text}");
                }
                if (hits.Count == 0) return ToolResult.Ok($"no match for {query}", $"No loaded line matched '{query}'.");
                var note = hits.Count >= max ? $"\n(stopped at {max} matches; more may exist.)" : string.Empty;
                return ToolResult.Ok($"{hits.Count} match(es) for {query}", string.Join("\n", hits) + note);
            })),

        new DelegateClientTool(
            "read_selected_lines",
            "Read the lines the user has selected (checked in the gutter), in order.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => OnUi(() =>
            {
                if (_selectedLineNumbers.Count == 0)
                    return ToolResult.Ok("no selection", "The user has not selected any lines.");
                var total = Document.LineCount;
                var lines = _selectedLineNumbers.OrderBy(n => n)
                    .Where(n => n >= 1 && n <= total)
                    .Select(n => { var dl = Document.GetLineByNumber(n); return $"{n}: {Document.GetText(dl.Offset, dl.Length)}"; });
                return ToolResult.Ok($"{_selectedLineNumbers.Count} selected line(s)", string.Join("\n", lines));
            })),

        // ── View state (auto-run) — mirror the user's filter / highlight / tail controls (reversible, faded) ──
        new DelegateClientTool(
            "set_regex_filter",
            "Set or clear the regex filter that fades non-matching lines — the same box the user types in. Pass "
          + "an empty pattern to clear it. Lines are faded, never removed from the file.",
            [ new ClientToolParameter("pattern", "A regular expression; empty clears the filter.", Required: false) ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var pattern = ToolArgs.Str(args, "pattern", "regex", "filter") ?? string.Empty;
                await _shell.RunOnUiAsync(() => FilterRegex = pattern);
                if (pattern.Length == 0) return ToolResult.Ok("filter cleared", "Cleared the regex filter.");
                return IsFilterActive
                    ? ToolResult.Ok($"filtering /{pattern}/", $"Applied regex filter '{pattern}'. Non-matching lines are faded.")
                    : ToolResult.Error($"'{pattern}' is not a valid regular expression.");
            }),

        new DelegateClientTool(
            "set_time_filter",
            "Fade lines whose timestamp falls outside a range (only meaningful when the log has timestamps), or "
          + "clear it. Pass ISO-8601 date/times; omit both start and end to clear.",
            [
                new ClientToolParameter("start", "Range start, e.g. 2024-01-15T10:00:00 (optional).", Required: false),
                new ClientToolParameter("end", "Range end, e.g. 2024-01-15T11:00:00 (optional).", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var startStr = ToolArgs.Str(args, "start", "from");
                var endStr   = ToolArgs.Str(args, "end", "to");
                if (startStr is null && endStr is null)
                {
                    await _shell.RunOnUiAsync(() => ClearTimeFilterCommand.Execute(null));
                    return ToolResult.Ok("time filter cleared", "Cleared the time filter.");
                }

                DateTime start = default, end = default;
                if (startStr is not null &&
                    !DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                    return ToolResult.Error($"Could not parse start time '{startStr}'.");
                if (endStr is not null &&
                    !DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                    return ToolResult.Error($"Could not parse end time '{endStr}'.");

                await _shell.RunOnUiAsync(() =>
                {
                    if (startStr is not null) { FilterStartDate = start.Date; FilterStartTime = start.ToString("HH:mm:ss"); }
                    if (endStr   is not null) { FilterEndDate   = end.Date;   FilterEndTime   = end.ToString("HH:mm:ss"); }
                    ApplyTimeFilterCommand.Execute(null);
                });
                return ToolResult.Ok("time filter applied",
                    $"Filtering to timestamps in [{FilterStart?.ToString("s") ?? "…"} .. {FilterEnd?.ToString("s") ?? "…"}]. "
                  + "Lines outside the range are faded.");
            }),

        new DelegateClientTool(
            "highlight_levels",
            "Set which severity levels are colour-highlighted (Fatal, Error, Warning, Info, Debug). Pass a "
          + "comma-separated list to enable exactly those; an empty value turns all level highlighting off.",
            [ new ClientToolParameter("levels", "Comma-separated levels, e.g. \"error,warning\"; empty turns all off.", Required: false) ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var raw    = ToolArgs.Str(args, "levels", "level") ?? string.Empty;
                var wanted = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(x => x.ToLowerInvariant()).ToHashSet();
                await _shell.RunOnUiAsync(() =>
                {
                    HighlightFatal   = wanted.Contains("fatal")   || wanted.Contains("critical");
                    HighlightError   = wanted.Contains("error")   || wanted.Contains("err");
                    HighlightWarning = wanted.Contains("warning") || wanted.Contains("warn");
                    HighlightInfo    = wanted.Contains("info");
                    HighlightDebug   = wanted.Contains("debug")   || wanted.Contains("trace");
                });
                var on = EnabledHighlightLevels();
                return ToolResult.Ok(
                    on.Count > 0 ? $"highlighting {string.Join(", ", on)}" : "highlighting off",
                    on.Count > 0 ? $"Highlighting levels: {string.Join(", ", on)}." : "Turned off all level highlighting.");
            }),

        new DelegateClientTool(
            "set_tail_paused",
            "Pause or resume the live tail (whether newly-appended lines are shown as the file grows). Resuming "
          + "flushes lines that arrived while paused. Display only — the file is untouched.",
            [ new ClientToolParameter("paused", "true to pause, false to resume.", Type: "boolean") ],
            ToolSafety.SafeOperation,
            async (args, _) =>
            {
                var paused = ToolArgs.Bool(args, "paused", true);
                await _shell.RunOnUiAsync(() => IsPaused = paused);
                return ToolResult.Ok(paused ? "tail paused" : "tail resumed",
                    paused ? "Paused the live tail." : "Resumed the live tail.");
            }),
    ];

    // Runs a document-reading tool body on the workspace UI thread (the document is thread-affine),
    // returning its result — features never touch the dispatcher directly.
    private Task<ToolResult> OnUi(Func<ToolResult> read) => _shell.RunOnUiAsync(() => Task.FromResult(read()));

    private string ReadLineRange(int startLine, int endLine)
    {
        var sb = new StringBuilder();
        for (var i = startLine; i <= endLine; i++)
        {
            var dl = Document.GetLineByNumber(i);
            sb.Append(i).Append(": ").Append(Document.GetText(dl.Offset, dl.Length)).Append('\n');
        }
        return sb.ToString();
    }

    private List<string> EnabledHighlightLevels()
    {
        var levels = new List<string>(5);
        if (HighlightFatal)   levels.Add("Fatal");
        if (HighlightError)   levels.Add("Error");
        if (HighlightWarning) levels.Add("Warning");
        if (HighlightInfo)    levels.Add("Info");
        if (HighlightDebug)   levels.Add("Debug");
        return levels;
    }

    public IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext
        {
            RootPath      = dir,
            CurrentPath   = dir,
            SelectedItems = [FilePath]
        };
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _headLoadCts?.Cancel();
        _watch?.Dispose();
    }
}
