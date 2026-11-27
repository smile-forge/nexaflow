using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.PlainText;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.Text.ClientTools;
using Nexaflow.Features.Text.Services;
using Nexaflow.IO.Common;
using Nexaflow.Localization.Formatting;
using Nexaflow.Visuals.Common.Controls;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Localization;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Interaction;

namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// The Text viewer: a file of any length shown as a document read over its blocks on disk (<see cref="ContentFile"/>) — held through a
/// window onto it that moves as the reader does, where it is too long for one document (<see cref="ContentFileWindow"/>) — and shown
/// through one view (<see cref="Editor"/>). Opening it costs a screen, a search reads it a line at a time, and what is written is held
/// over the file's own characters until it is saved, every step of it to take back.
/// <para>
/// <strong>Read, then edited.</strong> It is read while other programs may write it, and read again when they do; switched to editing it
/// is held against other writers (<see cref="ContentFile.HoldForEdit"/>) — read again first, where it changed meanwhile — and let go of
/// once editing ends. A file in an archive or on a location is read from a copy of it on this disk, and saved back through the file system.
/// </para>
/// </summary>
public sealed partial class TextViewModel : ObservableObject, IDisposable, IPageViewModel, IContextPreview, ISearchable
{
    // ── File info ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _filePath    = string.Empty;
    [ObservableProperty] private string _fileName    = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int    _lineCount;

    /// <summary>Whether the file is too long for one document, and is shown through a window onto it that moves as the reader does.</summary>
    [ObservableProperty] private bool _isLargeFile;

    /// <summary>What the first line of the document is numbered — one, or the line of the file a window onto it starts at.</summary>
    [ObservableProperty] private long _firstLineNumber = 1;

    // ── The document and its view ────────────────────────────────────────────

    /// <summary>The file as it is written in — over its blocks on disk, or the window onto it.</summary>
    public EditedDocument Document { get; } = new();

    /// <summary>This page's view of <see cref="Document"/>: the caret, what is picked out, what a search marks and which lines are on screen.</summary>
    public ContentEditor Editor { get; }

    /// <summary>The whole file the document shows, in its own lines, with the window onto a long one kept round the reader.</summary>
    private readonly ShownFile _shown;

    /// <summary>What the surface reads the file as: plain text, its lines as written.</summary>
    public string WrittenIn => TextParser.Language;

    // ── Editing ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(CanChangeEncoding))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    /// <summary>True while a save runs — the document is held read-only meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    private bool _isBusy;

    /// <summary>Whether the surface takes no writing: outside editing, and while a save runs.</summary>
    public bool IsReadOnly => !IsEditing || IsBusy;

    /// <summary>Whether the encoding can be changed — not while editing, when reading the file again would lose what was written.</summary>
    public bool CanChangeEncoding => !IsEditing;

    /// <summary>Raised whenever <see cref="IsDirty"/> changes, so the page registration can mark the tab.</summary>
    public event Action<bool>? DirtyChanged;

    // ── Encoding ──────────────────────────────────────────────────────────────

    [ObservableProperty] private EncodingOption _selectedEncoding;
    public List<EncodingOption> AvailableEncodings { get; } =
    [
        new("UTF-8",          Encoding.UTF8),
        new("UTF-16 LE",      Encoding.Unicode),
        new("UTF-16 BE",      Encoding.BigEndianUnicode),
        new("Latin-1",        Encoding.Latin1),
        new("System Default", Encoding.Default) { Label = Str.Get("Text.Viewer.Encoding.SystemDefault") },
    ];

    // ── Display toggles ───────────────────────────────────────────────────────

    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private bool _wordWrap;

    /// <summary>This tab's zoom over the shell's text size, which the surface is set at.</summary>
    public TextZoom Zoom { get; } = new();

    // ── Monitoring ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isMonitoring = true;
    [ObservableProperty] private string _fileChangedMessage = string.Empty;
    [ObservableProperty] private bool _fileChangedBannerVisible;

    // ── File splitting ────────────────────────────────────────────────────────

    [ObservableProperty] private bool             _isSplitPanelOpen;
    [ObservableProperty] private SplitModeOption  _selectedSplitMode;
    [ObservableProperty] private string           _splitValue     = "1000";
    [ObservableProperty] private string           _splitValueHint = Str.Get("Text.Viewer.Split.LinesPerFile");

