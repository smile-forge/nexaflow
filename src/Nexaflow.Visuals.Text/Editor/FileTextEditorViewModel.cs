using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Text.Editor.Commands;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// Shared view-model for the read-write code/text editor surface: a full-file load into AvalonEdit's in-memory
/// document, with encoding/EOL-aware save, F5 reload, and a file watcher that reloads on external change
/// (prompting first when there are unsaved edits) while ignoring the editor's own writes.
///
/// Concrete and unsealed (not abstract) so the generic editor uses it directly, and the "As Code" editor
/// subclasses it (see <c>CodeViewModel</c>) to add its structure panel.
/// </summary>
public partial class FileTextEditorViewModel : ObservableObject, IPageViewModel, IDisposable
{
    protected readonly IShellServices Shell;
    private readonly long _maxEditableBytes;

    private IFileWatch? _watch;
    private bool _applyingEncoding;     // true while syncing the encoding selector to the detected encoding
    private bool _hasSelection;         // mirrors the editor's live selection (drives selection-only commands)

    public TextDocument Document { get; } = new();

    // ── File info ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private int _lineCount;

    // ── Editor state ─────────────────────────────────────────────────────────────
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [ObservableProperty] private bool _isDirty;
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [ObservableProperty] private bool _isReadOnlyMode;
    [ObservableProperty] private string _readOnlyReason = string.Empty;

    [ObservableProperty] private bool _showLineNumbers = true;

    /// <summary>The document's detected line terminator, shown in the footer (e.g. "LF", "CRLF", "Mixed").</summary>
    [ObservableProperty] private string _lineEndingLabel = string.Empty;

    // ── External-change banner ──────────────────────────────────────────────────
    [ObservableProperty] private string _bannerMessage = string.Empty;
    [ObservableProperty] private bool _bannerVisible;

