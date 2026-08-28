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
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Controls;
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
public partial class FileTextEditorViewModel : ObservableObject, IPageViewModel, IContextPreview, IDisposable
{
    protected readonly IShellServices Shell;
    private readonly long _maxEditableBytes;

    private IFileWatch? _watch;
    private bool _applyingEncoding;     // true while syncing the encoding selector to the detected encoding
    private bool _hasSelection;         // mirrors the editor's live selection (drives selection-only commands)

    public TextDocument Document { get; } = new();

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
            if (string.IsNullOrEmpty(FilePath) || !VirtualFileSystem.Instance.Exists(FilePath))
            {
                SetDocumentText(string.Empty);
                IsDirty = false;
                return;
            }

            long length  = VirtualFileSystem.Instance.GetLength(FilePath);
            FileSizeText = SizeFormatter.FormatBytes(length);

            if (length > _maxEditableBytes)
            {
                IsReadOnlyMode = true;
                ReadOnlyReason = $"This file is {SizeFormatter.FormatBytes(length)} — too large to edit. "
                               + "Open it As Text, or split it into smaller files first.";
                SetDocumentText(string.Empty);
                return;
            }

            EncodingProbe probe;
            await using (var fs = VirtualFileSystem.Instance.OpenRead(FilePath))
                probe = EncodingDetector.Detect(fs);
            SyncEncodingSelection(probe);

            string text;
            await using (var rs = VirtualFileSystem.Instance.OpenRead(FilePath))
            using (var sr = new StreamReader(rs, SelectedEncoding.Encoding))
                text = await sr.ReadToEndAsync(ct);
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
        finally { IsLoaded = true; }
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
        if (IsReadOnlyMode || string.IsNullOrEmpty(FilePath) || !VirtualFileSystem.Instance.Exists(FilePath)) return;
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
            var enc  = SelectedEncoding.Encoding;
            // VFS write: a plain file write for real paths, or an archive rewrite for an in-archive entry.
            await Task.Run(() => VirtualFileSystem.Instance.WriteAllText(FilePath, text, enc));
            Document.UndoStack.MarkAsOriginalFile(); // current buffer is now the saved state ⇒ clean
            FileSizeText = SizeFormatter.FormatBytes(VirtualFileSystem.Instance.GetLength(FilePath));
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
        // ── Read / explore (auto-run) ──────────────────────────────────────────
        new DelegateClientTool(
            "get_editor_text",
            "Return the full current text of the open editor document.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok(Document.Text, "Read the editor contents."))),

        new DelegateClientTool(
            "get_syntax_tree",
            "Return the tree-sitter parse tree (s-expression) of the current code document for structural understanding. Empty if the file has no code grammar.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                var grammar = HighlightingRegistry.Resolve(FileName).TreeSitterLanguage;
                if (grammar is null)
                    return Task.FromResult(ToolResult.Ok(string.Empty, "This file has no code grammar."));
                using var highlighter = Nexaflow.Syntax.CodeHighlighter.TryCreate(grammar);
                var tree = highlighter?.GetParseTree(Document.Text);
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
                if (EditorGrammar is not { } grammar)
                    return Task.FromResult(ToolResult.Ok(string.Empty, "This file has no code grammar."));

                var found = Nexaflow.Syntax.StructuralEdit.Declarations(grammar, Document.Text);
                if (found.Count == 0)
                    return Task.FromResult(ToolResult.Ok("none", "This file declares nothing the parser recognises."));

                var listing = string.Join("\n",
                    found.Select(d => $"{d.Line,5}-{d.EndLine,-5} {d.Kind,-11} {d.Name,-32} {d.AstPath}"));
                return Task.FromResult(ToolResult.Ok($"{found.Count} declaration(s)", listing));
            }),

