using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Text.ClientTools;
using Nexaflow.Features.Text.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Controls;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Nexaflow.Features.Text.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}

public sealed record SplitModeOption(SplitMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class TextViewModel : ObservableObject, IDisposable, IPageViewModel, IContextPreview
{
    private const long SmallFileSizeLimit = 100 * 1024; // 100 KB
    private const int  LinesPerPage       = 2000;        // page granularity for the line index
    private const int  WindowLines        = 4000;        // real lines kept resident in the editor
    private const int  SlideMargin        = 800;         // slide when the viewport nears a window edge

    // ── File info ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private string _fileName    = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int    _lineCount;
    [ObservableProperty] private bool   _isLargeFile;

    // ── Document (owned here; code-behind sets Editor.Document = this.Document) ──

    public TextDocument Document { get; } = new TextDocument();

    // ── Editing ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isEditing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty] private bool _isBusy; // a streaming save is running — editor is held read-only

    /// <summary>Raised whenever <see cref="IsDirty"/> changes, so the page registration can mark the tab.</summary>
    public event Action<bool>? DirtyChanged;

    /// <summary>True when this file can be edited: any small file, or a large file on a real (non-archive) path.</summary>
    public bool CanEdit => !IsLargeFile || VirtualFileSystem.Instance.SplitOutermostContainer(FilePath).Inner is null;

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

    // ── File splitting (the raw viewer turns a too-large file into editable chunks) ──

    [ObservableProperty] private bool             _isSplitPanelOpen;
    [ObservableProperty] private SplitModeOption  _selectedSplitMode;
    [ObservableProperty] private string           _splitValue     = "1000";
    [ObservableProperty] private string           _splitValueHint = "Lines per file";

    public IReadOnlyList<SplitModeOption> SplitModes { get; } =
    [
        new(SplitMode.ByLineCount, "By line count"),
        new(SplitMode.BySize,      "By size (MB)"),
        new(SplitMode.ByRegex,     "At lines matching regex"),
    ];

    // ── Search state ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private bool   _isSearchRunning;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _fileChangedMessage    = string.Empty;
    [ObservableProperty] private bool   _fileChangedBannerVisible;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    // -1 means "not yet navigated"; code-behind watches this to trigger centering
    [ObservableProperty] private int _scrollToOffset = -1;

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

    // ── Large-file windowing ────────────────────────────────────────────────────

    private OverlayTextFile? _file;        // the overlay engine (windowed reads + edits + save)
    private long   _winStartLine;          // first real line in the resident window
    private long   _winStartByte;          // current byte offset of the window start
    private string _winText = string.Empty;// exact decoded text of the window (mirrors Document's real region)
    private int    _winDocOffset;          // Document offset where the window's real text begins
    private bool   _suppressDocChanges;    // guards programmatic Document mutations from the edit pipeline
    private bool   _slidingWindow;         // re-entrancy guard for slides
    private string AddBufferPath => Path.Combine(
        Path.GetDirectoryName(FilePath) ?? string.Empty, "." + FileName + ".nexedit");

    // ── Monitoring ────────────────────────────────────────────────────────────

    private IFileWatch?             _watch;
    private readonly IShellServices _shell;

    // ── Search cancellation ───────────────────────────────────────────────────

    private CancellationTokenSource? _searchCts;
    private Func<CancellationToken, Task>? _lastSearch;

    // All line numbers (0-based) across the full file that contain at least one match.
    private long[]  _matchingLineNumbers  = [];
    private Regex? _activeRegex;
    private string _activeSearchPattern  = string.Empty;
    private int    _currentMatchIndex     = -1;

    // ─────────────────────────────────────────────────────────────────────────

    public TextViewModel(string filePath, IShellServices shell)
    {
        _shell = shell;
        _selectedEncoding  = AvailableEncodings[0]; // UTF-8
        _selectedSplitMode = SplitModes[0];
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    partial void OnIsDirtyChanged(bool value) => DirtyChanged?.Invoke(value);

    // ── Loading ───────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        try
        {
            long length  = VirtualFileSystem.Instance.GetLength(FilePath);
            FileSizeText = FormatSize(length);
            IsLargeFile  = length >= SmallFileSizeLimit;
            OnPropertyChanged(nameof(CanEdit));

            if (!IsLargeFile)
            {
                using var smallStream = VirtualFileSystem.Instance.OpenRead(FilePath);
                using var smallReader = new StreamReader(smallStream, SelectedEncoding.Encoding);
                _suppressDocChanges = true;
                Document.Text = await smallReader.ReadToEndAsync(ct);
                _suppressDocChanges = false;
                LineCount     = Document.LineCount;
            }
            else
            {
                _file?.Dispose();
                _file = await OverlayTextFile.OpenAsync(FilePath, SelectedEncoding.Encoding, LinesPerPage, AddBufferPath, ct);
                LineCount = (int)Math.Min(int.MaxValue, _file.TotalLines);
                _winStartLine = 0;
                await SlideWindowAsync(0);
            }

            if (IsMonitoring) StartMonitoring();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _suppressDocChanges = true;
            Document.Text = $"Error loading file: {ex.Message}";
            _suppressDocChanges = false;
        }
    }

    // ── Sliding window (large files) ─────────────────────────────────────────────

    // Called by code-behind when the viewport's visible lines move. Slides the resident window if the
    // viewport nears (or passes) a window edge, keeping only the window's real text in the Document.
    public async Task EnsureWindowAsync(int topVisibleLine, int bottomVisibleLine)
    {
        if (!IsLargeFile || _file is null || _slidingWindow) return;

        long winEnd = _winStartLine + CountNewlines(_winText);
        bool nearTop    = _winStartLine > 0 && topVisibleLine - _winStartLine < SlideMargin;
        bool nearBottom = winEnd < _file.TotalLines && winEnd - bottomVisibleLine < SlideMargin;
        bool outside    = topVisibleLine < _winStartLine || bottomVisibleLine > winEnd;
        if (!nearTop && !nearBottom && !outside) return;

        long desiredStart = Math.Max(0, topVisibleLine - SlideMargin);
        await SlideWindowAsync(desiredStart);
    }

    private async Task SlideWindowAsync(long desiredStart)
    {
        if (_file is null) return;
        _slidingWindow = true;
        try
        {
            long start = Math.Max(0, Math.Min(desiredStart, Math.Max(0, _file.TotalLines - 1)));
            var win = await _file.ReadWindowAsync(start, WindowLines, CancellationToken.None);
            _winStartLine = win.StartLine;
            _winStartByte = win.StartByteOffset;
            _winText      = win.Text;
            LineCount     = (int)Math.Min(int.MaxValue, _file.TotalLines);
            ComposeDocument();
            RefreshWindowHighlights();
        }
        finally { _slidingWindow = false; }
    }

    // Builds the full Document text = top placeholders + window real text + bottom placeholders, keeping
    // Document.LineCount == TotalLines so the scrollbar coordinate space spans the whole file.
    private void ComposeDocument()
    {
        long total = _file?.TotalLines ?? 1;
        long nwin  = CountNewlines(_winText);
        long bottom = (total - 1) - _winStartLine - nwin;

        var sb = new StringBuilder();
        if (_winStartLine > 0) sb.Append('\n', checked((int)_winStartLine));
        _winDocOffset = (int)_winStartLine;
        sb.Append(_winText);
        if (bottom > 0)
        {
            if (_winText.Length > 0 && _winText[^1] != '\n') { sb.Append('\n'); bottom--; }
            if (bottom > 0) sb.Append('\n', checked((int)bottom));
        }

        _suppressDocChanges = true;
        Document.Text = sb.ToString();
        _suppressDocChanges = false;
    }

    /// <summary>Document offset where the editable (real, resident) text begins.</summary>
    public int LoadedRealStart => IsLargeFile ? _winDocOffset : 0;

    /// <summary>Document offset just past the editable text (placeholders begin here).</summary>
    public int LoadedRealEnd => IsLargeFile ? _winDocOffset + _winText.Length : Document.TextLength;

    // ── User edits (forwarded from the view's Document.Changed) ───────────────────

    public void OnUserEdit(int docOffset, int removalLength, string insertedText)
    {
        if (_suppressDocChanges || !IsEditing) return;
        IsDirty = true;

        if (!IsLargeFile || _file is null) return; // small files persist the whole Document on save

        int inWin = docOffset - _winDocOffset;
        if (inWin < 0 || inWin > _winText.Length) return; // outside the resident window (guarded read-only)

        string removed = removalLength > 0 && inWin + removalLength <= _winText.Length
            ? _winText.Substring(inWin, removalLength)
            : string.Empty;

        long curByte      = _winStartByte + SelectedEncoding.Encoding.GetByteCount(_winText.AsSpan(0, inWin));
        long bytesRemoved = SelectedEncoding.Encoding.GetByteCount(removed);
        _file.ApplyEdit(curByte, bytesRemoved, insertedText);

        // Keep the window shadow in sync with the Document the user just mutated.
        _winText = string.Concat(_winText.AsSpan(0, inWin), insertedText, _winText.AsSpan(inWin + removed.Length));
        LineCount = (int)Math.Min(int.MaxValue, _file.TotalLines);
    }

    // ── Editing toggle ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleEditing()
    {
        if (IsEditing)
        {
            if (IsDirty)
            {
                var discard = await _shell.ConfirmAsync("Discard changes?",
                    "You have unsaved edits. Discard them and stop editing?");
                if (!discard) return;
                await DiscardAsync();
            }
            IsEditing = false;
            if (_watch is not null) _watch.Enabled = IsMonitoring;
            return;
        }

        if (!CanEdit)
        {
            _shell.ShowError(IsLargeFile
                ? "Large files inside archives can't be edited in place — extract or split first."
                : "This file can't be edited.");
            return;
        }

        if (_watch is not null) _watch.Enabled = false; // hold reloads while editing
        IsEditing = true;
    }

    private async Task DiscardAsync()
    {
        IsDirty = false;
        await ReloadAsync(); // rebuild the overlay/window from the on-disk original
    }

    // ── Save ─────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task Save()
    {
        if (!IsDirty) return;
        try
        {
            IsBusy = true;
            if (_watch is not null) _watch.Enabled = false;

            if (!IsLargeFile || _file is null)
            {
                VirtualFileSystem.Instance.WriteAllText(FilePath, Document.Text, SelectedEncoding.Encoding);
                IsDirty = false;
            }
            else
            {
                var tmp = FilePath + ".nexsave.tmp";
                await using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    await _file.SaveAsync(outStream, CancellationToken.None);

                // Release the original's file handle so the atomic replace can swap it in.
                long keepLine = _winStartLine;
                _file.Dispose();
                _file = null;

                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);

                IsDirty = false;

                // Re-open the overlay over the saved file (the add-buffer is consumed) but DON'T rebuild the
                // document — the on-screen content is identical to what was just saved, so leaving it as-is
                // keeps the viewport exactly where the user was.
                _file = await OverlayTextFile.OpenAsync(FilePath, SelectedEncoding.Encoding, LinesPerPage, AddBufferPath, CancellationToken.None);
                LineCount = (int)Math.Min(int.MaxValue, _file.TotalLines);
                var w = await _file.ReadWindowAsync(keepLine, 0, CancellationToken.None); // just to recompute the window's byte offset
                _winStartByte = w.StartByteOffset;
            }

            _shell.ShowNotification($"Saved {FileName}.");
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Could not save '{FileName}': {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            if (_watch is not null) _watch.Enabled = IsMonitoring;
        }
    }

    // ── Reload (encoding change / monitor / post-save) ────────────────────────────

    private async Task ReloadAsync()
    {
        _searchCts?.Cancel();
        _file?.Dispose();
        _file = null;
        _winText = string.Empty;
        _winStartLine = 0;
        _winStartByte = 0;
        _suppressDocChanges = true;
        Document.Text = string.Empty;
        _suppressDocChanges = false;
        await LoadAsync(CancellationToken.None);
        if (IsSearchActive) await ReRunSearchAsync();
    }

    partial void OnSelectedEncodingChanged(EncodingOption value)
    {
        if (IsEditing) return; // encoding is locked while editing (combo is disabled in the view)
        _ = ReloadAsync();
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        _watch?.Dispose();
        if (!VirtualFileSystem.Instance.Exists(FilePath)) return;
        _watch = _shell.WatchFile(FilePath, OnFileChanged);
        if (IsEditing || IsBusy) _watch.Enabled = false;
    }

    private async void OnFileChanged()
    {
        if (IsEditing || IsBusy) return; // our own writes / an active edit session — ignore
        FileChangedMessage         = "File changed on disk — reloading…";
        FileChangedBannerVisible   = true;
        await ReloadAsync();
        await Task.Delay(3000);
        FileChangedBannerVisible = false;
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        if (IsMonitoring)
            StartMonitoring();
        else
        {
            _watch?.Dispose();
            _watch = null;
        }
    }

    // ── File splitting ──────────────────────────────────────────────────────────

    partial void OnSelectedSplitModeChanged(SplitModeOption value)
        => (SplitValueHint, SplitValue) = value.Mode switch
        {
            SplitMode.BySize  => ("Megabytes per file",            "10"),
            SplitMode.ByRegex => ("Regex — new file at each match", "^"),
            _                 => ("Lines per file",                "1000"),
        };

    [RelayCommand]
    private void ToggleSplitPanel() => IsSplitPanelOpen = !IsSplitPanelOpen;

    [RelayCommand]
    private void Split()
    {
        if (string.IsNullOrEmpty(FilePath) || !VirtualFileSystem.Instance.Exists(FilePath)) return;

        SplitOptions options;
        var mode = SelectedSplitMode.Mode;
        switch (mode)
        {
            case SplitMode.BySize:
                if (!double.TryParse(SplitValue, out var mb) || mb <= 0)
                { _shell.ShowError("Enter a positive size in MB."); return; }
                options = new SplitOptions { Mode = mode, MaxBytesPerPart = (long)(mb * 1024 * 1024) };
                break;
            case SplitMode.ByLineCount:
                if (!int.TryParse(SplitValue, out var lines) || lines <= 0)
                { _shell.ShowError("Enter a positive line count."); return; }
                options = new SplitOptions { Mode = mode, LinesPerPart = lines };
                break;
            default:
                if (string.IsNullOrWhiteSpace(SplitValue))
                { _shell.ShowError("Enter a regex pattern."); return; }
                try { _ = new Regex(SplitValue); }
                catch { _shell.ShowError("Invalid regex pattern."); return; }
                options = new SplitOptions { Mode = mode, BoundaryPattern = SplitValue };
                break;
        }

        IsSplitPanelOpen = false;
        var task = new SplitFileBackgroundTask(FilePath, options);
        _shell.QueueBackgroundTask(task, ok =>
        {
            if (ok && task.Result is { OutputFiles.Count: > 0 } r)
                _shell.ShowNotification($"Split '{FileName}' into {r.OutputFiles.Count} file(s).");
            else if (ok)
                _shell.ShowNotification($"'{FileName}' produced no output (empty file).");
        });
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

            _activeRegex         = regex;
            _activeSearchPattern = string.Empty;
            var (lines, count)   = await ScanAsync(regex, null, ct);
            _matchingLineNumbers = lines;

            CurrentSearchTerm  = $"/{pattern}/";
            MiniMapMarks       = MarksFor(lines);
            SearchMatchCount   = count;
            IsSearchActive     = count > 0;
            _currentMatchIndex = count > 0 ? 0 : -1;
            RefreshWindowHighlights();
            if (count > 0) await NavigateToMatchLineAsync(lines[0]);
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
            _activeRegex         = null;
            _activeSearchPattern = query;
            var (lines, count)   = await ScanAsync(null, query, ct);
            _matchingLineNumbers = lines;

            CurrentSearchTerm  = query;
            MiniMapMarks       = MarksFor(lines);
            SearchMatchCount   = count;
            IsSearchActive     = count > 0;
            _currentMatchIndex = count > 0 ? 0 : -1;
            RefreshWindowHighlights();
            if (count > 0) await NavigateToMatchLineAsync(lines[0]);
        }
        catch (OperationCanceledException) { }
        finally { IsSearchRunning = false; }
    }

    // Scans the CURRENT content (overlay-aware for large files) for matching line numbers + total count.
    private async Task<(long[] lines, int count)> ScanAsync(Regex? regex, string? query, CancellationToken ct)
    {
        var matchingLines = new List<long>();
        var total         = 0;

        if (IsLargeFile && _file is not null)
        {
            await foreach (var (line, text, _) in _file.EnumerateLinesAsync(ct))
            {
                bool hit = regex is not null ? regex.IsMatch(text)
                    : !string.IsNullOrEmpty(query) && text.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (hit) { matchingLines.Add(line); total++; }
            }
        }
        else
        {
            var docLines = Document.Text.Split('\n');
            for (var i = 0; i < docLines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var text = docLines[i];
                bool hit = regex is not null ? regex.IsMatch(text)
                    : !string.IsNullOrEmpty(query) && text.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (hit) { matchingLines.Add(i); total++; }
            }
        }
        return (matchingLines.ToArray(), total);
    }

    private IReadOnlyList<double> MarksFor(long[] lines)
    {
        long total = Math.Max(1, LineCount);
        var marks  = new List<double>(lines.Length);
        foreach (var l in lines) marks.Add((double)l / total);
        return marks;
    }

    // Computes search highlights for the resident window only (Document offsets), called after a search
    // and after every slide. Highlights outside the window aren't drawn (those lines are placeholders).
    private void RefreshWindowHighlights()
    {
        if (!IsSearchActive) { SearchHighlights = []; return; }

        var text    = IsLargeFile ? _winText : Document.Text;
        var baseOff = IsLargeFile ? _winDocOffset : 0;
        var result  = new List<(int, int)>();

        if (_activeRegex is not null)
        {
            foreach (Match m in _activeRegex.Matches(text))
                result.Add((baseOff + m.Index, m.Length));
        }
        else if (!string.IsNullOrEmpty(_activeSearchPattern))
        {
            var cmp = StringComparison.OrdinalIgnoreCase;
            var start = 0;
            while (true)
            {
                var i = text.IndexOf(_activeSearchPattern, start, cmp);
                if (i < 0) break;
                result.Add((baseOff + i, _activeSearchPattern.Length));
                start = i + _activeSearchPattern.Length;
            }
        }
        SearchHighlights = result;
    }

    private Task ReRunSearchAsync() => _lastSearch?.Invoke(CancellationToken.None) ?? Task.CompletedTask;

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

    public int CurrentCaretOffset { get; set; }

    [RelayCommand]
    private async Task FindNext()
    {
        if (_matchingLineNumbers.Length == 0) return;
        var caretLine = GetCaretLine();
        var idx = LowerBound(_matchingLineNumbers, caretLine + 1);
        if (idx >= _matchingLineNumbers.Length) idx = 0;
        _currentMatchIndex = idx;
        await NavigateToMatchLineAsync(_matchingLineNumbers[idx]);
    }

    [RelayCommand]
    private async Task FindPrevious()
    {
        if (_matchingLineNumbers.Length == 0) return;
        var caretLine = GetCaretLine();
        var idx = LowerBound(_matchingLineNumbers, caretLine) - 1;
        if (idx < 0) idx = _matchingLineNumbers.Length - 1;
        _currentMatchIndex = idx;
        await NavigateToMatchLineAsync(_matchingLineNumbers[idx]);
    }

    // First index whose value is >= target.
    private static int LowerBound(long[] arr, long target)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (arr[mid] < target) lo = mid + 1; else hi = mid; }
        return lo;
    }

    private long GetCaretLine()
    {
        try
        {
            return Document.GetLineByOffset(Math.Clamp(CurrentCaretOffset, 0, Document.TextLength)).LineNumber - 1;
        }
        catch { return 0; }
    }

    private async Task NavigateToMatchLineAsync(long lineNumber) // 0-based file line
    {
        if (IsLargeFile && _file is not null)
        {
            long winEnd = _winStartLine + CountNewlines(_winText);
            if (lineNumber < _winStartLine || lineNumber >= winEnd)
                await SlideWindowAsync(Math.Max(0, lineNumber - SlideMargin));
        }

        var offset = FindMatchOffsetOnLine(lineNumber);
        if (offset >= 0) ScrollToOffset = offset;
    }

    private int FindMatchOffsetOnLine(long lineNumber) // 0-based
    {
        try
        {
            var docLine  = Document.GetLineByNumber((int)lineNumber + 1);
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

    private static long CountNewlines(string text)
    {
        long n = 0;
        foreach (var c in text) if (c == '\n') n++;
        return n;
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    // ── AI surface helpers (used by the client tools) ────────────────────────────

    internal IShellServices Shell => _shell;
    internal OverlayTextFile? Engine => _file;

    /// <summary>Reads up to <paramref name="count"/> lines starting at <paramref name="startLine"/>
    /// (1-based), overlay-aware, each prefixed with its 1-based line number.</summary>
    internal async Task<string> ReadNumberedLinesAsync(int startLine, int count, CancellationToken ct)
    {
        startLine = Math.Max(1, startLine);
        if (IsLargeFile && _file is not null)
        {
            var win = await _file.ReadWindowAsync(startLine - 1, count, ct);
            var sb = new StringBuilder();
            var lines = win.Text.Split('\n');
            int n = startLine;
            for (int i = 0; i < lines.Length && i < count; i++)
                sb.Append(n++).Append('\t').Append(lines[i].TrimEnd('\r')).Append('\n');
            return sb.ToString();
        }

        return await _shell.RunOnUiAsync(() =>
        {
            var sb = new StringBuilder();
            var lines = Document.Text.Split('\n');
            for (int i = startLine - 1; i < lines.Length && i < startLine - 1 + count; i++)
                sb.Append(i + 1).Append('\t').Append(lines[i].TrimEnd('\r')).Append('\n');
            return Task.FromResult(sb.ToString());
        });
    }

    /// <summary>Finds <paramref name="query"/> over current content (overlay-aware), returns a numbered
    /// summary for the AI, and drives the on-screen search so the user sees the same hits.</summary>
    internal async Task<string> ToolFindAsync(string query, bool regex, bool caseSensitive, int max, CancellationToken ct)
    {
        // Drive the visible search (highlights + minimap) on the UI thread (fire-and-forget the highlight).
        await _shell.RunOnUiAsync(() => { if (regex) _ = SearchRegexAsync(query); else _ = SearchConventionalAsync(query); });

        var sb = new StringBuilder();
        int shown = 0;
        if (IsLargeFile && _file is not null)
        {
            var matches = await _file.FindAsync(query, regex, caseSensitive, 0, _file.TotalLines, max, ct);
            foreach (var m in matches)
            {
                sb.Append("line ").Append(m.Line + 1).Append(": ").Append(m.Preview).Append('\n');
                shown++;
            }
        }
        else
        {
            var (lines, _) = await ScanAsync(regex ? SafeRegex(query, caseSensitive) : null, regex ? null : query, ct);
            foreach (var l in lines)
            {
                if (shown >= max) break;
                var text = await _shell.RunOnUiAsync(() => Task.FromResult(LineText((int)l)));
                sb.Append("line ").Append(l + 1).Append(": ").Append(text).Append('\n');
                shown++;
            }
        }
        return shown == 0 ? "No matches." : $"{SearchMatchCount} match(es). Showing {shown}:\n{sb}";

        static Regex? SafeRegex(string p, bool cs) { try { return new Regex(p, cs ? RegexOptions.None : RegexOptions.IgnoreCase); } catch { return null; } }
    }

    private string LineText(int line0)
    {
        try { var l = Document.GetLineByNumber(line0 + 1); return Document.GetText(l.Offset, l.Length); }
        catch { return string.Empty; }
    }

    /// <summary>Ensures an edit session is active so a (permission-approved) AI edit can be applied.</summary>
    internal void EnsureEditing()
    {
        if (IsEditing) return;
        if (_watch is not null) _watch.Enabled = false;
        IsEditing = true;
    }

    /// <summary>Replaces lines [startLine, endLine] (0-based, inclusive) with <paramref name="newText"/>,
    /// refreshing the viewport + dirty state on the UI thread. Works for small and large files.</summary>
    internal async Task ApplyAiLineEditAsync(long startLine, long endLine, string newText, CancellationToken ct)
    {
        if (IsLargeFile && _file is not null)
        {
            await _file.ReplaceLinesAsync(startLine, endLine, newText, ct);
            await AfterAiEditAsync();
            return;
        }

        await _shell.RunOnUiAsync(() =>
        {
            EnsureEditing();
            int s = Math.Clamp((int)startLine + 1, 1, Document.LineCount);
            int e = Math.Clamp((int)endLine + 1, s, Document.LineCount);
            var startOff = Document.GetLineByNumber(s).Offset;
            var endObj   = Document.GetLineByNumber(e);
            var endOff   = Math.Min(endObj.Offset + endObj.TotalLength, Document.TextLength);
            Document.Replace(startOff, endOff - startOff, newText);
            IsDirty = true;
        });
    }

    internal async Task<int> ApplyAiReplaceAsync(string pattern, string replacement, bool regex, bool caseSensitive,
        long fromLine, long toLine, CancellationToken ct)
    {
        if (IsLargeFile && _file is not null)
        {
            int n = await _file.ReplaceAsync(pattern, replacement, regex, caseSensitive, fromLine, toLine, ct);
            if (n > 0) await AfterAiEditAsync();
            return n;
        }

        int hits = 0;
        await _shell.RunOnUiAsync(() =>
        {
            EnsureEditing();
            var rx  = regex ? new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
            var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var lines = Document.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i < fromLine || i > toLine) continue;
                if (rx is not null)
                {
                    int c = rx.Matches(lines[i]).Count;
                    if (c > 0) { lines[i] = rx.Replace(lines[i], replacement); hits += c; }
                }
                else lines[i] = ReplaceAllPlain(lines[i], pattern, replacement, cmp, ref hits);
            }
            if (hits > 0) { Document.Text = string.Join('\n', lines); IsDirty = true; }
        });
        return hits;
    }

    private static string ReplaceAllPlain(string text, string find, string repl, StringComparison cmp, ref int hits)
    {
        if (string.IsNullOrEmpty(find)) return text;
        var sb = new StringBuilder();
        int idx = 0, prev = 0, n = 0;
        while ((idx = text.IndexOf(find, idx, cmp)) >= 0) { sb.Append(text, prev, idx - prev).Append(repl); idx += find.Length; prev = idx; n++; }
        if (n == 0) return text;
        sb.Append(text, prev, text.Length - prev);
        hits += n;
        return sb.ToString();
    }

    private async Task AfterAiEditAsync()
    {
        IsDirty = true;
        // re-read the resident window on the UI thread so the edit shows — await completion (Func<Task<T>> overload)
        await _shell.RunOnUiAsync(async () => { await SlideWindowAsync(_winStartLine); return true; });
    }

    internal async Task SaveFromToolAsync()
    {
        if (SaveCommand.CanExecute(null))
            await _shell.RunOnUiAsync(() => SaveCommand.ExecuteAsync(null));
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    internal Func<string> GetVisibleText { get; set; } = () => string.Empty;
    internal Func<int>    GetFirstVisibleLine { get; set; } = () => 1;

    public string GetContext()
    {
        var fileName = Path.GetFileName(FilePath);
        var dirty    = IsDirty ? " (unsaved edits)" : IsEditing ? " (editing)" : string.Empty;
        var visible  = GetVisibleText();
        var first    = GetFirstVisibleLine();

        var numbered = new StringBuilder();
        var lines    = visible.Replace("\r", string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
            numbered.Append(first + i).Append('\t').Append(lines[i]).Append('\n');

        return $"Text file: '{fileName}' at '{FilePath}'{dirty}\n" +
               $"Encoding: {SelectedEncoding.Name}. Total lines: {LineCount}.\n" +
               $"Showing lines {first}-{first + Math.Max(0, lines.Length - 1)} of {LineCount}:\n{numbered}";
    }

    public IReadOnlyList<IClientTool> GetClientTools() => TextEditorTools.For(this);

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

    /// <summary>The file this page's tools act within — disambiguates two Text tabs on different files when
    /// both are pinned into one conversation (so their identically-named tools don't collapse first-wins).</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(FilePath) ? null : FilePath;

    /// <summary>A compact, read-only preview for the conversation's context panel: the file name, a meta line,
    /// and a capped snippet of the resident text. Built fresh each time — it never re-hosts the live editor.</summary>
    public System.Windows.Controls.UserControl CreateContextPreview()
    {
        var meta = $"{LineCount:N0} line{(LineCount == 1 ? "" : "s")} · {SelectedEncoding.Name}" +
                   (IsDirty ? " · unsaved edits" : IsEditing ? " · editing" : string.Empty);
        const int cap = 8000;
        var text = Document.Text;
        var body = text.Length > cap ? text[..cap] + "\n… (preview truncated)" : text;
        return new ReadOnlyTextPreview(string.IsNullOrEmpty(FileName) ? "Text" : FileName, meta, body);
    }

    public string? GetAiSystemPromptGuidance() =>
        "This is a text file viewer/editor. Use read_lines to page through the file, find_text to search " +
        "(results reflect unsaved edits). To change the file, use edit_lines, replace_in_range or " +
        "replace_all (these require approval), then save_file to persist.";

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _watch?.Dispose();
        _searchCts?.Cancel();
        _file?.Dispose();
        try { if (File.Exists(FilePath + ".nexsave.tmp")) File.Delete(FilePath + ".nexsave.tmp"); } catch { }
    }
}
