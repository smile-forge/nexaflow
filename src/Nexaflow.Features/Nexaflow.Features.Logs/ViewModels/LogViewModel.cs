using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using Nexaflow.Localization.Formatting;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.PlainText;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Logs.ViewModels;

/// <summary>
/// The log viewer: a log of any size shown read-only over its blocks on disk (<see cref="ContentFile"/>), shared with the program writing
/// it. Its last few megabytes are shown at once and the history before them is read in behind, so a log opens on its live end however long
/// it is; one too long for a document is then shown through a window onto it that moves where the reader goes. What the writer adds is
/// scanned in as it lands — never read again from the start — and what is marked on the lines (levels, the filter, a term, a search, the
/// lines picked out) is worked out only for the lines on screen.
/// </summary>
public sealed partial class LogViewModel : ObservableObject, IDisposable, IPageViewModel, IContextPreview
{
    /// <summary>How much of a long log's end is read before it is shown, the rest read in behind.</summary>
    private const int TailBytes = 4 * 1024 * 1024;

    /// <summary>How much of the file's start is read to tell its encoding and its format.</summary>
    private const int HeadBytes = 4096;

    // ── File ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _filePath     = string.Empty;
    [ObservableProperty] private string _fileName     = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int    _lineCount;

    /// <summary>The number the document's first line has in the whole file: one, unless a window onto a long log stands further on.</summary>
    [ObservableProperty] private long _firstLineNumber = 1;

    // ── Document ──────────────────────────────────────────────────────────────

    /// <summary>The log as shown: the file's own characters, read over its blocks, never written in.</summary>
    public EditedDocument Document { get; } = new();

    /// <summary>The one view of it: the caret, what is picked out, where the page stands, and what is marked.</summary>
    public ContentEditor Editor { get; }

    /// <summary>What the log is read as: lines of text, coloured by the marks rather than a grammar.</summary>
    public string WrittenIn => TextParser.Language;

    /// <summary>The whole log in its own lines, with the window onto a long one kept round the reader.</summary>
    private readonly ShownFile _shown;

    /// <summary>How many characters a document holds before a log is shown through a window instead.</summary>
    private readonly int _windowSize;

    /// <summary>How much of a long log's end is shown before the rest is read.</summary>
    private readonly int _tailBytes;

    // ── Encoding ──────────────────────────────────────────────────────────────

    [ObservableProperty] private EncodingOption _selectedEncoding;
    public List<EncodingOption> AvailableEncodings { get; } =
    [
        new("UTF-8",          Encoding.UTF8),
        new("UTF-16 LE",      Encoding.Unicode),
        new("UTF-16 BE",      Encoding.BigEndianUnicode),
        new("Latin-1",        Encoding.Latin1),
        new(Str.Get("Logs.Encoding.SystemDefault"), Encoding.Default),
    ];

    /// <summary>Whether the reader chose the encoding, rather than it being what the file's first bytes said.</summary>
    private bool _encodingChosen;

    /// <summary>Whether the selector is being set to what the file was found in, which reads nothing again.</summary>
    private bool _applyingEncoding;

    // ── Parser ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ILogParser _activeParser = new RawTextLogParser();

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>Whether only the end of the log is shown yet, the history before it still being read.</summary>
    [ObservableProperty] private bool _isLoadingHead;

    /// <summary>The file grown into what is shown as a writer adds to it: the end of a long log, or the whole of it — read where it is.</summary>
    private ContentFile? _file;

    /// <summary>The history before the end shown first, while it is being read.</summary>
    private ContentFile? _history;

    /// <summary>Every file opened for what is shown, let go of when it is read again or the page closes.</summary>
    private readonly List<ContentFile> _files = [];

    /// <summary>Where the file is on this disk, read where it is; for one in an archive or on a location, the copy read here.</summary>
    private string? _onDisk;

    /// <summary>Whether the file is read where it is, so what a writer appends is scanned in rather than the file copied again.</summary>
    private bool _live;

    private CancellationTokenSource? _loadCts;

    // ── Monitoring ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isMonitoring    = true;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isAutoScrolling = true;
    private IFileWatch?             _watch;
    private readonly IShellServices _shell;

    /// <summary>The runtime's file system the log is read through — on disk, in an archive or on a location.</summary>
    private readonly IVirtualFileSystem _vfs;

    /// <summary>Whether what was added is being taken up, and whether more was added meanwhile.</summary>
    private bool _growing, _growAgain;