    public IReadOnlyList<SplitModeOption> SplitModes { get; } =
    [
        new(SplitMode.ByLineCount, Str.Get("Text.Viewer.Split.ByLineCount")),
        new(SplitMode.BySize,      Str.Get("Text.Viewer.Split.BySize")),
        new(SplitMode.ByRegex,     Str.Get("Text.Viewer.Split.ByRegex")),
    ];

    // ── What it stands over ───────────────────────────────────────────────────

    private readonly IShellServices _shell;

    /// <summary>The runtime's file system the file is read, saved and split through — on disk, in an archive or on a location.</summary>
    private readonly IVirtualFileSystem _vfs;

    /// <summary>How many characters a window onto a file too long for one document holds.</summary>
    private readonly int _windowSize;

    /// <summary>The file the document is read over — the one last saved to, once it has been — or null for a file read whole.</summary>
    private ContentFile? _file;

    /// <summary>The window onto a file too long for one document, or null where the file is one.</summary>
    private ContentFileWindow? _window;

    /// <summary>Whether the file starts with its encoding's mark, which a save keeps as it was found.</summary>
    private bool _marked;

    /// <summary>Whether the reader chose the encoding, so the file is read in it rather than in what its first bytes suggest.</summary>
    private bool _encodingChosen;

    /// <summary>True while the encoding selector is being set to what the file was found in, which is no choice of the reader's.</summary>
    private bool _applyingEncoding;

    private IFileWatch? _watch;
    private bool _disposed;

