using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.Text.ClientTools;
using Nexaflow.Features.Text.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Controls;
using System.Globalization;
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

public sealed partial class TextViewModel : ObservableObject, IDisposable, IPageViewModel, IContextPreview, ISearchable
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

    // ── Zoom (editor font scale) ─────────────────────────────────────────────────

    [ObservableProperty] private int _zoomPercent = 100;
    public IReadOnlyList<int> ZoomPresets { get; } = [80, 90, 100, 110, 120, 130];

    partial void OnZoomPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 50, 400);
        if (clamped != value) ZoomPercent = clamped; // re-enters with the clamped value; the view reads it
    }

    [RelayCommand] private void ZoomIn()    => ZoomPercent = Math.Min(400, ZoomPercent + 10);
    [RelayCommand] private void ZoomOut()   => ZoomPercent = Math.Max(50,  ZoomPercent - 10);
    [RelayCommand] private void ResetZoom() => ZoomPercent = 100;

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
    private bool _disposed;
    private CancellationTokenSource? _findDebounceCts;

    /// <summary>Supplies the editor's current selection (single-line) so opening the bar can seed FindText.</summary>
    internal Func<string?>? GetEditorSelection { get; set; }

    /// <summary>Raised when the bar opens so the view can focus the find box.</summary>
    public event Action? FindBarFocusRequested;

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

    // The active query — the ONE authority for what matches, so the text viewer honours whole-word,
    // wildcards and quoting exactly like every other ISearchable page (a literal "needle" is the word, not
    // a substring of "needless"). Line scanning, highlight spans and match navigation all read this.
    private SearchRequest? _activeRequest;

    // Kept only for regex-mode Replace's $1 expansion; null for a literal/wildcard query.
    private Regex? _activeRegex;
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
                Document.UndoStack.ClearAll(); // the initial load is the baseline, not an undoable edit
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

        // Where the caret logically is, as a FILE line + column. Replacing Document.Text below resets the
        // editor caret, and the view echoes that reset straight back into CurrentCaretOffset — which moved
        // the user's cursor whenever the window slid, and left Find Next/Previous stepping from the window
        // start instead of from the caret (so they only ever cycled the matches in the resident window).
        var (caretLine, caretColumn) = CaretLineAndColumn();

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
        // Placeholder composition is not a user edit — drop it from the undo stack so Ctrl+Z can't revert
        // it. (For a large file being edited, a window slide therefore resets undo history — safe, never
        // corrupts; small-file editing keeps the whole session's history since it never re-composes.)
        Document.UndoStack.ClearAll();

        RestoreCaret(caretLine, caretColumn);
    }

    /// <summary>The caret's FILE line (0-based) and its column within that line. Document lines are file
    /// lines (the window's real text is padded to its file position), so this needs no window arithmetic.</summary>
    private (long Line, int Column) CaretLineAndColumn()
    {
        try
        {
            var offset = Math.Clamp(CurrentCaretOffset, 0, Document.TextLength);
            var line   = Document.GetLineByOffset(offset);
            return (line.LineNumber - 1, offset - line.Offset);
        }
        catch { return (0, 0); }
    }

    /// <summary>Puts the caret back on the same file line after a recomposition, and tells the view to move
    /// the editor caret there — without scrolling, so a window slide never yanks the view.</summary>
    private void RestoreCaret(long fileLine, int column)
    {
        try
        {
            var docLineNo = (int)Math.Clamp(fileLine + 1, 1, Math.Max(1, Document.LineCount));
            var line      = Document.GetLineByNumber(docLineNo);
            var offset    = line.Offset + Math.Clamp(column, 0, line.Length);

            CurrentCaretOffset = offset;
            CaretRestoreRequested?.Invoke(offset);
        }
        catch { }
    }

    /// <summary>Raised after a window slide so the view can put the editor caret back where it logically
    /// was. Caret only — never scrolls.</summary>
    public event Action<int>? CaretRestoreRequested;

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
    /// Runs a parsed query against the current content and drives the on-screen search: the match count, the
    /// minimap ticks, the in-window highlights and navigation to the first match. The find bar's text and
    /// toggles are set by the caller (a "?" injection or a driver), not here, so live typing isn't fought.
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

        IsFindBarOpen     = true;              // opens even for a large streaming scan, or a no-match search
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
            RefreshWindowHighlights();
            if (count > 0) await NavigateToMatchLineAsync(lines[0]);
        }
        catch (OperationCanceledException) { }
        finally { IsSearchRunning = false; }
    }

    // Scans the CURRENT content (overlay-aware for large files) for matching line numbers + total count.
    // Matching is the request's own — whole-word literals, wildcards, quoted phrases and regex all decided
    // in one place (SearchTerm), so the text viewer can't drift from the rest of the app.
    private async Task<(long[] lines, int count)> ScanAsync(
        SearchRequest request, CancellationToken ct,
        List<SearchHit>? collect = null, int collectCap = 0)
    {
        var matchingLines = new List<long>();
        var total         = 0;

        if (IsLargeFile && _file is not null)
        {
            await foreach (var (line, text, _) in _file.EnumerateLinesAsync(ct))
                if (request.Matches(text)) { matchingLines.Add(line); total++; Collect(line, text); }
        }
        else
        {
            var docLines = Document.Text.Split('\n');
            for (var i = 0; i < docLines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (request.Matches(docLines[i])) { matchingLines.Add(i); total++; Collect(i, docLines[i]); }
            }
        }
        return (matchingLines.ToArray(), total);

        // Previews are taken from the scan itself, so a large file (whose Document holds only the
        // resident window) still yields real line text rather than a placeholder.
        void Collect(long line, string text)
        {
            if (collect is null || collect.Count >= collectCap) return;
            collect.Add(new SearchHit(line.ToString(CultureInfo.InvariantCulture), $"line {line + 1}", text.TrimEnd('\r')));
        }
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName) ? "the open text file" : $"the open text file '{FileName}'";

    /// <summary>Mirrors the old conventional-search handler's curve: a term or two is almost certainly a
    /// search; four is borderline. Past that <see cref="SearchScoring.LooksLikeProse"/> has already bowed out.</summary>
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
            // Agent-side read: scan without touching highlights, the minimap or the find bar.
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
    /// Narrows the match set to the agent's chosen lines — the minimap, match count and next/previous
    /// navigation all follow it. In-window highlighting still paints every occurrence of the pattern,
    /// since it is drawn from the pattern rather than the match list.
    /// </summary>
    public async Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var lines = hits.Select(h => long.TryParse(h.Id, out var l) ? l : -1)
                        .Where(l => l >= 0)
                        .Distinct()
                        .OrderBy(l => l)
                        .ToArray();
        if (lines.Length == 0) return false;

        _matchingLineNumbers = lines;
        MiniMapMarks         = MarksFor(lines);
        SearchMatchCount     = lines.Length;
        IsSearchActive       = true;
        _currentMatchIndex   = 0;
        RefreshWindowHighlights();
        await NavigateToMatchLineAsync(lines[0]);
        return true;
    }

    // The current match lines as hits, previewed from the resident document.
    private IReadOnlyList<SearchHit> VisibleHits()
    {
        var hits = new List<SearchHit>(Math.Min(_matchingLineNumbers.Length, SearchHitCap));
        foreach (var l in _matchingLineNumbers)
        {
            if (hits.Count >= SearchHitCap) break;
            var preview = IsLargeFile ? null : LineText((int)l);
            hits.Add(new SearchHit(l.ToString(CultureInfo.InvariantCulture), $"line {l + 1}", preview));
        }
        return hits;
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
    // Spans come from the request itself, so what is painted can't drift from what was counted.
    private void RefreshWindowHighlights()
    {
        if (!IsSearchActive || _activeRequest is null) { SearchHighlights = []; return; }

        var text    = IsLargeFile ? _winText : Document.Text;
        var baseOff = IsLargeFile ? _winDocOffset : 0;
        var result  = new List<(int, int)>();
        foreach (var (index, length) in _activeRequest.Occurrences(text))
            result.Add((baseOff + index, length));
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
        _activeRequest       = null;
        _activeRegex         = null;
    }

    /// <summary>The editor caret offset (Document-relative), mirrored from the view on every caret move.
    /// Find Next/Previous read it to step from wherever the cursor is.</summary>
    public int CurrentCaretOffset { get; set; }

    // Next/Previous find the match after/before the CARET — the first match whose file line is past the
    // caret's, so a plain click-then-F3 lands on the next occurrence like every editor. It works while
    // streaming because ComposeDocument keeps Document line numbers equal to file line numbers (the window's
    // real text is padded to its file position), so the caret's file line is just its document line — no
    // window arithmetic. The match list is file-line-sorted, so a binary search picks the neighbour, and
    // navigation slides the window to whatever line it lands on.
    [RelayCommand]
    private Task FindNext() => StepFromCaret(forward: true);

    [RelayCommand]
    private Task FindPrevious() => StepFromCaret(forward: false);

    private async Task StepFromCaret(bool forward)
    {
        var n = _matchingLineNumbers.Length;
        if (n == 0) return;

        var caretLine = CaretFileLine();
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
        await NavigateToMatchLineAsync(_matchingLineNumbers[idx]);
    }

    // First index whose value is >= target (the match list is ascending file lines).
    private static int LowerBound(long[] arr, long target)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (arr[mid] < target) lo = mid + 1; else hi = mid; }
        return lo;
    }

    // The caret's FILE line (0-based). Document lines == file lines even when streaming (see StepFromCaret),
    // so this is simply the caret's document line.
    private long CaretFileLine()
    {
        try { return Document.GetLineByOffset(Math.Clamp(CurrentCaretOffset, 0, Document.TextLength)).LineNumber - 1; }
        catch { return 0; }
    }

    private async Task NavigateToMatchLineAsync(long lineNumber) // 0-based file line
    {
        await EnsureLineResidentAsync(lineNumber);
        var offset = FindMatchOffsetOnLine(lineNumber);
        if (offset < 0) return;

        // Set the caret authoritatively here rather than waiting for the view to echo it back: across a
        // window slide that round-trip was unreliable, leaving the reference line stale so the next step
        // re-picked the same match. The view still moves the editor caret to the same offset for the user.
        CurrentCaretOffset = offset;
        ScrollToOffset = offset;
    }

    // Slides the resident window so <paramref name="lineNumber"/> (0-based file line) is in it — a no-op
    // for small files (the whole document is resident) or when the line is already resident.
    private async Task EnsureLineResidentAsync(long lineNumber)
    {
        if (IsLargeFile && _file is not null)
        {
            long winEnd = _winStartLine + CountNewlines(_winText);
            if (lineNumber < _winStartLine || lineNumber >= winEnd)
                await SlideWindowAsync(Math.Max(0, lineNumber - SlideMargin));
        }
    }

    /// <summary>Scrolls to a file line (0-based), sliding the window if needed. Backs Go-to-line.</summary>
    private async Task NavigateToFileLineAsync(long lineNumber)
    {
        await EnsureLineResidentAsync(lineNumber);
        try
        {
            int docLineNo  = (int)Math.Clamp(lineNumber + 1, 1, Math.Max(1, Document.LineCount));
            ScrollToOffset = Document.GetLineByNumber(docLineNo).Offset;
        }
        catch { }
    }

    // The first match's (Document offset, length) on <paramref name="lineNumber"/> (0-based), or (-1, 0).
    private (int off, int len) MatchSpanOnLine(long lineNumber)
    {
        try
        {
            var docLine  = Document.GetLineByNumber((int)lineNumber + 1);
            var lineText = Document.GetText(docLine.Offset, docLine.Length);
            if (FirstSpanOnLine(lineText) is { } span)
                return (docLine.Offset + span.Index, span.Length);
        }
        catch { }
        return (-1, 0);
    }

    private int FindMatchOffsetOnLine(long lineNumber) // 0-based
    {
        try
        {
            var docLine  = Document.GetLineByNumber((int)lineNumber + 1);
            var lineText = Document.GetText(docLine.Offset, docLine.Length);
            return FirstSpanOnLine(lineText) is { } span ? docLine.Offset + span.Index : docLine.Offset;
        }
        catch { }
        return -1;
    }

    // The first match span on a line, from the active request, so navigation lands where highlighting paints.
    private (int Index, int Length)? FirstSpanOnLine(string lineText)
    {
        if (_activeRequest is null) return null;
        foreach (var span in _activeRequest.Occurrences(lineText)) return span;
        return null;
    }

    // ── Find & Replace bar ────────────────────────────────────────────────────

    // Live re-run when the user edits the find text or flips a toggle (guarded so the engine writing
    // FindText/UseRegex/UseWildcards back doesn't recurse). Wildcards and regex are mutually exclusive:
    // turning one on turns the other off. That sibling-set is itself suppressed, so it neither recurses nor
    // schedules a second search.
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
        var sel = GetEditorSelection?.Invoke();
        if (string.IsNullOrEmpty(FindText) && !string.IsNullOrEmpty(sel) && !sel.Contains('\n'))
        {
            _suppressFindRun = true; FindText = sel; _suppressFindRun = false;
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

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (!CanEdit) { _shell.ShowError(NotEditableMessage()); return; }
        if (string.IsNullOrEmpty(FindText)) return;
        EnsureEditing();
        int n = await ApplyAiReplaceAsync(FindText, ReplaceText, UseRegex, MatchCase,
                                          0, Math.Max(0, LineCount - 1), CancellationToken.None);
        if (IsSearchActive) await ReRunSearchAsync();
        _shell.ShowNotification(n == 0 ? "No matches to replace." : $"Replaced {n} occurrence(s).");
    }

    [RelayCommand]
    private async Task ReplaceCurrent()
    {
        if (!CanEdit) { _shell.ShowError(NotEditableMessage()); return; }
        if (string.IsNullOrEmpty(FindText)) return;
        if (!IsSearchActive) await RunFindAsync();
        if (!IsSearchActive || _matchingLineNumbers.Length == 0) return;
        EnsureEditing();

        long line0 = _matchingLineNumbers[Math.Clamp(_currentMatchIndex < 0 ? 0 : _currentMatchIndex,
                                                     0, _matchingLineNumbers.Length - 1)];
        await EnsureLineResidentAsync(line0);

        var (off, len) = MatchSpanOnLine(line0);
        if (off >= 0 && len > 0 && off >= LoadedRealStart && off + len <= LoadedRealEnd)
        {
            var repl = ReplaceText;
            if (UseRegex && _activeRegex is not null)      // expand $1… against the matched text
                repl = _activeRegex.Replace(Document.GetText(off, len), repl, 1);
            Document.Replace(off, len, repl);              // flows through OnUserEdit → overlay for large files
            IsDirty = true;
        }
        await ReRunSearchAsync();
        await FindNext();
    }

    private string NotEditableMessage() => IsLargeFile
        ? "Large files inside archives can't be edited in place — extract or split first."
        : "This file can't be edited.";

    [RelayCommand]
    private void GoToLine()
    {
        int max = Math.Max(1, LineCount);
        _shell.ShowPrompt("Go to line", $"Line number (1–{max})", string.Empty,
            onConfirm: text =>
            {
                if (int.TryParse(text?.Trim(), out var line) && line >= 1)
                    _ = NavigateToFileLineAsync(Math.Min(line, max) - 1);
                else
                    _shell.ShowError("Enter a valid line number.");
            },
            onCancel: () => { });
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
            var request = new SearchRequest(query, IsRegex: regex, MatchCase: caseSensitive);
            var (lines, _) = await ScanAsync(request, ct);
            foreach (var l in lines)
            {
                if (shown >= max) break;
                var text = await _shell.RunOnUiAsync(() => Task.FromResult(LineText((int)l)));
                sb.Append("line ").Append(l + 1).Append(": ").Append(text).Append('\n');
                shown++;
            }
        }
        return shown == 0 ? "No matches." : $"{SearchMatchCount} match(es). Showing {shown}:\n{sb}";
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
        _disposed = true;
        _findDebounceCts?.Cancel();
        _watch?.Dispose();
        _searchCts?.Cancel();
        _file?.Dispose();
        try { if (File.Exists(FilePath + ".nexsave.tmp")) File.Delete(FilePath + ".nexsave.tmp"); } catch { }
    }
}
