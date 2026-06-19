using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Formatting;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;

namespace Nexaflow.Features.Text.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}

public sealed partial class TextViewModel : ObservableObject, IDisposable, IPageViewModel
{
    private const long SmallFileSizeLimit = 100 * 1024; // 100 KB
    private const int  ChunkSize          = 100 * 1024; // bytes per sliding-window chunk

    // ── File info ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private string _fileName    = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int    _lineCount;
    [ObservableProperty] private bool   _isLargeFile;

    // ── Document (owned here; code-behind sets Editor.Document = this.Document) ──

    public TextDocument Document { get; } = new TextDocument();

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

    // ── Display toggles ───────────────────────────────────────────────────────

    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private bool _wordWrap;

    // ── Monitoring ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isMonitoring = true;

    // ── Search state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private bool   _isSearchRunning;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _fileChangedMessage    = string.Empty;
    [ObservableProperty] private bool   _fileChangedBannerVisible;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    // -1 means "not yet navigated"; code-behind watches this to trigger centering
    [ObservableProperty] private int _scrollToOffset = -1;

    private int _currentMatchIndex = -1;

    private IReadOnlyList<(int offset, int length)> _searchHighlights = [];
    private IReadOnlyList<double>                   _miniMapMarks     = [];

    public IReadOnlyList<(int offset, int length)> SearchHighlights
    {
        get => _searchHighlights;
        private set { _searchHighlights = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<double> MiniMapMarks
    {
        get => _miniMapMarks;
        private set { _miniMapMarks = value; OnPropertyChanged(); }
    }

    // ── Large-file state ──────────────────────────────────────────────────────

    private FileStream? _fileStream;
    private long        _loadedByteEnd;
    private bool        _moreDataAvailable;
    private bool        _isLoadingChunk;
    private string      _loadedText     = string.Empty; // decoded text of the loaded byte range
    private long[]      _lineByteStarts = [];            // byte offset of each line start (pre-scan)
    private int         _fileLineCount;                  // total lines from pre-scan

    // Number of document lines backed by real file content (not placeholder newlines).
    // Updated after each chunk; code-behind compares this against the viewport bottom line.
    public int LoadedLineCount { get; private set; }

    // ── Monitoring ────────────────────────────────────────────────────────────

    private FileSystemWatcher? _watcher;

    // ── Search cancellation ───────────────────────────────────────────────────

    private CancellationTokenSource? _searchCts;

    // ── Last search (for re-running after reload) ─────────────────────────────

    private Func<CancellationToken, Task>? _lastSearch;

    // ── Active search state (used for navigation and incremental highlighting) ─

    // All line numbers (0-based) across the full file that contain at least one match.
    // Populated by streaming scan; used by FindNext/FindPrevious to navigate beyond
    // the loaded portion without relying on SearchHighlights.
    private int[]  _matchingLineNumbers  = [];
    private Regex? _activeRegex;                         // set when search is regex mode
    private string _activeSearchPattern  = string.Empty; // set when search is conventional mode

    // ─────────────────────────────────────────────────────────────────────────

    public TextViewModel(string filePath)
    {
        _selectedEncoding = AvailableEncodings[0]; // UTF-8
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        try
        {
            var info = new FileInfo(FilePath);
            FileSizeText = FormatSize(info.Length);
            IsLargeFile  = info.Length >= SmallFileSizeLimit;

            if (!IsLargeFile)
            {
                var text = await File.ReadAllTextAsync(FilePath, SelectedEncoding.Encoding, ct);
                Document.Text = text;
                LineCount     = Document.LineCount;
            }
            else
            {
                _fileStream?.Dispose();
                _fileStream    = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _loadedByteEnd = 0;
                _loadedText    = string.Empty;
                Document.Text  = string.Empty;

                // Pre-scan for exact line count so the scrollbar and minimap share the
                // same coordinate space from the very first chunk.
                await PreScanAsync(ct);
                LineCount = _fileLineCount;

                await LoadNextChunkCoreAsync(ct);
            }

            if (IsMonitoring) StartMonitoring();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Document.Text = $"Error loading file: {ex.Message}";
        }
    }

    private async Task PreScanAsync(CancellationToken ct)
    {
        var starts = new List<long> { 0L };
        long pos   = 0;
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
                if (buffer[i] == '\n') starts.Add(pos + i + 1);
            pos += read;
        }
        _lineByteStarts = starts.ToArray();
        _fileLineCount  = _lineByteStarts.Length; // one entry per line start = line count
    }

    private async Task LoadNextChunkCoreAsync(CancellationToken ct)
    {
        if (_fileStream is null) return;

        var buffer = new byte[ChunkSize];
        _fileStream.Seek(_loadedByteEnd, SeekOrigin.Begin);
        var read = await _fileStream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct);
        if (read == 0) { _moreDataAvailable = false; return; }

        var oldLoadedLen = _loadedText.Length;   // where real content currently ends in Document
        var oldDocLen    = Document.TextLength;  // includes old sep + old placeholder

        var newChunk = SelectedEncoding.Encoding.GetString(buffer, 0, read);
        _loadedText    += newChunk;
        _loadedByteEnd += read;
        _moreDataAvailable = _loadedByteEnd < _fileStream.Length;

        // Binary-search line starts to find how many lines are now backed by real content.
        var idx = Array.BinarySearch(_lineByteStarts, _loadedByteEnd);
        if (idx < 0) idx = ~idx;
        LoadedLineCount = idx;

        // Replace everything from oldLoadedLen to end of document with:
        //   newChunk  +  separator (if loaded text doesn't end with \n)  +  placeholder newlines
        // This uses AvalonEdit's O(log N) rope Replace — no full-string rebuild.
        var suffix = BuildPlaceholderSuffix();
        Document.Replace(oldLoadedLen, oldDocLen - oldLoadedLen, newChunk + suffix);

        // Append highlights for the newly loaded content so the renderer keeps up.
        AppendHighlightsForChunk(oldLoadedLen, newChunk);
    }

