using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.PlainText;
using Nexaflow.Syntax;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Localization.Formatting;
using Nexaflow.Visuals.Text.Editor.Commands;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Localization;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// Shared view-model for the read-write code/text editor: the file held as an <see cref="EditedDocument"/> and shown through one
/// <see cref="ContentEditor"/> — the caret, what is picked out, what a search found and which lines are on screen are this page's, whatever
/// draws them — with encoding/EOL-aware save, F5 reload, and a file watcher that reloads on external change (prompting first when there are
/// unsaved edits) while ignoring the editor's own writes. A file past the editable ceiling is shown read-only over its blocks on disk
/// (<see cref="ContentFile"/>), never read whole.
///
/// Concrete and unsealed (not abstract) so the generic editor uses it directly, and the "As Code" editor
/// subclasses it (see <c>CodeViewModel</c>) to add its structure panel.
/// </summary>
public partial class FileTextEditorViewModel : ObservableObject, IPageViewModel, IContextPreview, IDisposable
{
    /// <summary>
    /// How much of a file read over its blocks its line endings are judged by: its start — reading all of it to say is what reading it over
    /// its blocks is for not doing.
    /// </summary>
    private const int LineEndingSample = 64 * 1024;

    protected readonly IShellServices Shell;

    /// <summary>The runtime's file system the file is read and saved through — on disk, in an archive or on a location.</summary>
    protected readonly IVirtualFileSystem Vfs;

    private readonly long _maxEditableBytes;

    /// <summary>The grammar the file is read in, or null for plain text.</summary>
    private readonly string? _grammar;

    private IFileWatch? _watch;
    private bool _applyingEncoding;     // true while syncing the encoding selector to the detected encoding
    private bool _hasSelection;         // mirrors the editor's selection (drives selection-only commands)

    /// <summary>The file a document past the editable ceiling is read over, block by block — null while the document is held whole.</summary>
    private ContentFile? _file;

    /// <summary>The file as it is written in: what is shown, saved, searched and handed to the assistant.</summary>
    public EditedDocument Document { get; } = new();

    /// <summary>This page's view of <see cref="Document"/>: its caret, what is picked out, what a search found, and which lines are on screen.</summary>
    public ContentEditor Editor { get; }

    /// <summary>What the file is written in, as the surface reads it: its grammar, or plain text where no grammar reads it.</summary>
    public string WrittenIn => _grammar ?? TextParser.Language;

    /// <summary>False until the initial file load finishes (success OR failure). Gates the AI send so the
    /// model is never handed an empty pre-load document.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

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