    public TextViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs) : this(filePath, shell, vfs, ContentFileWindow.DefaultSize)
    {
    }

    /// <param name="windowSize">How many characters a window onto a file too long for one document holds — smaller, for a test.</param>
    internal TextViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs, int windowSize)
    {
        _shell = shell;
        _vfs   = vfs;
        _windowSize = windowSize;
        _selectedEncoding  = AvailableEncodings[0];
        _selectedSplitMode = SplitModes[0];
        FilePath = filePath;
        FileName = vfs.GetDisplayName(filePath);

        Editor = new ContentEditor(Document);
        Editor.Marks.Add(_matchMarks);

        Document.Changed += OnDocumentChanged;
        Document.PropertyChanged += OnDocumentPropertyChanged;
        _shown = new ShownFile(Editor);
    }

    partial void OnIsDirtyChanged(bool value) => DirtyChanged?.Invoke(value);

    private void OnDocumentChanged(object? sender, ContentSourceChange change) => Recounted();

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(EditedDocument.IsDirty)) IsDirty = Document.IsDirty;
    }

    /// <summary>How many lines the file comes to, and what the first one shown is numbered, said again after any change.</summary>
    private void Recounted()
    {
        LineCount = (int)Math.Min(int.MaxValue, _shown.LineCount);
        FirstLineNumber = _shown.LinesBefore + 1;
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    public Task LoadAsync(CancellationToken ct) => LoadAsync(keep: null, ct);

    /// <summary>
    /// Reads the file in place of whatever was shown — the caret and the top of the page stood on the lines of the file they stood on, where
    /// <paramref name="keep"/> says.
    /// </summary>
    private async Task LoadAsync((long Caret, long Top)? keep, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        try
        {
            var entry = await _vfs.GetEntryAsync(FilePath, ct) ?? throw new FileNotFoundException($"Could not find file '{FilePath}'.", FilePath);
            FileSizeText = SizeFormatter.FormatBytes(entry.Size);

            var onDisk = await _vfs.MaterializeFileAsync(FilePath, ct);
            var chosen = _encodingChosen ? SelectedEncoding.Encoding : null;
            var (file, whole, encoding, marked) = await Task.Run(() => Opened(onDisk, chosen, ct), ct);

            if (!_encodingChosen) SyncEncodingSelection(encoding);
            _marked = marked;

            Show(file, keep, whole);
            if (IsMonitoring) StartMonitoring();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Show(null, null);
            Document.Load(Str.Format("Text.Viewer.Status.LoadFailedFormat", ex.Message));
        }
    }

    /// <summary>
    /// The file on this disk at <paramref name="onDisk"/>, opened and scanned in <paramref name="chosen"/>, or in what its first bytes say where
    /// nothing was chosen — and whether it starts with that encoding's mark. In an encoding whose characters cannot be told apart a block at a
    /// time (<see cref="ContentFile.Reads"/>), it is read whole instead.
    /// </summary>
    [LocalDiskOnly("the file as it is on this disk - MaterializeFileAsync copies an archive's entry or a location's file here first, and a file on this disk is itself")]
    private static (ContentFile? File, string? Whole, Encoding Encoding, bool Marked) Opened(string onDisk, Encoding? chosen, CancellationToken ct)
    {
        Encoding encoding;
        byte[] head;

        using (var stream = new FileStream(onDisk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            encoding = chosen ?? EncodingDetector.Detect(stream).Encoding;
            stream.Position = 0;
            head = new byte[4];
            head = head[..stream.Read(head)];
        }

        var mark = encoding.GetPreamble();
        var marked = mark.Length > 0 && head.AsSpan().StartsWith(mark);

        if (!ContentFile.Reads(encoding)) return (null, File.ReadAllText(onDisk, encoding), encoding, marked);

        var file = ContentFile.Open(onDisk, encoding, marked ? mark.Length : 0, ContentFileAccess.Editable);
        try
        {
            file.Scan(cancel: ct);
        }
        catch
        {
            file.Dispose();
            throw;
        }

        return (file, null, encoding, marked);
    }

    /// <summary>
    /// Shows <paramref name="file"/> in place of whatever was — one document over its blocks, or a window onto it where it is too long for one;
    /// <paramref name="whole"/>, where the file was read whole — and lets go of the file shown before; the caret and the top of the page stood
    /// where <paramref name="keep"/> says.
    /// </summary>
    private void Show(ContentFile? file, (long Caret, long Top)? keep, string? whole = null)
    {
        var before = _file;
        (_file, _window) = (file, null);

        if (file is null) Document.Load(whole ?? string.Empty);
        else if (file.Characters > _windowSize) _window = ContentFileWindow.Open(file, Document, at: 0, size: _windowSize);
        else Document.Load(ContentDocument.Of(file));

        if (!ReferenceEquals(before, file)) before?.Dispose();

        IsLargeFile = _window is not null;
        IsDirty = Document.IsDirty;
        Recounted();

        if (keep is not { } place) return;

        _shown.GoToLine(place.Top, ShownPlace.Top);
        if (_shown.OffsetOfLine(Math.Min(place.Caret, _shown.LineCount - 1)) is { } caret) Editor.MoveCaretTo(_shown.DocumentOffset(caret));
    }

    /// <summary>Where the caret and the top of the page stand, as lines of the file — what reading it again keeps.</summary>
    private (long Caret, long Top) Place()
    {
        var document = Document.Current;
        return (_shown.LineOf(Editor.Caret), _shown.LinesBefore + Math.Min(Editor.ShownLines.First, Math.Max(0, document.LineCount - 1)));
    }

    /// <summary>Reflects the encoding the file was found in in the selector, without reading it again.</summary>
    private void SyncEncodingSelection(Encoding found)
    {
        var option = AvailableEncodings.Find(each => each.Encoding.CodePage == found.CodePage);
        if (option is null || ReferenceEquals(option, SelectedEncoding)) return;

        _applyingEncoding = true;
        SelectedEncoding = option;
        _applyingEncoding = false;
    }

    // ── Editing toggle ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleEditing()
    {
        if (IsEditing)
        {
            if (IsDirty && !await _shell.ConfirmAsync(Str.Get("Text.Viewer.Edit.DiscardTitle"), Str.Get("Text.Viewer.Edit.DiscardAsk"))) return;

            // Read again: what was written and not kept goes, and so does the hold against other writers.
            IsEditing = false;
            await ReloadAsync();
            if (_watch is not null) _watch.Enabled = IsMonitoring;
            return;
        }

        await EnsureEditingAsync();
    }

    /// <summary>
    /// Starts editing, where it has not started: the file held against other writers — read again first, where it changed since it was read —
    /// and false, with the reader told why, where another program is writing it.
    /// </summary>
    internal async Task<bool> EnsureEditingAsync()
    {
        if (IsEditing) return true;

        if (_file is not null && _vfs.ResolveOnDisk(FilePath) is not null)
        {
            var held = _file.HoldForEdit();

            if (held == ContentFileHold.Changed)
            {
                await ReloadAsync();
                held = _file?.HoldForEdit() ?? ContentFileHold.Held;
            }

            if (held == ContentFileHold.WrittenElsewhere)
            {
                _shell.ShowError(Str.Format("Text.Viewer.Edit.WrittenElsewhere", FileName));
                return false;
            }
        }

        if (_watch is not null) _watch.Enabled = false; // nothing else writes it now, and what it would read again is being written here
        IsEditing = true;
        return true;
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

            var (encoding, marked, hold) = (SelectedEncoding.Encoding, _marked, IsEditing);

            if (_vfs.ResolveOnDisk(FilePath) is { } path && _file is not null)
            {
                // Written beside the file, away from the page, and put in its place — so a save that fails leaves the file as it was — and
                // then the document is held over the file it was written to, every step that can be taken back kept. The file it stood over
                // before is let go of once nothing reads it: a step taken back may still.
                if (_window is { } window)
                {
                    var saved = await Task.Run(() => window.WriteOut(path, encoding, marked, hold));
                    window.StandOver(saved);
                    _file = saved;
                }
                else
                {
                    var version = Document.Current;
                    var saved = await Task.Run(() => ContentFile.WriteOut(version, path, encoding, marked, ContentFileAccess.Editable, hold));
                    Document.Rebase(ContentDocument.Of(saved));
                    Document.MarkSaved();
                    _file = saved;
                }
            }
            else
            {
                // A file in an archive or on a location, written back through the file system as a stream of what is written.
                var (window, version) = (_window, Document.Current);

                await using (var write = await _vfs.BeginWriteAsync(FilePath, WriteMode.Replace))
                {
                    await Task.Run(() =>
                    {
                        if (marked) write.Content.Write(encoding.GetPreamble());

                        using var writer = new StreamWriter(write.Content, ContentFile.Unmarked(encoding), 1 << 16, leaveOpen: true);
                        if (window is not null) window.WriteTo(writer);
                        else version.WriteTo(writer);
                    });

                    await write.CommitAsync(CancellationToken.None);
                }

                Document.MarkSaved();
            }

            if (await _vfs.GetEntryAsync(FilePath) is { } entry) FileSizeText = SizeFormatter.FormatBytes(entry.Size);
            _shell.ShowNotification(Str.Format("Text.Viewer.Save.SavedFormat", FileName));
        }
        catch (Exception ex)
        {
            _shell.ShowError(Str.Format("Text.Viewer.Save.FailedFormat", FileName, ex.Message));
        }
        finally
        {
            IsBusy = false;
            if (_watch is not null && !IsEditing) _watch.Enabled = IsMonitoring;
        }
    }

    // ── Reload (encoding change / monitor / leaving editing) ──────────────────────

    /// <summary>Reads the file again, the caret and the page kept on the lines they stood on, and the search run again where one is showing.</summary>
    private async Task ReloadAsync()
    {
        _searchCts?.Cancel();
        await LoadAsync(Place(), CancellationToken.None);
        if (IsSearchActive) await ReRunSearchAsync();
    }

    partial void OnSelectedEncodingChanged(EncodingOption value)
    {
        if (_applyingEncoding || IsEditing) return; // set to what the file was found in, or locked while editing
        _encodingChosen = true;
        _ = ReloadAsync();
    }

    // ── File monitoring ───────────────────────────────────────────────────────

    private void StartMonitoring()
    {
        // Started once the file is loaded, so it is there; the shell watches it wherever it is.
        _watch?.Dispose();
        _watch = _shell.WatchFile(FilePath, OnFileChanged);
        if (IsEditing || IsBusy) _watch.Enabled = false;
    }

    private async void OnFileChanged()
    {
        if (IsEditing || IsBusy) return; // held against other writers while editing; and a save of ours is no change
        FileChangedMessage       = Str.Get("Text.Viewer.Status.ChangedOnDisk");
        FileChangedBannerVisible = true;
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
            SplitMode.BySize  => (Str.Get("Text.Viewer.Split.MegabytesPerFile"), "10"),
            SplitMode.ByRegex => (Str.Get("Text.Viewer.Split.RegexHint"),        "^"),
            _                 => (Str.Get("Text.Viewer.Split.LinesPerFile"),     "1000"),
        };

    [RelayCommand]
    private void ToggleSplitPanel() => IsSplitPanelOpen = !IsSplitPanelOpen;

    [RelayCommand]
    private async Task Split()
    {
        if (string.IsNullOrEmpty(FilePath) || !await _vfs.ExistsAsync(FilePath)) return;

        SplitOptions options;
        var mode = SelectedSplitMode.Mode;
        switch (mode)
        {
            case SplitMode.BySize:
                if (!double.TryParse(SplitValue, out var mb) || mb <= 0)
                { _shell.ShowError(Str.Get("Text.Viewer.Split.PositiveSize")); return; }
                options = new SplitOptions { Mode = mode, MaxBytesPerPart = (long)(mb * 1024 * 1024) };
                break;
            case SplitMode.ByLineCount:
                if (!int.TryParse(SplitValue, out var lines) || lines <= 0)
                { _shell.ShowError(Str.Get("Text.Viewer.Split.PositiveLineCount")); return; }
                options = new SplitOptions { Mode = mode, LinesPerPart = lines };
                break;
            default:
                if (string.IsNullOrWhiteSpace(SplitValue))
                { _shell.ShowError(Str.Get("Text.Viewer.Split.PatternRequired")); return; }
                try { _ = new Regex(SplitValue); }
                catch { _shell.ShowError(Str.Get("Text.Viewer.Split.PatternInvalid")); return; }
                options = new SplitOptions { Mode = mode, BoundaryPattern = SplitValue };
                break;
        }

        IsSplitPanelOpen = false;
        var task = new SplitFileBackgroundTask(_vfs, FilePath, options);
        _shell.QueueBackgroundTask(task, ok =>
        {
            if (ok && task.Result is { OutputFiles.Count: > 0 } r)
                _shell.ShowNotification(Str.Format("Text.Viewer.Split.DoneFormat", FileName, r.OutputFiles.Count));
            else if (ok)
                _shell.ShowNotification(Str.Format("Text.Viewer.Split.EmptyFormat", FileName));
        });
    }

    [RelayCommand]
    private void GoToLine()
    {
        var max = Math.Max(1, LineCount);
        _shell.ShowPrompt(Str.Get("Text.Viewer.GoToLine.Title"), Str.Format("Text.Viewer.GoToLine.PromptFormat", max), string.Empty,
            onConfirm: text =>
            {
                if (int.TryParse(text?.Trim(), out var line) && line >= 1) _shown.GoToLine(Math.Min(line, max) - 1, ShownPlace.Centre);
                else _shell.ShowError(Str.Get("Text.Viewer.GoToLine.Invalid"));
            },
            onCancel: () => { });
    }

    // ── IPageViewModel ────────────────────────────────────────────────────────

    public string GetContext()
    {
        var fileName = _vfs.GetDisplayName(FilePath);
        var dirty    = IsDirty ? " (unsaved edits)" : IsEditing ? " (editing)" : string.Empty;
        var (first, lines) = _shown.ShownLines();

        var numbered = new StringBuilder();
        for (var at = 0; at < lines.Count; at++)
            numbered.Append(first + at).Append('\t').Append(lines[at]).Append('\n');

        return $"Text file: '{fileName}' at '{FilePath}'{dirty}\n" +
               $"Encoding: {SelectedEncoding.Name}. Total lines: {LineCount}.\n" +
               $"Showing lines {first}-{first + Math.Max(0, lines.Count - 1)} of {LineCount}:\n{numbered}";
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

    /// <summary>The file this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(FilePath);

    /// <summary>A compact, read-only preview for the conversation's context panel: the file name, a meta line, and the start of what is
    /// written. Built fresh each time — it never re-hosts the live editor.</summary>
    public object CreateContextPreview()
    {
        var lines = LineCount == 1
            ? Str.Format("Text.Viewer.Preview.LineOne", LineCount)
            : Str.Format("Text.Viewer.Preview.LineMany", LineCount);
        var state = IsDirty ? Str.Get("Text.Viewer.Preview.Unsaved")
                  : IsEditing ? Str.Get("Text.Viewer.Preview.Editing")
                  : null;
        var meta = state is null
            ? $"{lines} · {SelectedEncoding.Label}"
            : $"{lines} · {SelectedEncoding.Label} · {state}";
        const int cap = 8000;
        var document = Document.Current;
        var body = document.Length > cap ? document.Slice(0, cap) + "\n" + Str.Get("Text.Viewer.Preview.Truncated") : document.ToString();
        return new TextContextPreview(meta, body);
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
        Document.Changed -= OnDocumentChanged;
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        _shown.Dispose();
        Editor.Dispose();
        _file?.Dispose();
    }

    /// <summary>Puts the text the page shows on the clipboard; false while another application holds it open.</summary>
    public Task<bool> CopyVisibleTextAsync() => _shell.Clipboard.WriteAsync(ComposedPayload.OfText(string.Join("\n", _shown.ShownLines().Lines)));
}