    // ── Encoding (decode on load, encode on save) ────────────────────────────────
    [ObservableProperty] private EncodingOption _selectedEncoding;
    public List<EncodingOption> AvailableEncodings { get; } =
    [
        new("UTF-8",          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
        new("UTF-8 with BOM", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        new("UTF-16 LE",      Encoding.Unicode),
        new("UTF-16 BE",      Encoding.BigEndianUnicode),
        new("Latin-1",        Encoding.Latin1),
        new("System Default", Encoding.Default),
    ];

    // ── Line endings ─────────────────────────────────────────────────────────────
    [ObservableProperty] private LineEndingOption _selectedEol;
    public List<LineEndingOption> AvailableEols { get; } =
    [
        new("Preserve EOL", LineEnding.Preserve),
        new("LF",           LineEnding.Lf),
        new("CRLF",         LineEnding.CrLf),
        new("CR",           LineEnding.Cr),
    ];

    public FileTextEditorViewModel(string filePath, IShellServices shell, long maxEditableBytes)
    {
        Shell = shell;
        _maxEditableBytes = maxEditableBytes;
        _selectedEncoding = AvailableEncodings[0];
        _selectedEol = AvailableEols[0];
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        IsPlainText = !HighlightingRegistry.IsStructured(FileName); // structured ⇒ highlighted; plain ⇒ spell-check eligible
        Document.TextChanged += OnDocumentTextChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackChanged; // dirty = "differs from the saved state"
        BuildCommands();
    }

    /// <summary>True when the file has no syntax highlighting (so the Windows spell-checker may attach).</summary>
    public bool IsPlainText { get; }

    // ── Command groups (Checksum / Encode / Decode + per-subclass extras; Lines lives in the footer) ──

    /// <summary>The floating panel's command-group dropdowns (Checksum / Encode / Decode). Selection-only
    /// groups and entries are dropped when there is no selection, so the panel never offers a no-op.</summary>
    public ObservableCollection<EditorCommandGroupVm> CommandGroups { get; } = [];

    /// <summary>The "Lines" group, surfaced from the footer's line-ending indicator (EOL conversions, plus
    /// prose line ops for plain text). Built once — its membership doesn't depend on the selection.</summary>
    public IReadOnlyList<EditorCommandVm> LineCommands { get; private set; } = [];

    /// <summary>Set by the view so commands can read the live selection. The only editor-control coupling.</summary>
    internal Func<TextArea>? EditorAccess { get; set; }

    /// <summary>Raised to ask the hosting view to scroll the editor to a 1-based line and place the caret there.
    /// Used by the "As Code" structure panel to jump to a member; harmless for the plain editor.</summary>
    public event Action<int>? ScrollToLineRequested;

    /// <summary>Requests the view scroll the editor to <paramref name="line"/> (1-based).</summary>
    public void RequestScrollToLine(int line) => ScrollToLineRequested?.Invoke(line);

    /// <summary>Override to contribute viewer-specific commands; merged with the built-ins.</summary>
    protected virtual IEnumerable<ITextEditorCommand> ProvideCommands() => [];

    /// <summary>The commands available for this file: built-ins + subclass extras, minus line-reordering/munging
    /// for code/markup (only meaningful on prose/data).</summary>
    private IEnumerable<ITextEditorCommand> AvailableCommands =>
        BuiltInTextEditorCommands.All.Concat(ProvideCommands()).Where(c => IsPlainText || !c.PlainTextOnly);

    private void BuildCommands()
    {
        LineCommands = AvailableCommands.Where(c => c.Group == "Lines")
            .Select(c => new EditorCommandVm(c, this)).ToList();
        RebuildCommandGroups();
    }

    /// <summary>Rebuilds the floating panel's groups for the current selection state: the "Lines" group is
    /// excluded (it's in the footer), and selection-scoped commands appear only when there is a selection — so
    /// Encode/Decode (all selection-scoped) vanish entirely and Checksum drops its "of selection" entries.</summary>
    private void RebuildCommandGroups()
    {
        CommandGroups.Clear();
        var groups = AvailableCommands
            .Where(c => c.Group != "Lines")
            .Where(c => c.Scope != TextEditScope.Selection || _hasSelection)
            .GroupBy(c => c.Group);
        foreach (var group in groups)
            CommandGroups.Add(new EditorCommandGroupVm(
                group.Key,
                group.Select(c => new EditorCommandVm(c, this)).ToList()));
    }

    /// <summary>Called by the view when the editor selection changes; rebuilds the panel only when the
    /// has-selection state actually flips, so dragging within a selection doesn't churn the menu.</summary>
    public void OnSelectionChanged(bool hasSelection)
    {
        if (hasSelection == _hasSelection) return;
        _hasSelection = hasSelection;
        RebuildCommandGroups();
    }

    internal void RunEditorCommand(ITextEditorCommand cmd)
    {
        var area = EditorAccess?.Invoke();
        if (area is null) return;

        var selection = area.Selection;
        var ctx = new TextEditorContext
        {
            DocumentText     = Document.Text,
            SelectedText     = selection.GetText(),
            HasSelection     = !selection.IsEmpty,
            CanEdit          = !IsReadOnlyMode,
            Encoding         = SelectedEncoding.Encoding,
            Eol              = SelectedEol.Eol,
            ReplaceDocument  = t => Document.Text = t,
            ReplaceSelection = t => area.Selection.ReplaceSelectionWithText(t),
            ShowResult       = (label, value) =>
            {
                try { Clipboard.SetText(value); } catch { /* clipboard briefly locked */ }
                Shell.ShowNotification($"{label}: {value} (copied)"); // toasts via the notification service
            },
            ShowError        = Shell.ShowError,
        };

        if (!cmd.CanExecute(ctx))
        {
            Shell.ShowError(cmd.Scope == TextEditScope.Selection && !ctx.HasSelection
                ? "Select some text first."
                : "The document is read-only.");
            return;
        }

        cmd.Execute(ctx);
        RecomputeLineEndingLabel(); // an EOL-conversion command may have changed the terminator
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => LineCount = Document.LineCount;

    // Dirty follows AvalonEdit's undo-stack "original file" marker, so undoing every edit back to the saved
    // state clears the unsaved flag (a plain "any edit ⇒ dirty" flag would stay stuck on after such an undo).
    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(UndoStack.IsOriginalFile))
            IsDirty = !Document.UndoStack.IsOriginalFile;
    }

    /// <summary>Recomputes the footer line-ending indicator from the current buffer.</summary>
    private void RecomputeLineEndingLabel() => LineEndingLabel = TextTransforms.DetectLineEnding(Document.Text) switch
    {
        LineEndingKind.Lf    => "LF",
        LineEndingKind.CrLf  => "CRLF",
        LineEndingKind.Cr    => "CR",
        LineEndingKind.Mixed => "Mixed",
        _                    => "—",
    };

    // ── Loading ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            // A not-yet-existing path is a new/blank file — start empty, editable, watched once saved.
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                SetDocumentText(string.Empty);
                IsDirty = false;
                return;
            }

            var info = new FileInfo(FilePath);
            FileSizeText = SizeFormatter.FormatBytes(info.Length);

            if (info.Length > _maxEditableBytes)
            {
                IsReadOnlyMode = true;
                ReadOnlyReason = $"This file is {SizeFormatter.FormatBytes(info.Length)} — too large to edit. "
                               + "Open it As Text, or split it into smaller files first.";
                SetDocumentText(string.Empty);
                return;
            }

            EncodingProbe probe;
            await using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                probe = EncodingDetector.Detect(fs);
            SyncEncodingSelection(probe);