    private bool _disposed;

    // ──────────────────────────────────────────────────────────────────────────

    public LogViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs) : this(filePath, shell, vfs, ContentFileWindow.DefaultSize, TailBytes)
    {
    }

    /// <summary>A log viewer that shows a log longer than <paramref name="windowSize"/> characters through a window, and one longer than
    /// <paramref name="tailBytes"/> bytes end first — sizes the tests make small, to show a short file the way a long one is.</summary>
    internal LogViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs, int windowSize, int tailBytes)
    {
        _shell = shell;
        _vfs   = vfs;
        _windowSize = Math.Max(1, windowSize);
        _tailBytes = Math.Max(1, tailBytes);
        _selectedEncoding = AvailableEncodings[0];
        FilePath = filePath;
        FileName = vfs.GetDisplayName(filePath);

        Editor = new ContentEditor(Document);
        _shown = new ShownFile(Editor);
        _find = new FileFind(_shown, Editor.Found, ShownPlace.Centre);
        _find.Changed += OnFindChanged;
        Lined();

        Document.Changed += (_, _) => Recounted();
    }

    /// <summary>How many lines the log comes to, and what the first one shown is numbered, said again after any change.</summary>
    private void Recounted()
    {
        LineCount = (int)Math.Min(int.MaxValue, _shown.LineCount);
        FirstLineNumber = _shown.LinesBefore + 1;
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>What opening the log found: its encoding, its first bytes, and what is shown at once — and the history to read in behind.</summary>
    private readonly record struct Opening(Encoding Encoding, int Preamble, byte[] Head, ContentFile? Shown, ContentFile? History, string? Whole);

    /// <summary>How many bytes the encoding's mark at the start of the log takes.</summary>
    private int _preamble;

    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        try
        {
            if (await _vfs.GetEntryAsync(FilePath, ct) is not { IsDirectory: false } entry) return;

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var loading = _loadCts.Token;

            FileSizeText = SizeFormatter.FormatBytes(entry.Size);
            _live = _vfs.ResolveOnDisk(FilePath) is not null;
            _onDisk = await _vfs.MaterializeFileAsync(FilePath, ct);

            var (onDisk, chosen, tail) = (_onDisk, _encodingChosen ? SelectedEncoding.Encoding : null, _tailBytes);
            var opening = await Task.Run(() => Opened(onDisk, chosen, tail, ct), ct);
            if (_disposed) { Closed(opening); return; }

            Show(opening);
            if (IsMonitoring) StartMonitoring();
            if (_history is not null) _ = ReadHistoryAsync(loading);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Show(new Opening(Encoding.UTF8, 0, [], null, null, Str.Format("Logs.Document.LoadFailedFormat", ex.Message)));
        }
    }

    /// <summary>
    /// The log on this disk at <paramref name="onDisk"/>, in <paramref name="chosen"/> or what its first bytes say: scanned whole where it is no
    /// longer than <paramref name="tailBytes"/>, and otherwise its end from where a line starts, the history before it opened to be read in
    /// behind. In an encoding a block cannot be cut out of (<see cref="ContentFile.Reads"/>) it is read whole.
    /// </summary>
    [LocalDiskOnly("the log as it is on this disk - MaterializeFileAsync copies an archive's entry or a location's file here first, and a file on this disk is itself")]
    private static Opening Opened(string onDisk, Encoding? chosen, int tailBytes, CancellationToken ct)
    {
        byte[] head;
        Encoding encoding;

        using (var stream = new FileStream(onDisk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            head = new byte[(int)Math.Min(HeadBytes, stream.Length)];
            stream.ReadExactly(head);
            encoding = chosen ?? EncodingDetector.Detect(new MemoryStream(head, writable: false)).Encoding;
        }

        var mark = encoding.GetPreamble();
        var preamble = mark.Length > 0 && head.AsSpan().StartsWith(mark) ? mark.Length : 0;

        if (!ContentFile.Reads(encoding)) return new Opening(encoding, preamble, head, null, null, File.ReadAllText(onDisk, encoding));

        var whole = ContentFile.Open(onDisk, encoding, preamble, ContentFileAccess.ReadOnly);
        try
        {
            var length = new FileInfo(onDisk).Length;
            var start = length - preamble > tailBytes ? whole.LineStartByte(length - tailBytes) : preamble;

            if (start <= preamble)
            {
                whole.Scan(cancel: ct);
                return new Opening(encoding, preamble, head, whole, null, null);
            }

            var tail = ContentFile.OpenStretch(onDisk, encoding, start, ContentFileAccess.ReadOnly);
            try
            {
                tail.Scan(cancel: ct);
            }
            catch
            {
                tail.Dispose();
                throw;
            }

            return new Opening(encoding, preamble, head, tail, whole, null);
        }
        catch
        {
            whole.Dispose();
            throw;
        }
    }

    /// <summary>Shows what opening found in place of whatever was, letting go of the files that showed it — and stands at the live end where following.</summary>
    private void Show(Opening opening)
    {
        var before = _files.ToList();
        _files.Clear();

        (_file, _history, _preamble) = (opening.Shown, opening.History, opening.Preamble);
        if (_file is not null) _files.Add(_file);
        if (_history is not null) _files.Add(_history);

        if (_file is { } shown) Document.Load(ContentDocument.Of(shown));
        else Document.Load(opening.Whole ?? string.Empty);

        foreach (var file in before) file.Dispose();

        if (!_encodingChosen) SyncEncodingSelection(opening.Encoding);
        IsLoadingHead = _history is not null;
        _selectedLines.Clear();
        Reselected();

        SelectParser(opening.Head);
        DetectTimestamps();
        Recounted();
        if (IsAutoScrolling) FollowEnd();
    }

    private static void Closed(Opening opening)
    {
        opening.Shown?.Dispose();
        opening.History?.Dispose();
    }

    /// <summary>
    /// Reads the history before the end shown first, and puts it before it: grown in at the start where the two fit in one document — the
    /// page left on the lines it stood on — and otherwise read on to the end, the whole log then shown through a window standing where the
    /// reader was. Lines picked out move with the lines put before them.
    /// </summary>
    private async Task ReadHistoryAsync(CancellationToken ct)
    {
        if (_history is not { } history || _file is not { } tail) return;

        try
        {
            var until = tail.From;
            await Task.Run(() => history.Scan(cancel: ct, until: until), ct);
            if (_disposed || ct.IsCancellationRequested || !ReferenceEquals(history, _history)) return;

            var before = history.LineBreaksOf(0, history.BlockCount);

            if (history.Characters + tail.Characters <= _windowSize)
            {
                var top = Editor.ShownLines.First;
                Document.Grow(ContentDocument.Of(history), atStart: true);
                _history = null;

                if (!IsAutoScrolling) Editor.ScrollToLine((int)Math.Min(int.MaxValue, top + before), ShownPlace.Top);
            }
            else
            {
                var shift = history.Characters;
                var (caret, top) = (shift + Editor.Caret, shift + Document.Current.OffsetOf(Editor.ShownLines.First));

                // Read on past where the end shown starts, to the end: the whole log, which no document holds.
                await Task.Run(() => history.Scan(cancel: ct), ct);
                if (_disposed || ct.IsCancellationRequested || !ReferenceEquals(history, _history)) return;

                ContentFileWindow.Open(history, Document, caret, _windowSize);
                _files.Remove(tail);
                tail.Dispose();
                (_file, _history) = (history, null);

                Editor.MoveCaretTo(_shown.DocumentOffset(caret));
                if (!IsAutoScrolling) Editor.ScrollToLine(Document.Current.LineOf(_shown.DocumentOffset(top)), ShownPlace.Top);
            }

            MoveSelection(before);
            MoveSearch(before);
            IsLoadingHead = false;
            Recounted();
            if (HasTimestamps) ScanForTimeRange();
            await RescanSearchAsync();
            if (IsAutoScrolling) FollowEnd();

            // What was written while the history was read.
            if (!IsPaused) await TakeUpAsync();
        }
        catch (OperationCanceledException) { }
        // The log could not be read on, or the page let go of it meanwhile: the end shown stays what is shown.
        catch (Exception e) when (e is IOException or ObjectDisposedException) { IsLoadingHead = false; }
    }

    /// <summary>Puts the page at the live end: the last line on screen, a window onto a long log moved there first.</summary>
    private void FollowEnd()
    {
        if (Document.Window is { } window && !window.Covers(window.Length, window.Length)) window.MoveTo(window.Length);
        Editor.ScrollToLine(Math.Max(0, Document.Current.LineCount - 1), ShownPlace.Nearest);
    }

    // ── Encoding ──────────────────────────────────────────────────────────────

    /// <summary>Reflects the encoding the log was found in in the selector, without reading it again.</summary>
    private void SyncEncodingSelection(Encoding found)
    {
        var option = AvailableEncodings.Find(each => each.Encoding.CodePage == found.CodePage);
        if (option is null || ReferenceEquals(option, SelectedEncoding)) return;

        _applyingEncoding = true;
        SelectedEncoding = option;
        _applyingEncoding = false;
    }

    partial void OnSelectedEncodingChanged(EncodingOption value)
    {
        if (_applyingEncoding) return;

        _encodingChosen = true;
        _ = LoadAsync(CancellationToken.None);
    }

    // ── Parser selection ──────────────────────────────────────────────────────

    /// <summary>The parser the file's first bytes and the first line shown vote for.</summary>
    private void SelectParser(byte[] head)
    {
        var document = Document.Current;
        var (start, end) = document.LineAt(0);

        ActiveParser = LogParserRegistry.SelectParser(head.AsSpan(0, Math.Min(16, head.Length)), document.Slice(start, end - start));
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        // Started once the file is loaded, so it is there; the shell watches it wherever it is.
        _watch?.Dispose();
        _watch = _shell.WatchFile(FilePath, OnFileChanged);
    }

    /// <summary>
    /// The watched log changed: its size said again, and — unless the tail is paused — what was appended taken up. A log that came to be
    /// shorter than what was read was rotated or cut, and is read again; so is one not read where it is, whose copy here is made again.
    /// </summary>
    private async void OnFileChanged()
    {
        if (!IsMonitoring || _disposed) return;

        long size;
        try   { if (await _vfs.GetEntryAsync(FilePath) is not { } changed) return; size = changed.Size; }
        catch { return; }

        FileSizeText = SizeFormatter.FormatBytes(size);

        if (!_live || (_file is { } file && size < file.Scanned))
        {
            await LoadAsync(CancellationToken.None);
            return;
        }

        if (!IsPaused) await TakeUpAsync();
    }

    /// <summary>
    /// Takes up what has been appended to the log since it was last read: scanned off the page, then grown into what is shown — at its end,
    /// or after a window onto a long log — and followed where following. A log grown past what one document holds moves into a window.
    /// </summary>
    private async Task TakeUpAsync()
    {
        if (_growing)
        {
            _growAgain = true;
            return;
        }

        _growing = true;

        try
        {
            do
            {
                _growAgain = false;
                if (_file is not { } file || _history is not null) return;

                var blocks = file.BlockCount;
                await Task.Run(() => file.Scan());
                if (_disposed || !ReferenceEquals(file, _file)) return;

                if (Document.Window is { } window) window.Grew();
                else if (file.BlockCount > blocks) Document.Grow(ContentDocument.Of(file, blocks));
                else continue;

                if (Document.Window is null && Document.Current.Length > _windowSize) await MoveIntoWindowAsync();

                await RescanSearchAsync();
                if (IsAutoScrolling) FollowEnd();
            }
            while (_growAgain && !_disposed);
        }
        // The log could not be read just now, or the page let go of it meanwhile: the next change tries again, where there is one.
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        finally
        {
            _growing = false;
        }
    }

    /// <summary>
    /// Shows a log grown past what one document holds through a window onto the whole of it, standing where the reader is: the whole file
    /// scanned once more off the page, its earlier stretches then let go of.
    /// </summary>
    private async Task MoveIntoWindowAsync()
    {
        if (_onDisk is not { } path || _file is not { } shown) return;

        var whole = ContentFile.Open(path, shown.Encoding, _preamble, ContentFileAccess.ReadOnly);
        var caret = Editor.Caret;

        try
        {
            await Task.Run(() => whole.Scan());
        }
        catch
        {
            whole.Dispose();
            throw;
        }

        if (_disposed || !ReferenceEquals(shown, _file))
        {
            whole.Dispose();
            return;
        }

        ContentFileWindow.Open(whole, Document, caret, _windowSize);
        foreach (var file in _files) file.Dispose();
        _files.Clear();
        _files.Add(whole);
        _file = whole;

        Editor.MoveCaretTo(_shown.DocumentOffset(caret));
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
        // Resuming takes up everything written while the tail was paused — held on disk meanwhile, not here.
        if (!value) _ = TakeUpAsync();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _watch?.Dispose();
        _find.Changed -= OnFindChanged;
        _find.Dispose();
        _shown.Dispose();
        Editor.Dispose();

        foreach (var file in _files) file.Dispose();
        _files.Clear();
    }
}