    private void AppendHighlightsForChunk(int chunkDocOffset, string chunk)
    {
        if (!IsSearchActive) return;

        var additions = new List<(int, int)>();
        if (_activeRegex is not null)
        {
            foreach (Match m in _activeRegex.Matches(chunk))
                additions.Add((chunkDocOffset + m.Index, m.Length));
        }
        else if (!string.IsNullOrEmpty(_activeSearchPattern))
        {
            var cmp   = StringComparison.OrdinalIgnoreCase;
            var start = 0;
            while (true)
            {
                var i = chunk.IndexOf(_activeSearchPattern, start, cmp);
                if (i < 0) break;
                additions.Add((chunkDocOffset + i, _activeSearchPattern.Length));
                start = i + _activeSearchPattern.Length;
            }
        }

        if (additions.Count > 0)
        {
            var merged = new List<(int, int)>(SearchHighlights);
            merged.AddRange(additions);
            SearchHighlights = merged;
        }
    }

    // Returns the separator + placeholder block that follows the loaded text in the document.
    // The result keeps Document.LineCount == _fileLineCount while content loads incrementally.
    private string BuildPlaceholderSuffix()
    {
        if (!_moreDataAvailable) return string.Empty;

        var loadedNl  = 0;
        foreach (var c in _loadedText) if (c == '\n') loadedNl++;

        var remaining = _fileLineCount - 1 - loadedNl;
        if (remaining <= 0) return string.Empty;

        // If loaded text doesn't end with \n, add one so placeholders start on a fresh line.
        var needsNl = _loadedText.Length > 0 && _loadedText[^1] != '\n';
        if (needsNl) remaining--;

        var sb = new StringBuilder((needsNl ? 1 : 0) + Math.Max(0, remaining));
        if (needsNl) sb.Append('\n');
        if (remaining > 0) sb.Append('\n', remaining);
        return sb.ToString();
    }

    // Called by code-behind whenever the viewport bottom line advances into the placeholder
    // region. Loads chunks in sequence (concurrency-guarded) until real content covers
    // `targetLine` plus a look-ahead buffer.
    public async Task EnsureContentLoadedUpToLineAsync(int targetLine)
    {
        if (_isLoadingChunk || !_moreDataAvailable) return;
        _isLoadingChunk = true;
        try
        {
            const int LookAhead = 200; // lines of buffer past the viewport bottom
            while (_moreDataAvailable && LoadedLineCount < targetLine + LookAhead)
                await LoadNextChunkCoreAsync(CancellationToken.None);
        }
        finally { _isLoadingChunk = false; }
    }