            var text = await File.ReadAllTextAsync(FilePath, SelectedEncoding.Encoding, ct);
            SetDocumentText(text);
            IsDirty = false;
            StartWatching();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsReadOnlyMode = true;
            ReadOnlyReason = $"Could not open file: {ex.Message}";
            SetDocumentText(string.Empty);
        }
    }

    private void SetDocumentText(string text)
    {
        Document.Text = text;
        LineCount = Document.LineCount;
        // A programmatic (re)load is the new saved baseline: clear the undo history and mark it original so the
        // buffer is clean and the user can't undo past the loaded content.
        Document.UndoStack.ClearAll();
        Document.UndoStack.MarkAsOriginalFile();
        IsDirty = false;
        RecomputeLineEndingLabel();
    }

    /// <summary>Reflects the detected encoding in the selector without triggering a re-read.</summary>
    private void SyncEncodingSelection(EncodingProbe probe)
    {
        var match = probe.Encoding switch
        {
            { CodePage: 1200 }  => "UTF-16 LE",
            { CodePage: 1201 }  => "UTF-16 BE",
            _ when probe.Encoding.Equals(Encoding.Latin1) => "Latin-1",
            _ when probe.HadBom => "UTF-8 with BOM",
            _                    => "UTF-8",
        };
        var option = AvailableEncodings.Find(e => e.Name == match);
        if (option is null) return;

        _applyingEncoding = true;
        SelectedEncoding = option;
        _applyingEncoding = false;
    }

    partial void OnSelectedEncodingChanged(EncodingOption value)
    {
        if (_applyingEncoding) return; // selector being synced to the detected encoding, not a user re-decode
        _ = ReloadForEncodingAsync();
    }

    private async Task ReloadForEncodingAsync()
    {
        if (IsReadOnlyMode || string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return;
        if (IsDirty && !await Shell.ConfirmAsync("Change encoding",
                "Re-read the file with the new encoding and discard your unsaved changes?"))
            return;
        await LoadAsync();
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsReadOnlyMode || string.IsNullOrEmpty(FilePath)) return;
        try
        {
            var text = TextTransforms.NormalizeLineEndings(Document.Text, SelectedEol.Eol);
            await File.WriteAllTextAsync(FilePath, text, SelectedEncoding.Encoding);
            Document.UndoStack.MarkAsOriginalFile(); // current buffer is now the saved state ⇒ clean
            FileSizeText = SizeFormatter.FormatBytes(new FileInfo(FilePath).Length);
            StartWatching(); // a brand-new file now exists to watch
        }
        catch (Exception ex)
        {
            Shell.ShowError($"Could not save '{FileName}': {ex.Message}");
        }
    }

    private bool CanSave() => IsDirty && !IsReadOnlyMode;

    // ── Reload (F5) ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsDirty && !await Shell.ConfirmAsync("Reload file",
                "Reload from disk and discard your unsaved changes?"))
            return;
        await LoadAsync();
    }

    // ── File watching ────────────────────────────────────────────────────────────

    private void StartWatching()
    {
        if (_watch is not null || string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return;
        _watch = Shell.WatchFile(FilePath, OnFileChanged);
    }

    // Invoked by the shell on the workspace UI thread when the watched file changes. We compare on-disk
    // content to the buffer rather than toggling the watcher, so our own saves (which leave them equal)
    // are ignored regardless of debounce timing, while real external edits reload (with a dirty prompt).
    private async void OnFileChanged()
    {
        string onDisk;
        try { onDisk = await File.ReadAllTextAsync(FilePath, SelectedEncoding.Encoding); }
        catch { return; } // file briefly locked/removed — ignore this burst
        if (onDisk == Document.Text) return; // our own write, or a no-op change

        if (IsDirty && !await Shell.ConfirmAsync("File changed on disk",
                "Reload and discard your unsaved changes?"))
            return;

        SetDocumentText(onDisk);
        IsDirty = false;
        try { FileSizeText = SizeFormatter.FormatBytes(new FileInfo(FilePath).Length); } catch { }
        BannerMessage = "Reloaded — the file changed on disk.";
        BannerVisible = true;
        await Task.Delay(3000);
        BannerVisible = false;
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────────

    public virtual string GetContext()
    {
        var name = string.IsNullOrEmpty(FileName) ? "Untitled" : FileName;
        var sample = Document.Text;
        if (sample.Length > 4000) sample = sample[..4000] + "\n…(truncated)";
        return $"Editing text file '{name}' at '{FilePath}'.\n{sample}";
    }

    public virtual IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool(
            "get_editor_text",
            "Return the full current text of the open editor document.",
            [],
            ToolSafety.ReadOnly,
            (_, _) => Task.FromResult(ToolResult.Ok(Document.Text, "Read the editor contents."))),

        new DelegateClientTool(
            "get_syntax_tree",
            "Return the tree-sitter parse tree (s-expression) of the current code document for structural understanding. Empty if the file has no code grammar.",
            [],
            ToolSafety.ReadOnly,
            (_, _) =>
            {
                var grammar = HighlightingRegistry.Resolve(FileName).TreeSitterLanguage;
                if (grammar is null)
                    return Task.FromResult(ToolResult.Ok(string.Empty, "This file has no code grammar."));
                using var highlighter = Nexaflow.Syntax.CodeHighlighter.TryCreate(grammar);
                var tree = highlighter?.GetParseTree(Document.Text);
                return Task.FromResult(ToolResult.Ok(tree ?? string.Empty, "Parsed the document."));
            }),
    ];

    public virtual IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext { RootPath = dir, CurrentPath = dir, SelectedItems = [FilePath] };
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Document.TextChanged -= OnDocumentTextChanged;
        Document.UndoStack.PropertyChanged -= OnUndoStackChanged;
        _watch?.Dispose();
        GC.SuppressFinalize(this);
    }
}