        // ── Edit / save (approval-gated) — mutate the live AvalonEdit document ──
        new DelegateClientTool(
            "edit_declaration",
            "Change ONE declaration — replace it, delete it, change its signature without touching its body "
            + "(or the reverse), rename it, substitute text inside it, insert one beside it, append a member "
            + "to a type, or rewrite its doc comment. Prefer this over set_editor_text and replace_all: it "
            + "edits exactly the declaration named and nothing else, and it re-parses the result and refuses "
            + "rather than leaving the file broken. Do not worry about indentation or line endings — write "
            + "the replacement flush-left with \\n and it lands correctly indented with the file's own "
            + "endings. Get ast_path from list_declarations.",
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
                if (IsReadOnlyMode) return Task.FromResult(ToolResult.Error("This file can't be edited."));
                if (EditorGrammar is not { } grammar)
                    return Task.FromResult(ToolResult.Error(
                        "This file has no code grammar, so it has no declarations to edit structurally. Use "
                      + "replace_all or set_editor_text."));

                if (ParseEditOp(ToolArgs.Str(args, "op", "operation")) is not { } op)
                    return Task.FromResult(ToolResult.Error(
                        $"Unknown 'op' '{ToolArgs.Str(args, "op", "operation")}'. Expected replace, delete, "
                      + "signature, body, rename, insert_before, insert_after, append, doc, substitute or import."));

                // 'import' is the one op that belongs to the file rather than to a declaration, so it is the
                // one that needs no ast_path.
                var path = ToolArgs.Str(args, "ast_path", "path", "declaration");
                if (op is not Nexaflow.Syntax.StructuralEdit.Op.Import && string.IsNullOrEmpty(path))
                    return Task.FromResult(ToolResult.Error("No 'ast_path' provided — call list_declarations first."));

                var options = new Nexaflow.Syntax.StructuralEdit.Options(
                    ToolArgs.Bool(args, "with_trivia"),
                    ToolArgs.Str(args, "expect"),
                    ToolArgs.Raw(args, "find"),
                    ToolArgs.Bool(args, "find_is_regex"),
                    ToolArgs.Bool(args, "all_occurrences"));

                // Raw, not Str: replacement code is whitespace-significant, and trimming it would silently
                // reflow whatever the caller wrote.
                var text   = ToolArgs.Raw(args, "text", "content", "new_text");
                var result = op is Nexaflow.Syntax.StructuralEdit.Op.Import
                    ? Nexaflow.Syntax.StructuralEdit.AddImport(grammar, Document.Text, text ?? "")
                    : Nexaflow.Syntax.StructuralEdit.Apply(grammar, Document.Text, path!, op, text,
                                                           options, ToolArgs.Str(args, "to"));

                if (!result.Ok || result.Change is not { } change)
                    return Task.FromResult(ToolResult.Error(result.Message));

                // One splice rather than a whole-document assignment: a single undo step, and the caret and
                // scroll position survive.
                Document.Replace(change.Offset, change.Length, change.Inserted);

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
                Document.Text = ToolArgs.Raw(args, "text", "content", "new_text") ?? string.Empty;
                return Task.FromResult(ToolResult.Ok("document replaced",
                    $"Replaced the document ({Document.LineCount} lines). Unsaved — call save_file to persist."));
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
                if (IsReadOnlyMode) return Task.FromResult(ToolResult.Error("This file can't be edited."));
                var find = ToolArgs.Str(args, "find", "pattern", "search");
                if (string.IsNullOrEmpty(find)) return Task.FromResult(ToolResult.Error("No 'find' provided."));
                var repl = ToolArgs.Str(args, "replace", "replacement", "to") ?? string.Empty;
                var rx   = ToolArgs.Bool(args, "regex");
                var cs   = ToolArgs.Bool(args, "case_sensitive");
                try
                {
                    var re = new Regex(rx ? find : Regex.Escape(find), cs ? RegexOptions.None : RegexOptions.IgnoreCase);
                    var original = Document.Text;
                    var count = re.Matches(original).Count;
                    if (count > 0) Document.Text = re.Replace(original, rx ? repl : repl.Replace("$", "$$"));
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

    /// <summary>The tree-sitter grammar for the open file, or null when no grammar covers it — which is the
    /// line between "this file has declarations to address" and "this file is just text".</summary>
    private string? EditorGrammar
    {
        get
        {
            var grammar = HighlightingRegistry.Resolve(FileName).TreeSitterLanguage;
            return string.IsNullOrEmpty(grammar) ? null : grammar;
        }
    }

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

    /// <summary>A compact, read-only preview for the conversation context panel — file name, a meta line, and a
    /// capped monospace snippet. Built fresh each time; it never re-hosts the live editor.</summary>
    public System.Windows.Controls.UserControl CreateContextPreview()
    {
        var meta = $"{LineCount:N0} line{(LineCount == 1 ? "" : "s")} · {SelectedEncoding.Name}"
                 + (IsDirty ? " · unsaved edits" : string.Empty)
                 + (IsReadOnlyMode ? " · read-only" : string.Empty);
        const int cap = 8000;
        var text = Document.Text;
        var body = text.Length > cap ? text[..cap] + "\n… (preview truncated)" : text;
        return new ReadOnlyTextPreview(string.IsNullOrEmpty(FileName) ? "Editor" : FileName, meta, body);
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