    /// <summary>Whether spaces, tabs and line ends are shown as faint marks.</summary>
    [ObservableProperty] private bool _showWhitespace;

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
        new("UTF-8 with BOM", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Str.Get("Text.Editor.Encoding.Utf8Bom")),
        new("UTF-16 LE",      Encoding.Unicode),
        new("UTF-16 BE",      Encoding.BigEndianUnicode),
        new("Latin-1",        Encoding.Latin1),
        new("System Default", Encoding.Default, Str.Get("Text.Editor.Encoding.SystemDefault")),
    ];

    // ── Line endings ─────────────────────────────────────────────────────────────
    [ObservableProperty] private LineEndingOption _selectedEol;
    public List<LineEndingOption> AvailableEols { get; } =
    [
        new("Preserve EOL", LineEnding.Preserve, Str.Get("Text.Editor.Eol.Preserve")),
        new("LF",           LineEnding.Lf),
        new("CRLF",         LineEnding.CrLf),
        new("CR",           LineEnding.Cr),
    ];

    public FileTextEditorViewModel(string filePath, IShellServices shell, IVirtualFileSystem vfs, long maxEditableBytes)
    {
        Shell = shell;
        Vfs   = vfs;
        _maxEditableBytes = maxEditableBytes;
        _selectedEncoding = AvailableEncodings[0];
        _selectedEol = AvailableEols[0];
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        _grammar = TreeSitterLanguages.ForEdit(FileName);
        IsPlainText = _grammar is null;

        Editor = new ContentEditor(Document);
        if (_grammar is not null) Editor.Marks.Add(new ColourSwatchMarks(_grammar));

        _searched = new ShownFile(Editor);
        _find = new FileFind(_searched, Editor.Found);
        _find.Changed += OnFindChanged;

        Document.Changed += OnDocumentChanged;
        Document.PropertyChanged += OnDocumentPropertyChanged; // dirty = "differs from the saved state"
        Editor.SelectionChanged += OnEditorSelectionChanged;
        BuildCommands();
    }

    /// <summary>True when no grammar reads the file: prose or data, where the line-reordering commands mean something.</summary>
    public bool IsPlainText { get; }

    /// <summary>The whole document as one string — null for a file read over its blocks, which is never made one.</summary>
    protected string? WholeText() => _file is null ? Document.ToString() : null;

    // ── Command groups (Checksum / Encode / Decode + per-subclass extras; Lines lives in the footer) ──

    /// <summary>The floating panel's command-group dropdowns (Checksum / Encode / Decode). Selection-only
    /// groups and entries are dropped when there is no selection, so the panel never offers a no-op.</summary>
    public ObservableCollection<EditorCommandGroupVm> CommandGroups { get; } = [];

    /// <summary>The "Lines" group, surfaced from the footer's line-ending indicator (EOL conversions, plus
    /// prose line ops for plain text). Built again only when the file comes to be held whole or not.</summary>
    public IReadOnlyList<EditorCommandVm> LineCommands { get; private set; } = [];

    /// <summary>
    /// Puts the caret at the start of <paramref name="line"/> (1-based) and brings it onto the page — the structure panel's jump to a member,
    /// a search's step to its next match. Nothing, for a line the document does not have.
    /// </summary>
    public void MoveCaretToLine(int line)
    {
        var document = Document.Current;
        if (line < 1 || line > document.LineCount) return;

        Editor.MoveCaretTo(document.OffsetOf(line - 1));
        Editor.ScrollToLine(line - 1);
    }

    /// <summary>Override to contribute viewer-specific commands; merged with the built-ins.</summary>
    protected virtual IEnumerable<ITextEditorCommand> ProvideCommands() => [];

    /// <summary>
    /// The commands available for this file: built-ins + subclass extras, minus line-reordering/munging for code/markup (only meaningful on
    /// prose/data) — and none for a file read over its blocks, which every command would first have to make one string.
    /// </summary>
    private IEnumerable<ITextEditorCommand> AvailableCommands =>
        _file is not null ? [] : BuiltInTextEditorCommands.All.Concat(ProvideCommands()).Where(c => IsPlainText || !c.PlainTextOnly);

    private void BuildCommands()
    {
        LineCommands = AvailableCommands.Where(c => c.Group == "Lines")
            .Select(c => new EditorCommandVm(c, this)).ToList();
        OnPropertyChanged(nameof(LineCommands));
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
                group.First().GroupTitle,
                group.Select(c => new EditorCommandVm(c, this)).ToList()));
    }

    /// <summary>The panel is rebuilt only when whether anything is picked out flips, so dragging within a selection doesn't churn the menu.</summary>
    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (Editor.HasSelection == _hasSelection) return;
        _hasSelection = Editor.HasSelection;
        RebuildCommandGroups();
    }

    internal void RunEditorCommand(ITextEditorCommand cmd)
    {
        if (WholeText() is not { } text) return;

        var ctx = new TextEditorContext
        {
            DocumentText     = text,
            SelectedText     = Editor.SelectedText(),
            HasSelection     = Editor.HasSelection,
            CanEdit          = !IsReadOnlyMode,
            Encoding         = SelectedEncoding.Encoding,
            Eol              = SelectedEol.Eol,
            ReplaceDocument  = ReplaceAll,
            ReplaceSelection = Editor.ReplaceSelection,
            ShowResult       = (label, value) => _ = CopyResultAsync(label, value),
            ShowError        = Shell.ShowError,
        };

        if (!cmd.CanExecute(ctx))
        {
            Shell.ShowError(cmd.Scope == TextEditScope.Selection && !ctx.HasSelection
                ? Str.Get("Text.Editor.SelectFirst")
                : Str.Get("Text.Editor.DocumentReadOnly"));
            return;
        }

        cmd.Execute(ctx);
        RecomputeLineEndingLabel(); // an EOL-conversion command may have changed the terminator
    }

    /// <summary>Writes <paramref name="text"/> in place of everything, as one step that can be taken back.</summary>
    private void ReplaceAll(string text) => Document.Write(ContentChange.Write(0, Document.Current.Length, text));

    /// <summary>A command's result, put on the clipboard and told — through the notification service's toast — once it is there.</summary>
    private async Task CopyResultAsync(string label, string value)
    {
        if (await Shell.Clipboard.WriteAsync(ComposedPayload.OfText(value)))
            Shell.ShowNotification(Str.Format("Text.Editor.Copied", label, value));
    }

    private void OnDocumentChanged(object? sender, ContentSourceChange change)
    {
        LineCount = Document.Current.LineCount;
    }

    // Dirty follows the document, which compares what it holds with what was saved — so undoing every edit back to the saved state clears
    // the unsaved flag, where a plain "any edit ⇒ dirty" flag would stay stuck on.
    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(EditedDocument.IsDirty))
            IsDirty = Document.IsDirty;
    }

    /// <summary>Recomputes the footer line-ending indicator from the document — from its start, for a file read over its blocks.</summary>
    private void RecomputeLineEndingLabel()
    {
        var document = Document.Current;
        var text = WholeText() ?? document.Slice(0, Math.Min(document.Length, LineEndingSample));

        LineEndingLabel = TextTransforms.DetectLineEnding(text) switch
        {
            LineEndingKind.Lf    => "LF",
            LineEndingKind.CrLf  => "CRLF",
            LineEndingKind.Cr    => "CR",
            LineEndingKind.Mixed => Str.Get("Text.Editor.Eol.Mixed"),
            _                    => "—",
        };
    }

    // ── Loading ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            // A not-yet-existing path is a new/blank file — start empty, editable, watched once saved.
            if (string.IsNullOrEmpty(FilePath) || await Vfs.GetEntryAsync(FilePath, ct) is not { IsDirectory: false } entry)
            {
                Show(ContentDocument.Of(string.Empty));
                return;
            }

            long length  = entry.Size;
            FileSizeText = SizeFormatter.FormatBytes(length);

            if (length > _maxEditableBytes)
            {
                IsReadOnlyMode = true;
                ReadOnlyReason = Str.Format("Text.Editor.TooLarge", SizeFormatter.FormatBytes(length));
                await ShowOverItsBlocksAsync(ct);
                StartWatching();
                return;
            }

            // Read once — on a location every open is a round trip — then probe and decode the same bytes.
            var bytes = await Vfs.ReadAllBytesAsync(FilePath, ct);
            EncodingProbe probe;
            using (var fs = new MemoryStream(bytes, writable: false))
                probe = EncodingDetector.Detect(fs);
            SyncEncodingSelection(probe);

            string text;
            using (var rs = new MemoryStream(bytes, writable: false))
            using (var sr = new StreamReader(rs, SelectedEncoding.Encoding))
                text = await sr.ReadToEndAsync(ct);
            Show(ContentDocument.Of(text));
            StartWatching();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsReadOnlyMode = true;
            ReadOnlyReason = Str.Format("Text.Editor.CouldNotOpen", ex.Message);
            Show(ContentDocument.Of(string.Empty));
        }
        finally { IsLoaded = true; }
    }

    [LocalDiskOnly("the file as it is on this disk - MaterializeFileAsync copies an archive's entry or a location's file here first, and a file on this disk is itself")]

    /// <summary>
    /// Shows the file read over its blocks on disk — a copy on disk first, for an archive's entry or a location's file — its blocks found off
    /// this thread, so a file of any length opens holding a screen's worth of it. One longer than a document can hold is not shown.
    /// </summary>
    private async Task ShowOverItsBlocksAsync(CancellationToken ct)
    {
        var onDisk = await Vfs.MaterializeFileAsync(FilePath, ct);

        EncodingProbe probe;
        await using (var head = new FileStream(onDisk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            probe = EncodingDetector.Detect(head);
        SyncEncodingSelection(probe);

        var encoding = SelectedEncoding.Encoding;
        var file = ContentFile.Open(onDisk, encoding, probe.HadBom ? encoding.GetPreamble().Length : 0, ContentFileAccess.ReadOnly);

        try
        {
            await Task.Run(() => file.Scan(cancel: ct), ct);
        }
        catch
        {
            file.Dispose();
            throw;
        }

        if (file.Characters > int.MaxValue)
        {
            file.Dispose();
            ReadOnlyReason = Str.Format("Text.Editor.TooLong", FileSizeText);
            Show(ContentDocument.Of(string.Empty));
            return;
        }

        Show(ContentDocument.Of(file), file);
    }

    /// <summary>
    /// Shows <paramref name="document"/> as the file's new saved baseline, nothing before it to take back — over <paramref name="file"/>,
    /// where it is read over its blocks — and lets go of the file the document before it was read over.
    /// </summary>
    private void Show(ContentDocument document, ContentFile? file = null)
    {
        var before = _file;
        _file = file;

        Document.Load(document);
        before?.Dispose();

        IsDirty = Document.IsDirty;
        if ((before is null) != (file is null)) BuildCommands(); // the commands come and go with whether the file is held whole
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
        if (IsReadOnlyMode || string.IsNullOrEmpty(FilePath) || !await Vfs.ExistsAsync(FilePath)) return;
        if (IsDirty && !await Shell.ConfirmAsync(Str.Get("Text.Editor.ChangeEncoding"),
                Str.Get("Text.Editor.ChangeEncodingAsk")))
            return;
        await LoadAsync();
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsReadOnlyMode || string.IsNullOrEmpty(FilePath) || WholeText() is not { } written) return;
        try
        {
            var text = TextTransforms.NormalizeLineEndings(written, SelectedEol.Eol);
            var enc  = SelectedEncoding.Encoding;
            // A plain file write for real paths, an archive rewrite for an in-archive entry, a whole-file write on a location.
            await Vfs.WriteAllTextAsync(FilePath, text, enc);
            Document.MarkSaved(); // what it holds is now the saved state ⇒ clean
            if (await Vfs.GetEntryAsync(FilePath) is { } saved) FileSizeText = SizeFormatter.FormatBytes(saved.Size);
            StartWatching(); // a brand-new file now exists to watch
        }
        catch (Exception ex)
        {
            Shell.ShowError(Str.Format("Text.Editor.CouldNotSave", FileName, ex.Message));
        }
    }

    private bool CanSave() => IsDirty && !IsReadOnlyMode;

    // ── Reload (F5) ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsDirty && !await Shell.ConfirmAsync(Str.Get("Text.Editor.Reload"),
                Str.Get("Text.Editor.ReloadAsk")))
            return;
        await LoadAsync();
    }

    // ── File watching ────────────────────────────────────────────────────────────

    private void StartWatching()
    {
        // Called once the file is known to be there — after a load or a save — and the shell watches it wherever it is.
        if (_watch is not null || string.IsNullOrEmpty(FilePath)) return;
        _watch = Shell.WatchFile(FilePath, OnFileChanged);
    }

    // Invoked by the shell on the workspace UI thread when the watched file changes. We compare on-disk
    // content to the buffer rather than toggling the watcher, so our own saves (which leave them equal)
    // are ignored regardless of debounce timing, while real external edits reload (with a dirty prompt).
    // A file read over its blocks is read-only, so nothing here wrote it: it is read again, never compared whole.
    private async void OnFileChanged()
    {
        if (_file is not null)
        {
            await LoadAsync();
            await AnnounceReloadAsync();
            return;
        }

        string onDisk;
        try { onDisk = await Vfs.ReadAllTextAsync(FilePath, SelectedEncoding.Encoding); }
        catch { return; } // file briefly locked/removed — ignore this burst
        if (onDisk == Document.ToString()) return; // our own write, or a no-op change

        if (IsDirty && !await Shell.ConfirmAsync(Str.Get("Text.Editor.ChangedOnDisk"),
                Str.Get("Text.Editor.ChangedOnDiskAsk")))
            return;

        Show(ContentDocument.Of(onDisk));
        try { if (await Vfs.GetEntryAsync(FilePath) is { } changed) FileSizeText = SizeFormatter.FormatBytes(changed.Size); } catch { }
        await AnnounceReloadAsync();
    }

    /// <summary>Says, for a few seconds, that the file was read again because it changed on disk.</summary>
    private async Task AnnounceReloadAsync()
    {
        BannerMessage = Str.Get("Text.Editor.Reloaded");
        BannerVisible = true;
        await Task.Delay(3000);
        BannerVisible = false;
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────────

    public virtual string GetContext()
    {
        var name = string.IsNullOrEmpty(FileName) ? "Untitled" : FileName;
        var document = Document.Current;
        var sample = document.Length > 4000 ? document.Slice(0, 4000) + "\n…(truncated)" : document.ToString();
        return $"Editing text file '{name}' at '{FilePath}'.\n{sample}";
    }

    /// <summary>What a tool that reads the whole file answers for one read over its blocks: there is no whole of it to hand over.</summary>
    private static ToolResult TooLongToRead() =>
        ToolResult.Error("This file is too large to read whole. Search it for what you need instead.");

    public virtual IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) ──────────────────────────────────────────
        new DelegateClientTool(
            "get_editor_text",
            "Return the full current text of the open editor document.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(WholeText() is { } text ? ToolResult.Ok(text, "Read the editor contents.") : TooLongToRead())),

        new DelegateClientTool(
            "get_syntax_tree",
            "Return the tree-sitter parse tree (s-expression) of the current code document for structural understanding. Empty if the file has no code grammar.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (_grammar is not { } grammar)
                    return Task.FromResult(ToolResult.Ok(string.Empty, "This file has no code grammar."));
                if (WholeText() is not { } text) return Task.FromResult(TooLongToRead());
                using var highlighter = Nexaflow.Syntax.CodeHighlighter.TryCreate(grammar);
                var tree = highlighter?.GetParseTree(text);
                return Task.FromResult(ToolResult.Ok(tree ?? string.Empty, "Parsed the document."));
            }),

        new DelegateClientTool(
            "list_declarations",
            "List the types and members this file declares, each with the ast_path that addresses it, its "
            + "kind and its line range. The way to find something to edit structurally — read this before "
            + "calling edit_declaration. Empty if the file has no code grammar.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                if (_grammar is not { } grammar)
                    return Task.FromResult(ToolResult.Ok(string.Empty, "This file has no code grammar."));
                if (WholeText() is not { } text) return Task.FromResult(TooLongToRead());

                var found = Nexaflow.Syntax.StructuralEdit.Declarations(grammar, text);
                if (found.Count == 0)
                    return Task.FromResult(ToolResult.Ok("none", "This file declares nothing the parser recognises."));

                var listing = string.Join("\n",
                    found.Select(d => $"{d.Line,5}-{d.EndLine,-5} {d.Kind,-11} {d.Name,-32} {d.AstPath}"));
                return Task.FromResult(ToolResult.Ok($"{found.Count} declaration(s)", listing));
            }),

        // ── Edit / save (approval-gated) — write in the live document ──
        new DelegateClientTool(
            "edit_declaration",
            "Change ONE declaration — replace it, delete it, change its signature without touching its body "
            + "(or the reverse), rename it, substitute text inside it, insert one beside it, append a member "
            + "to a type, or rewrite its doc comment. Prefer this over set_editor_text and replace_all: it "
            + "edits exactly the declaration named and nothing else, and it re-parses the result and refuses "
            + "rather than leaving the file broken. Do not worry about indentation or line endings — write "
            + "the replacement flush-left with \\n and it lands correctly indented with the file's own "
            + "endings. Get ast_path from list_declarations — and if the file has changed since you listed "
            + "it, a path that has moved is re-found by name rather than refused, so an older listing is "
            + "usually still fine.",
            [
                new ClientToolParameter("ast_path",
                    "Which declaration, from list_declarations (e.g. 'T:C/M:Add'). Not needed for 'import'.",
                    Required: false),
                new ClientToolParameter("op",
                    "replace | delete | signature | body | rename | insert_before | insert_after | append | "
                  + "doc | substitute | import. 'append' targets a type and adds a member at the end of its "
                  + "body; 'signature' and 'body' each leave the other half byte-for-byte unchanged; "
                  + "'import' adds a using/import where the file already keeps them (pass it in 'text')."),
                new ClientToolParameter("text", "The new code — or, for 'substitute', the replacement. Not needed for 'delete'.", Required: false),
                new ClientToolParameter("to", "The new name, for 'rename'.", Required: false),
                new ClientToolParameter("at",
                    "For an XML-family file (.xaml .xml .props .csproj …), instead of ast_path: an XPath naming the one "
                  + "element to edit - /a/b, //b, [n], [@attr], [@attr='v'], [child].", Required: false),
                new ClientToolParameter("name", "The attribute, for 'set_attribute' and 'remove_attribute'.", Required: false),
                new ClientToolParameter("find",
                    "For 'substitute': the text to find, searched only INSIDE this declaration. Literal "
                  + "unless find_is_regex, and refused unless it matches exactly once — use it to change one "
                  + "line without restating the whole method. Indentation need not match: paste the fragment "
                  + "as you read it, or flush-left, and it will still be found. If it is not there, the "
                  + "refusal names the declaration that does contain it.", Required: false),
                new ClientToolParameter("find_is_regex", "Treat 'find' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("all_occurrences", "Allow 'find' to match more than once.", Required: false, Type: "boolean"),
                new ClientToolParameter("with_trivia", "For 'replace', also replace the doc comment above it.", Required: false, Type: "boolean"),
                new ClientToolParameter("expect",
                    "Refuse unless the declaration currently contains this text. Worth passing when editing "
                  + "an overload (an ast_path with #N) after an earlier edit added or removed one, since #N "
                  + "is a position and the others will have renumbered.", Required: false),
            ],
            ToolSafety.RequiresApproval,
            (args, _) =>
            {
                if (IsReadOnlyMode || WholeText() is not { } source) return Task.FromResult(ToolResult.Error("This file can't be edited."));
                if (_grammar is not { } grammar)
                    return Task.FromResult(ToolResult.Error(
                        "This file has no code grammar, so it has no declarations to edit structurally. Use "
                      + "replace_all or set_editor_text."));

                if (ParseEditOp(ToolArgs.Str(args, "op", "operation")) is not { } op)
                    return Task.FromResult(ToolResult.Error(
                        $"Unknown 'op' '{ToolArgs.Str(args, "op", "operation")}'. Expected replace, delete, "
                      + "signature, body, rename, insert_before, insert_after, append, doc, substitute, import, set_attribute or "
                      + "remove_attribute."));

                // 'import' is the one op that belongs to the file rather than to a declaration, so it is the
                // one that needs no ast_path.
                var path = ToolArgs.Str(args, "ast_path", "path", "declaration");
                var at   = ToolArgs.Str(args, "at");
                if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(at))
                    return Task.FromResult(ToolResult.Error("Give 'ast_path' or 'at', not both - each names the element on its own."));
                if (op is not Nexaflow.Syntax.StructuralEdit.Op.Import && string.IsNullOrEmpty(path) && string.IsNullOrEmpty(at))
                    return Task.FromResult(ToolResult.Error("No 'ast_path' provided — call list_declarations first."));

                var options = new Nexaflow.Syntax.StructuralEdit.Options(
                    ToolArgs.Bool(args, "with_trivia"),
                    ToolArgs.Str(args, "expect"),
                    ToolArgs.Raw(args, "find"),
                    ToolArgs.Bool(args, "find_is_regex"),
                    ToolArgs.Bool(args, "all_occurrences"),
                    At: at,
                    Attribute: ToolArgs.Str(args, "name", "attribute"));

                // Raw, not Str: replacement code is whitespace-significant, and trimming it would silently
                // reflow whatever the caller wrote.
                var text   = ToolArgs.Raw(args, "text", "content", "new_text");
                var result = op is Nexaflow.Syntax.StructuralEdit.Op.Import
                    ? Nexaflow.Syntax.StructuralEdit.AddImport(grammar, source, text ?? "")
                    : !string.IsNullOrEmpty(at)
                        ? Nexaflow.Syntax.StructuralEdit.ApplyAt(grammar, source, op, text, options, ToolArgs.Str(args, "to"))
                        : Nexaflow.Syntax.StructuralEdit.Apply(grammar, source, path!, op, text,
                                                               options, ToolArgs.Str(args, "to"));

                if (!result.Ok || result.Change is not { } change)
                    return Task.FromResult(ToolResult.Error(result.Message));

                // One write rather than the whole document again: a single step to take back, and the caret and
                // the page stay where they were.
                Document.Write(ContentChange.Write(change.Offset, change.Length, change.Inserted));

                var report = string.Join("\n",
                    [$"--- line {result.Hunk!.Line}",
                     .. result.Hunk.Removed.Select(l => "- " + l),
                     .. result.Hunk.Added.Select(l => "+ " + l),
                     .. result.Notes.Select(n => "note: " + n),
                     "Unsaved — call save_file to persist."]);
                return Task.FromResult(ToolResult.Ok(result.Message, report));
            }),

        new DelegateClientTool(
            "set_editor_text",
            "Replace the entire document with new text (read it first with get_editor_text). Unsaved — call save_file to persist.",
            [new ClientToolParameter("text", "The full new document text.")],
            ToolSafety.RequiresApproval,
            (args, _) =>
            {
                if (IsReadOnlyMode) return Task.FromResult(ToolResult.Error("This file can't be edited."));
                ReplaceAll(ToolArgs.Raw(args, "text", "content", "new_text") ?? string.Empty);
                return Task.FromResult(ToolResult.Ok("document replaced",
                    $"Replaced the document ({Document.Current.LineCount} lines). Unsaved — call save_file to persist."));
            }),

        new DelegateClientTool(
            "replace_all",
            "Find and replace across the whole document (set regex=true for a regular expression). Returns the number replaced. Unsaved — call save_file.",
            [
                new ClientToolParameter("find", "Text or regex to find."),
                new ClientToolParameter("replace", "Replacement text.", Required: false),
                new ClientToolParameter("regex", "Treat 'find' as a regular expression.", Required: false, Type: "boolean"),
                new ClientToolParameter("case_sensitive", "Match case.", Required: false, Type: "boolean"),
            ],
            ToolSafety.RequiresApproval,
            (args, _) =>
            {
                if (IsReadOnlyMode || WholeText() is not { } original) return Task.FromResult(ToolResult.Error("This file can't be edited."));
                var find = ToolArgs.Str(args, "find", "pattern", "search");
                if (string.IsNullOrEmpty(find)) return Task.FromResult(ToolResult.Error("No 'find' provided."));
                var repl = ToolArgs.Str(args, "replace", "replacement", "to") ?? string.Empty;
                var rx   = ToolArgs.Bool(args, "regex");
                var cs   = ToolArgs.Bool(args, "case_sensitive");
                try
                {
                    var re = new Regex(rx ? find : Regex.Escape(find), cs ? RegexOptions.None : RegexOptions.IgnoreCase);
                    var count = re.Matches(original).Count;
                    if (count > 0) ReplaceAll(re.Replace(original, rx ? repl : repl.Replace("$", "$$")));
                    return Task.FromResult(ToolResult.Ok($"replaced {count}",
                        $"Replaced {count} occurrence(s). Unsaved — call save_file to persist."));
                }
                catch (ArgumentException ex) { return Task.FromResult(ToolResult.Error($"Invalid regex: {ex.Message}")); }
            }),

        new DelegateClientTool(
            "save_file",
            "Save the document to disk (encoding/EOL-aware).",
            [],
            ToolSafety.RequiresApproval,
            async (_, _) =>
            {
                if (IsReadOnlyMode) return ToolResult.Error("This file can't be edited.");
                if (!IsDirty) return ToolResult.Ok("nothing to save", "There are no unsaved edits.");
                await SaveCommand.ExecuteAsync(null);
                return ToolResult.Ok("saved", $"Saved {FileName}.");
            }),
    ];

    private static Nexaflow.Syntax.StructuralEdit.Op? ParseEditOp(string? op) =>
        op?.Replace('-', '_').ToLowerInvariant() switch
        {
            "replace"       => Nexaflow.Syntax.StructuralEdit.Op.Replace,
            "delete"        => Nexaflow.Syntax.StructuralEdit.Op.Delete,
            "signature"     => Nexaflow.Syntax.StructuralEdit.Op.Signature,
            "body"          => Nexaflow.Syntax.StructuralEdit.Op.Body,
            "rename"        => Nexaflow.Syntax.StructuralEdit.Op.Rename,
            "insert_before" => Nexaflow.Syntax.StructuralEdit.Op.InsertBefore,
            "insert_after"  => Nexaflow.Syntax.StructuralEdit.Op.InsertAfter,
            "append"        => Nexaflow.Syntax.StructuralEdit.Op.Append,
            "doc"           => Nexaflow.Syntax.StructuralEdit.Op.Doc,
            "substitute" or "sub" => Nexaflow.Syntax.StructuralEdit.Op.Substitute,
            "import" or "using"   => Nexaflow.Syntax.StructuralEdit.Op.Import,
            "set_attribute" or "set_attr"       => Nexaflow.Syntax.StructuralEdit.Op.SetAttribute,
            "remove_attribute" or "remove_attr" => Nexaflow.Syntax.StructuralEdit.Op.RemoveAttribute,
            _               => null,
        };

    public virtual IContext? GetContextObject()
    {
        if (string.IsNullOrEmpty(FilePath)) return null;
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir)) return null;
        return new FileSystemContext { RootPath = dir, CurrentPath = dir, SelectedItems = [FilePath] };
    }

    /// <summary>Held until the initial load finishes, so the AI never sees an empty pre-load document.</summary>
    public virtual bool IsContextReady => IsLoaded;

    /// <summary>The file this editor's tools act within — disambiguates two editor tabs on different files
    /// pinned into one conversation (so their identically-named tools don't collapse first-wins).</summary>
    public virtual string? GetSecurityContext() => string.IsNullOrEmpty(FilePath) ? null : FilePath;

    /// <summary>The file this editor shows.</summary>
    public virtual ContextGrant GetContextGrant() => ContextGrant.OfFile(FilePath);

    /// <summary>A compact, read-only preview for the conversation context panel — file name, a meta line, and a
    /// capped monospace snippet. Built fresh each time; it never re-hosts the live editor.</summary>
    public object CreateContextPreview()
    {
        var meta = $"{LineCount:N0} line{(LineCount == 1 ? "" : "s")} · {SelectedEncoding.Name}"
                 + (IsDirty ? " · unsaved edits" : string.Empty)
                 + (IsReadOnlyMode ? " · read-only" : string.Empty);
        const int cap = 8000;
        var document = Document.Current;
        var body = document.Length > cap ? document.Slice(0, cap) + "\n… (preview truncated)" : document.ToString();
        return new TextContextPreview(meta, body);
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Document.Changed -= OnDocumentChanged;
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        Editor.SelectionChanged -= OnEditorSelectionChanged;
        _find.Changed -= OnFindChanged;
        _find.Dispose();
        _searched.Dispose();
        Editor.Dispose();
        _watch?.Dispose();
        _file?.Dispose();
        GC.SuppressFinalize(this);
    }
}
