using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
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
    private string              _pendingAppendContent  = string.Empty;
    private FileChangeWatcher?  _watcher;

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

    public LogViewModel(string filePath)
    {
        _selectedEncoding = AvailableEncodings[0];
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return;

        try
        {
            _headLoadCts?.Cancel();
            _headLoadCts = new CancellationTokenSource();

            _activeEncoding = await DetectEncodingAsync(ct);

            var info     = new FileInfo(FilePath);
            var fileSize = info.Length;
            _lastKnownFileSize = fileSize;
            FileSizeText       = FormatSize(fileSize);

            if (fileSize <= 4096)
            {
                // Small file: read entirely
                var text       = await File.ReadAllTextAsync(FilePath, _activeEncoding, ct);
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
        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

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

            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Document.Insert(0, headContent);
                IsLoadingHead = false;
                LineCount     = Document.LineCount;
                ScanForCustomHighlights();
                if (HasTimestamps) ScanForTimeRange(); // refresh with oldest entries now available
                ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException) { IsLoadingHead = false; }
        catch                              { IsLoadingHead = false; }
    }

    // ── Encoding detection ────────────────────────────────────────────────────

    private async Task<Encoding> DetectEncodingAsync(CancellationToken ct)
    {
        using var fs  = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header    = new byte[4];
        var read      = await fs.ReadAsync(header.AsMemory(0, 4), ct);

        // BOM check
        if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF) return Encoding.BigEndianUnicode;

        // No BOM: read first line and check for non-printable ASCII bytes
        fs.Seek(0, SeekOrigin.Begin);
        var lineBuf  = new byte[512];
        var lineRead = await fs.ReadAsync(lineBuf.AsMemory(0, lineBuf.Length), ct);
        var nlIdx    = Array.IndexOf(lineBuf, (byte)'\n', 0, lineRead);
        var end      = nlIdx >= 0 ? nlIdx : lineRead;

        for (var i = 0; i < end; i++)
        {
            var b = lineBuf[i];
            // Non-printable control bytes (excluding TAB=0x09, LF=0x0A, CR=0x0D)
            if ((b < 0x09) || (b > 0x0D && b < 0x20) || b == 0x7F)
                return Encoding.Unicode;
        }

        return Encoding.UTF8;
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
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

        for (var i = 1; i <= sampleCount; i++)
        {
            var line = Document.GetLineByNumber(i);
            var text = Document.GetText(line.Offset, line.Length).Trim();
            if (text.Length > 0 && RawTextLogParser.LineHasTimestamp(text)) matches++;
        }

        HasTimestamps = matches >= Math.Max(3, sampleCount / 2);
        if (HasTimestamps) ScanForTimeRange();
    }

    // Scans all loaded lines for the first and last parseable timestamps and
    // pre-populates the date/time filter fields (without applying the filter).
    private void ScanForTimeRange()
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

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        _watcher?.Dispose();
        if (!File.Exists(FilePath)) return;

        _watcher = new FileChangeWatcher(FilePath);
        _watcher.Changed += OnFileChanged;
    }

    private async void OnFileChanged()
    {
        if (!IsMonitoring) return;

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            long newSize;
            try   { newSize = new FileInfo(FilePath).Length; }
            catch { return; }

            if (newSize <= _lastKnownFileSize) return;

            // Read only newly appended bytes
            var newByteCount = (int)(newSize - _lastKnownFileSize);
            var buf          = new byte[newByteCount];
            try
            {
                using var fs   = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
        });
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        if (IsMonitoring) StartMonitoring();
        else { _watcher?.Dispose(); _watcher = null; }
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
        return $"Log file: '{FileName}' at '{FilePath}'. {LineCount} line(s).";
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
        _watcher?.Dispose();
    }
}