    // ── Encoding change ───────────────────────────────────────────────────────

    partial void OnSelectedEncodingChanged(EncodingOption value)
    {
        // Re-read the file when the user changes encoding
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _searchCts?.Cancel();
        _fileStream?.Dispose();
        _fileStream   = null;
        _loadedText   = string.Empty;
        Document.Text = string.Empty;
        await LoadAsync(CancellationToken.None);
        if (IsSearchActive) await ReRunSearchAsync();
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        _watcher?.Dispose();
        if (!File.Exists(FilePath)) return;

        var dir      = Path.GetDirectoryName(FilePath)!;
        var fileName = Path.GetFileName(FilePath);
        _watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter         = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents  = true,
        };
        _watcher.Changed += OnFileChanged;
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        await Task.Delay(300); // debounce double-fire
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            FileChangedMessage         = "File changed on disk — reloading…";
            FileChangedBannerVisible   = true;
            _fileStream?.Dispose();
            _fileStream   = null;
            _loadedText   = string.Empty;
            Document.Text = string.Empty;
            await LoadAsync(CancellationToken.None);
            if (IsSearchActive) await ReRunSearchAsync();
            await Task.Delay(3000);
            FileChangedBannerVisible = false;
        });
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        if (IsMonitoring)
            StartMonitoring();
        else
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    public async Task SearchRegexAsync(string pattern, CancellationToken externalCt = default)
    {
        _lastSearch = ct => SearchRegexAsync(pattern, ct);
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_searchCts.Token, externalCt);
        var ct = linked.Token;

        IsSearchRunning = true;
        try
        {
            Regex regex;
            try   { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
            catch { IsSearchRunning = false; return; }

            var (highlights, marks, count) = IsLargeFile
                ? await ScanFileForRegexAsync(regex, ct)
                : ScanDocumentForRegex(regex);

            _activeRegex          = regex;
            _activeSearchPattern  = string.Empty;

            CurrentSearchTerm  = $"/{pattern}/";
            SearchHighlights   = highlights;
            MiniMapMarks       = marks;
            SearchMatchCount   = count;
            IsSearchActive     = count > 0;
            _currentMatchIndex = highlights.Count > 0 ? 0 : -1;
            ScrollToOffset     = highlights.Count > 0 ? highlights[0].Item1 : -1;
        }
        catch (OperationCanceledException) { }
        finally { IsSearchRunning = false; }
    }

    public async Task SearchConventionalAsync(string query, CancellationToken externalCt = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        _lastSearch = ct => SearchConventionalAsync(query, ct);
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_searchCts.Token, externalCt);
        var ct = linked.Token;

        IsSearchRunning = true;
        try
        {
            var (highlights, marks, count) = IsLargeFile
                ? await ScanFileConventionalAsync(query, ct)
                : ScanDocumentConventional(query);

            _activeRegex         = null;
            _activeSearchPattern = query;

            CurrentSearchTerm  = query;
            SearchHighlights   = highlights;
            MiniMapMarks       = marks;
            SearchMatchCount   = count;
            IsSearchActive     = count > 0;
            _currentMatchIndex = highlights.Count > 0 ? 0 : -1;
            ScrollToOffset     = highlights.Count > 0 ? highlights[0].Item1 : -1;
        }
        catch (OperationCanceledException) { }
        finally { IsSearchRunning = false; }
    }

    private Task ReRunSearchAsync() => _lastSearch?.Invoke(CancellationToken.None) ?? Task.CompletedTask;

    // Small-file search (operates on loaded DocumentText)

    private (IReadOnlyList<(int, int)> highlights, IReadOnlyList<double> marks, int count)
        ScanDocumentForRegex(Regex regex)
    {
        var highlights = new List<(int, int)>();
        var marks      = new List<double>();
        var lineNums   = new List<int>();
        var text       = Document.Text;
        var totalLines = Math.Max(1, LineCount);
        var prevLine   = -1;

        foreach (Match m in regex.Matches(text))
        {
            highlights.Add((m.Index, m.Length));
            var line = CountLines(text[..m.Index]); // 1-based
            marks.Add((double)line / totalLines);
            if (line - 1 != prevLine) { lineNums.Add(line - 1); prevLine = line - 1; }
        }
        _matchingLineNumbers = lineNums.ToArray();
        return (highlights, marks, highlights.Count);
    }

    private (IReadOnlyList<(int, int)> highlights, IReadOnlyList<double> marks, int count)
        ScanDocumentConventional(string query)
    {
        var highlights = new List<(int, int)>();
        var marks      = new List<double>();
        var lineNums   = new List<int>();
        var text       = Document.Text;
        var totalLines = Math.Max(1, LineCount);
        var comparison = StringComparison.OrdinalIgnoreCase;
        var start      = 0;
        var prevLine   = -1;

        while (true)
        {
            var idx = text.IndexOf(query, start, comparison);
            if (idx < 0) break;
            highlights.Add((idx, query.Length));
            var line = CountLines(text[..idx]); // 1-based
            marks.Add((double)line / totalLines);
            if (line - 1 != prevLine) { lineNums.Add(line - 1); prevLine = line - 1; }
            start = idx + query.Length;
        }
        _matchingLineNumbers = lineNums.ToArray();
        return (highlights, marks, highlights.Count);
    }

    // Large-file streaming search
    // Strategy: stream the whole file for accurate minimap marks (line positions),
    // but compute highlights only against the loaded DocumentText (avoids line-ending
    // offset drift that occurs when reconstructing character offsets from a StreamReader).

    private async Task<(IReadOnlyList<(int, int)> highlights, IReadOnlyList<double> marks, int count)>
        ScanFileForRegexAsync(Regex regex, CancellationToken ct)
    {
        var matchingLines = new List<int>();
        long totalLines   = 0;

        using (var reader = new StreamReader(FilePath, SelectedEncoding.Encoding))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (regex.IsMatch(line)) matchingLines.Add((int)totalLines);
                totalLines++;
            }
        }

        totalLines = Math.Max(1, totalLines);
        var marks  = matchingLines.Select(n => (double)n / totalLines).ToList();

        // Pass 2: highlights from loaded text — offsets are exact within the sparse document.
        var highlights = new List<(int, int)>();
        foreach (Match m in regex.Matches(_loadedText))
            highlights.Add((m.Index, m.Length));

        _matchingLineNumbers = matchingLines.ToArray(); // only set on full completion (not if cancelled)
        return (highlights, marks, matchingLines.Count);
    }

    private async Task<(IReadOnlyList<(int, int)> highlights, IReadOnlyList<double> marks, int count)>
        ScanFileConventionalAsync(string query, CancellationToken ct)
    {
        var comparison    = StringComparison.OrdinalIgnoreCase;
        var matchingLines = new List<int>();
        long totalLines   = 0;

        using (var reader = new StreamReader(FilePath, SelectedEncoding.Encoding))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Contains(query, comparison)) matchingLines.Add((int)totalLines);
                totalLines++;
            }
        }

        totalLines = Math.Max(1, totalLines);
        var marks  = matchingLines.Select(n => (double)n / totalLines).ToList();

        var highlights = new List<(int, int)>();
        var start      = 0;
        while (true)
        {
            var idx = _loadedText.IndexOf(query, start, comparison);
            if (idx < 0) break;
            highlights.Add((idx, query.Length));
            start = idx + query.Length;
        }

        _matchingLineNumbers = matchingLines.ToArray();
        return (highlights, marks, matchingLines.Count);
    }

    [RelayCommand]
    private void CancelSearch()
    {
        _searchCts?.Cancel();
        SearchHighlights     = [];
        MiniMapMarks         = [];
        SearchMatchCount     = 0;
        IsSearchActive       = false;
        CurrentSearchTerm    = string.Empty;
        _lastSearch          = null;
        _currentMatchIndex   = -1;
        ScrollToOffset       = -1;
        _matchingLineNumbers = [];
        _activeRegex         = null;
        _activeSearchPattern = string.Empty;
    }

    // Set by code-behind on every caret move so navigation is relative to cursor position.
    public int CurrentCaretOffset { get; set; }

    [RelayCommand]
    private async Task FindNext()
    {
        if (_matchingLineNumbers.Length == 0) return;

        var caretLine = GetCaretLine();

        // First matching line strictly after the caret line; wrap to 0 if none.
        var idx = Array.BinarySearch(_matchingLineNumbers, caretLine + 1);
        if (idx < 0) idx = ~idx; // insertion point = first element > caretLine
        if (idx >= _matchingLineNumbers.Length) idx = 0;

        _currentMatchIndex = idx;
        await NavigateToMatchLineAsync(_matchingLineNumbers[idx]);
    }

    [RelayCommand]
    private async Task FindPrevious()
    {
        if (_matchingLineNumbers.Length == 0) return;

        var caretLine = GetCaretLine();

        // Last matching line strictly before the caret line; wrap to last if none.
        var idx = Array.BinarySearch(_matchingLineNumbers, caretLine);
        idx = idx >= 0 ? idx - 1 : ~idx - 1; // step behind the insertion/found point
        if (idx < 0) idx = _matchingLineNumbers.Length - 1;

        _currentMatchIndex = idx;
        await NavigateToMatchLineAsync(_matchingLineNumbers[idx]);
    }

    private int GetCaretLine()
    {
        try { return Document.GetLineByOffset(Math.Clamp(CurrentCaretOffset, 0, Document.TextLength)).LineNumber - 1; }
        catch { return 0; }
    }

    private async Task NavigateToMatchLineAsync(int lineNumber) // 0-based
    {
        if (lineNumber >= LoadedLineCount)
            await EnsureContentLoadedUpToLineAsync(lineNumber);

        var offset = FindMatchOffsetOnLine(lineNumber);
        if (offset >= 0)
            ScrollToOffset = offset;
    }

    private int FindMatchOffsetOnLine(int lineNumber) // 0-based
    {
        try
        {
            var docLine  = Document.GetLineByNumber(lineNumber + 1); // TextDocument is 1-based
            var lineText = Document.GetText(docLine.Offset, docLine.Length);

            if (_activeRegex is not null)
            {
                var m = _activeRegex.Match(lineText);
                return m.Success ? docLine.Offset + m.Index : docLine.Offset;
            }
            if (!string.IsNullOrEmpty(_activeSearchPattern))
            {
                var i = lineText.IndexOf(_activeSearchPattern, StringComparison.OrdinalIgnoreCase);
                return i >= 0 ? docLine.Offset + i : docLine.Offset;
            }
        }
        catch { }
        return -1;
    }

    // ── Clipboard (delegated to ApplicationCommands in code-behind) ───────────

    [RelayCommand] private static void Cut()   => System.Windows.Input.ApplicationCommands.Cut.Execute(null, null);
    [RelayCommand] private static void Copy()  => System.Windows.Input.ApplicationCommands.Copy.Execute(null, null);
    [RelayCommand] private static void Paste() => System.Windows.Input.ApplicationCommands.Paste.Execute(null, null);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var c in text)
            if (c == '\n') count++;
        return count;
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    // ── IPageViewModel ────────────────────────────────────────────────────────

    /// <summary>
    /// Set by the View after construction to give the ViewModel access to the
    /// currently visible text (a rendering-layer concept).
    /// </summary>
    internal Func<string> GetVisibleText { get; set; } = () => string.Empty;

    public string GetContext()
    {
        var fileName = FilePath is not null ? Path.GetFileName(FilePath) : "Untitled";
        var path     = FilePath ?? string.Empty;
        var visible  = GetVisibleText();
        return $"Text file: '{fileName}' at '{path}'\nVisible text:\n{visible}";
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "copy_visible_text",
            "Copy the text currently visible in the editor to the clipboard.",
            [],
            ToolSafety.ReadOnly,
            (arguments, ct) =>
            {
                Clipboard.SetText(GetVisibleText());
                return Task.FromResult(ToolResult.Ok("copied", "Copied the visible text to the clipboard."));
            }),
        new DelegateClientTool(
            "find",
            "Find text within the editor.",
            [new ClientToolParameter("search_term", "The text to search for.")],
            ToolSafety.ReadOnly,
            (arguments, ct) =>
            {
                var term = arguments["search_term"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(term))
                    return Task.FromResult(ToolResult.Error("No search term provided."));

                _ = SearchConventionalAsync(term);
                return Task.FromResult(ToolResult.Ok($"finding {term}", $"Searching for '{term}'."));
            })
    ];

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
        _watcher?.Dispose();
        _searchCts?.Cancel();
        _fileStream?.Dispose();
    }
}
