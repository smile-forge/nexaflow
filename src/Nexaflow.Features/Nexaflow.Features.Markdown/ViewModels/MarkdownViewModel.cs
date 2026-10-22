using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Markdown.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Nexaflow.Visuals.Common.Theming;
using System;
using Nexaflow.Markdown.Packaging;
using System.Globalization;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// Backing view-model for <see cref="Views.MarkdownView"/>.
///
/// Holds the whole document as a single markdown string, two-way bound to the
/// view's editing surface(s). The default surface is the shared
/// <c>MarkdownSurface</c> (rendered with inline editing); the toolbar's
/// three-stop slider swaps to the raw markdown in one text box, or shows the two
/// side by side - see <see cref="MarkdownViewMode"/>.
/// Both surfaces bind the same <see cref="Markdown"/>, so edits carry across.
/// </summary>
public sealed partial class MarkdownViewModel : ObservableObject, IPageViewModel, IContextPreview
{
    // ── File state ────────────────────────────────────────────────────────

    public string FilePath { get; private set; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Where the files the document shows or links to are read from: the file's own folder — or, for a Markdown Package, the
    /// folder it is unpacked in while it is open, which a save packs up again.
    /// </summary>
    [ObservableProperty]
    private string? _documentFolder;

    /// <summary>What Save As offers a document to be written as: a package first, its attachments in it, or a file beside
    /// its attachments. A file of one language — a tune, a formula — stays that.</summary>
    private static SaveFileType[] DocumentTypes =>
    [
        new(Str.Get("Markdown.SaveAs.Package"), MarkdownPackage.Extension),
        new("Markdown", ".md"),
    ];

    /// <summary>
    /// Where a draft is offered to be saved — a document made for the reader and kept in a temp folder, such as a page read
    /// into Markdown — or null for a file opened where it lives. A draft's Save asks where, starting here, and once saved it
    /// is that file.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _draftFolder;

    /// <summary>Raised, with the new file, once the document is saved as another file and is that file from then on.</summary>
    public event Action<string>? Moved;

    /// <summary>Heading title-path to scroll to once rendered (set when opened from a snaplink), or null.</summary>
    public IReadOnlyList<string>? InitialHeading { get; }

    /// <summary>
    /// A saved reference to land on once rendered (<c>heading:getting-started</c>) — set when a link leads into the document at a
    /// place, as a summary's heading leads back to its section — or null.
    /// </summary>
    public string? InitialPlace { get; }

    /// <summary>
    /// The fenced language this whole file is, or null when it is an ordinary markdown document.
    ///
    /// <para>
    /// Handed straight to the editor, which then owns the fence: <see cref="Markdown"/> holds the tune, or
    /// the formula, exactly as the file does, and the <c>```abc</c> exists only for as long as it takes to
    /// render. So <see cref="Save"/> needs to know nothing about any of this — what it writes is what was
    /// read, and a file that was never markdown never gains a wrapper by having been opened here.
    /// </para>
    /// </summary>
    public string? WrittenIn { get; }

    private readonly IShellServices _shell;

    /// <summary>The workspace's AI, which works through the whole document; null where the page was made without one.</summary>
    private readonly IAIService? _ai;

    /// <summary>The gate on the workspace's Documents model, which both whole-document buttons stand behind; null without an AI service.</summary>
    private readonly IAiAbilityGate? _documentModel;

    /// <summary>
    /// Whether Summarise works now, and why not where it does not — the document not one it works on, a summary of it under way
    /// already, no Documents model — for the button to bind; null without an AI service.
    /// </summary>
    public DocumentWorkGate? SummaryWork { get; }

    /// <summary>Whether Translate works now, and why not where it does not, as <see cref="SummaryWork"/> says it for Summarise.</summary>
    public DocumentWorkGate? TranslationWork { get; }

    /// <summary>The whole-document work under way on this document, from here or by the assistant; null without an AI service.</summary>
    private IDocumentWorkWatch? _work;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    // ── Document ──────────────────────────────────────────────────────────

    /// <summary>The whole document. Two-way bound to the editing surface(s).</summary>
    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>Which of the three surfaces the tab is showing: the rendered document (the default), both side
    /// by side, or the raw source.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeIndex))]
    [NotifyPropertyChangedFor(nameof(ShowRendered))]
    [NotifyPropertyChangedFor(nameof(ShowSource))]
    [NotifyPropertyChangedFor(nameof(IsSplit))]
    private MarkdownViewMode _viewMode;

    /// <summary>The rendered surface is on screen - alone, or as the right half of the split.</summary>
    public bool ShowRendered => ViewMode is MarkdownViewMode.Rendered or MarkdownViewMode.Split;

    /// <summary>The raw-source surface is on screen - alone, or as the left half of the split.</summary>
    public bool ShowSource => ViewMode is MarkdownViewMode.Source or MarkdownViewMode.Split;

    /// <summary>Both surfaces are on screen, with a drag handle between them.</summary>
    public bool IsSplit => ViewMode is MarkdownViewMode.Split;

    /// <summary>
    /// The mode as the toolbar slider's position, 0 to 2. The slider snaps to whole ticks, so a value between
    /// two stops only ever arrives mid-drag; it is read as the stop it is nearest.
    /// </summary>
    public double ViewModeIndex
    {
        get => (double)(int)ViewMode;
        set => ViewMode = (MarkdownViewMode)Math.Clamp((int)Math.Round(value), 0, 2);
    }

    // ── Footer stats ──────────────────────────────────────────────────────────

    /// <summary>Words in the document, as the footer reports them. Kept in step with
    /// <see cref="Markdown"/> rather than computed on demand, so the footer is a binding like everything else.</summary>
    [ObservableProperty] private int _wordCount;

    /// <summary>Lines in the document. The text is normalised to \n on load, so this counts those.</summary>
    [ObservableProperty] private int _lineCount;

    /// <summary>This tab's zoom over the shell's text size. The rendered surface takes it as its body size
    /// (the rest of the document is proportional to that); the source box takes it directly.</summary>
    public TextZoom Zoom { get; } = new();

    /// <summary>Last value written to / read from disk; the dirty baseline.</summary>
    private string _savedText;

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownViewModel(string filePath, IShellServices shell, IReadOnlyList<string>? initialHeading = null, string? draftFolder = null,
                             IAIService? ai = null, string? initialPlace = null)
    {
        FilePath       = filePath;
        _shell         = shell;
        _ai            = ai;
        _documentModel = ai?.Gate(AiAbility.Documents);
        if (_documentModel is { } documents)
        {
            SummaryWork     = new DocumentWorkGate(documents);
            TranslationWork = new DocumentWorkGate(documents);
        }
        InitialHeading = initialHeading;
        InitialPlace   = initialPlace;
        _draftFolder   = draftFolder;
        WrittenIn      = SingleBlockFiles.LanguageOf(filePath);
        if (MarkdownPackage.Names(filePath) && VirtualFileSystem.Instance.Exists(filePath))
        {
            _documentFolder = Path.Combine(Path.GetTempPath(), "nexaflow-mdz", Guid.NewGuid().ToString("N")[..12]);
            _savedText      = MarkdownPackage.Unpack(filePath, _documentFolder).ReplaceLineEndings("\n");
        }
        else
        {
            _documentFolder = Path.GetDirectoryName(filePath) is { Length: > 0 } folder ? folder : null;
            _savedText = VirtualFileSystem.Instance.Exists(filePath)
                ? VirtualFileSystem.Instance.ReadAllText(filePath).ReplaceLineEndings("\n")
                : string.Empty;
        }
        Markdown = _savedText;   // OnMarkdownChanged sees value == _savedText → stays clean
        WatchWork();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Writes the document to its file — packed, for a Markdown Package — or, for a draft, to the file the reader
    /// picks (<see cref="SaveAs"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (DraftFolder is not null)
        {
            await SaveAs();
            return;
        }
        if (MarkdownPackage.Names(FilePath)) MarkdownPackage.Pack(Markdown, DocumentFolder!, FilePath);
        else VirtualFileSystem.Instance.WriteAllText(FilePath, Markdown);
        _savedText = Markdown;
        IsDirty    = false;
    }

    /// <summary>Something to save: edits, or a draft not yet saved anywhere.</summary>
    private bool CanSave => IsDirty || DraftFolder is not null;

    /// <summary>
    /// Writes the document to a file the reader picks, offered beside it — or, for a draft, where the draft says — and makes
    /// it that file. As a Markdown Package, the default, the files it shows or links to go inside it; as a Markdown file they
    /// go to the same places beside the new file, so it still shows them.
    /// </summary>
    [RelayCommand]
    private async Task SaveAs()
    {
        string? folder = DraftFolder ?? Path.GetDirectoryName(FilePath);
        string extension = Path.GetExtension(FilePath);
        IReadOnlyList<SaveFileType> types = WrittenIn is null ? DocumentTypes : [new(extension.TrimStart('.').ToUpperInvariant(), extension)];
        if (await _shell.PickSaveFileAsync(Path.GetFileNameWithoutExtension(FilePath), types,
                                           string.IsNullOrEmpty(folder) ? null : folder) is not { } target) return;
        try
        {
            if (MarkdownPackage.Names(target)) MarkdownPackage.Pack(Markdown, DocumentFolder ?? Path.GetDirectoryName(Path.GetFullPath(target))!, target);
            else
            {
                CarryAttachments(Path.GetDirectoryName(Path.GetFullPath(target))!);
                VirtualFileSystem.Instance.WriteAllText(target, Markdown);
                DocumentFolder = Path.GetDirectoryName(Path.GetFullPath(target));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _shell.ShowError(Str.Format("Markdown.Error.SaveFailedFormat", ex.Message));
            return;
        }
        FilePath = target;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        DraftFolder = null;
        WatchWork();
        _savedText  = Markdown;
        IsDirty     = false;
        Moved?.Invoke(target);
    }

    /// <summary>The files the document shows or links to from its folder, copied to the same places under <paramref name="to"/>.</summary>
    private void CarryAttachments(string to)
    {
        if (DocumentFolder is not { } from || string.Equals(Path.GetFullPath(from), to, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var attachment in MarkdownAttachments.Paths(Markdown))
        {
            string source = Path.Combine(from, attachment);
            if (!File.Exists(source)) continue;
            string copy = Path.Combine(to, attachment);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(source, copy, overwrite: true);
        }
    }

    /// <summary>Lets go of what follows the workspace's models, and of the folder a Markdown Package was unpacked in, once the document is closed.</summary>
    public void Release()
    {
        if (_work is { } work)
        {
            work.PropertyChanged -= OnWorkChanged;
            work.Dispose();
        }
        SummaryWork?.Dispose();
        TranslationWork?.Dispose();
        _documentModel?.Dispose();

        if (DocumentFolder is { } folder && folder.StartsWith(Path.Combine(Path.GetTempPath(), "nexaflow-mdz"), StringComparison.OrdinalIgnoreCase))
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ── Links ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Follows a link out of the document: the shell routes it to whichever feature claims it — a web page to the
    /// browser, a path to the viewer for that kind of file — and nothing claiming it leaves it to open the way any
    /// other link does. A link into the document never reaches here; the surface answers those itself, being the
    /// only thing that knows where they go.
    ///
    /// <para>
    /// A link into another markdown document at a place — a summary's heading leading back to its section, written as the
    /// file with a saved reference after its <c>#</c> — opens that document, or brings forward the tab showing it, at the
    /// place: the reference is markdown's own, so it is followed here rather than handed to a shell that cannot read it.
    /// </para>
    /// </summary>
    public bool FollowLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile && uri.Fragment.Length > 1 && MarkdownDocuments.Names(uri.LocalPath))
        {
            _shell.OpenTab(MarkdownTabRegistration.StaticPageKind, new Dictionary<string, string>
            {
                ["path"] = uri.LocalPath,
                [MarkdownTabRegistration.PlaceParameter] = Uri.UnescapeDataString(uri.Fragment[1..]),
            });
            return true;
        }

        return _shell.HandleObject(url);
    }

    // ── The whole document, with the workspace's AI ───────────────────────

    /// <summary>
    /// Whether the document can be worked through as a whole: a Markdown document — a file, or a package as a .docx is one — saved
    /// where it is, not a draft, nor a file of one fenced language drawn whole.
    /// </summary>
    public bool WorksOnDocument => DocumentRefusal is null;

    /// <summary>Why the document cannot be worked through as a whole, as the buttons' tooltips say it; null where it can.</summary>
    private string? DocumentRefusal =>
        WrittenIn is not null || !MarkdownDocuments.Names(FilePath) ? Str.Format("Markdown.DocumentWork.NotADocumentFormat", FileName)
      : DraftFolder is not null                                     ? Str.Get("Markdown.DocumentWork.SaveFirst")
      : null;

    /// <summary>Whether the document is being summarised now, from here or by the assistant.</summary>
    public bool IsSummarizing => Underway(DocumentWorkKind.Summary).Count > 0;

    /// <summary>Whether the document is being translated now, into any language, from here or by the assistant.</summary>
    public bool IsTranslating => Underway(DocumentWorkKind.Translation).Count > 0;

    /// <summary>What Translate's tooltip says: what it does, or each translation of the document under way and how far it has got.</summary>
    public string TranslateTip => Underway(DocumentWorkKind.Translation) is { Count: > 0 } translating
        ? string.Join("\n\n", translating.Select(Saying))
        : Str.Get("Markdown.Toolbar.Translate.Tip");

    /// <summary>The work of <paramref name="kind"/> under way on the document now.</summary>
    private IReadOnlyList<DocumentWorkUnderway> Underway(DocumentWorkKind kind) =>
        _work is null ? [] : [.. _work.Underway.Where(work => work.Kind == kind)];

    /// <summary>Work under way, as a tooltip says it: what is being done, then how far it has got.</summary>
    private static string Saying(DocumentWorkUnderway work)
    {
        var doing = work.Kind == DocumentWorkKind.Translation
            ? Str.Format("Markdown.DocumentWork.TranslatingFormat", work.Language ?? string.Empty)
            : Str.Get("Markdown.DocumentWork.Summarizing");
        return work.Progress is { } progress ? $"{doing}\n{progress}" : doing;
    }

    /// <summary>Follows the work under way on the document where it is now — again after it is saved somewhere else.</summary>
    private void WatchWork()
    {
        if (_work is { } watched)
        {
            watched.PropertyChanged -= OnWorkChanged;
            watched.Dispose();
        }
        _work = _ai?.WatchDocumentWork(FilePath);
        if (_work is not null) _work.PropertyChanged += OnWorkChanged;
        RefreshDocumentWork();
    }

    private void OnWorkChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshDocumentWork();

    partial void OnDraftFolderChanged(string? value) => RefreshDocumentWork();

    /// <summary>
    /// Says again whether each whole-document button works, and why not: the document's reason first, then — for Summarise — a
    /// summary of it under way already. Translate stays open while it translates, into any other language.
    /// </summary>
    private void RefreshDocumentWork()
    {
        var refusal = DocumentRefusal;
        if (SummaryWork is { } summary)
            summary.Refusal = refusal ?? (Underway(DocumentWorkKind.Summary) is [var summarizing, ..] ? Saying(summarizing) : null);
        if (TranslationWork is { } translation) translation.Refusal = refusal;

        OnPropertyChanged(nameof(IsSummarizing));
        OnPropertyChanged(nameof(IsTranslating));
        OnPropertyChanged(nameof(TranslateTip));
        SummarizeCommand.NotifyCanExecuteChanged();
        OpenTranslateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Whether the language to translate into is being chosen.</summary>
    [ObservableProperty]
    private bool _isTranslateOpen;

    /// <summary>The language to translate into, named in English; the reader's own to start with.</summary>
    [ObservableProperty]
    private string _translateInto = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).EnglishName;

    /// <summary>Languages offered to translate into, named in English as the model is asked them; any other may be typed.</summary>
    public IReadOnlyList<string> Languages { get; } =
    [
        "Arabic", "Chinese (Simplified)", "Chinese (Traditional)", "Dutch", "English", "French", "German", "Greek", "Hebrew",
        "Hindi", "Italian", "Japanese", "Korean", "Polish", "Portuguese", "Brazilian Portuguese", "Russian", "Spanish",
        "Swedish", "Turkish", "Ukrainian", "Vietnamese",
    ];

    /// <summary>
    /// Summarises the whole document — as it stands, edits not yet saved included — with the workspace's Documents model: a
    /// background job, listed with the others and stoppable there; the summary opens beside it when made.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSummarize))]
    private Task Summarize() => StartDocumentWorkAsync(SummaryWork, ai => ai.SummarizeDocument(new DocumentAsked(FilePath, Markdown, DocumentFolder)));

    /// <summary>Whether Summarise sets anything going: the document is one it works on, and no summary of it is under way.</summary>
    private bool CanSummarize => WorksOnDocument && !IsSummarizing;

    /// <summary>Asks which language to translate the document into.</summary>
    [RelayCommand(CanExecute = nameof(WorksOnDocument))]
    private void OpenTranslate() => IsTranslateOpen = true;

    /// <summary>Leaves the document untranslated.</summary>
    [RelayCommand]
    private void CancelTranslate() => IsTranslateOpen = false;

    /// <summary>
    /// Translates the whole document into the language chosen with the workspace's Documents model, a background job as
    /// <see cref="Summarize"/> is; the translation opens beside it when made.
    /// </summary>
    [RelayCommand]
    private async Task Translate()
    {
        if (TranslateInto.Trim() is not { Length: > 0 } language) return;

        IsTranslateOpen = false;
        await StartDocumentWorkAsync(TranslationWork, ai => ai.TranslateDocument(new DocumentAsked(FilePath, Markdown, DocumentFolder), language));
    }

    /// <summary>
    /// Sets the work going once <paramref name="gate"/> opens on it: where the Documents model waits on a download, its first use
    /// offers it and the work waits for it to land (<see cref="IAiAbilityGate.MakeReadyAsync"/>).
    /// </summary>
    private async Task StartDocumentWorkAsync(DocumentWorkGate? gate, Action<IAIService> start)
    {
        if (_ai is not { } ai || gate is null || !await gate.MakeReadyAsync()) return;

        try { start(ai); }
        catch (InvalidOperationException ex) { _shell.ShowError(ex.Message); }
    }

    // ── Dirty tracking ────────────────────────────────────────────────────

    partial void OnMarkdownChanged(string value)
    {
        IsDirty = value != _savedText;
        UpdateCounts(value);
    }

    // ── AI surface helpers (used by the client tools) ─────────────────────
    // Mutating tools run off the UI thread; the Markdown/ViewMode properties are two-way bound to WPF
    // editors, so every write is marshalled through IShellServices.RunOnUiAsync.

    /// <summary>Replaces the whole document (marks dirty via <see cref="OnMarkdownChanged"/>).</summary>
    internal Task SetDocumentAsync(string text) => _shell.RunOnUiAsync(() => { Markdown = text; });

    /// <summary>Switches which surface(s) are showing (parity with the toolbar slider).</summary>
    internal Task SetViewModeAsync(MarkdownViewMode mode) => _shell.RunOnUiAsync(() => { ViewMode = mode; });

    /// <summary>Saves through the same command the toolbar uses; false when there was nothing to save.</summary>
    internal async Task<bool> SaveFromToolAsync()
    {
        if (!IsDirty) return false;
        await _shell.RunOnUiAsync(() => { if (SaveCommand.CanExecute(null)) SaveCommand.Execute(null); });
        return true;
    }

    /// <summary>
    /// Saves the picture of a rendered block where the reader picks, as a PNG: named after the document and the language the
    /// block is written in, and offered beside the document to start with. Whether it was saved.
    /// </summary>
    /// <param name="png">The picture, encoded.</param>
    /// <param name="language">The language of the block's fence, or null where it is in none.</param>
    public async Task<bool> SavePictureAsync(byte[] png, string? language)
    {
        var name = $"{Path.GetFileNameWithoutExtension(FilePath)}-{language ?? "block"}.png";
        var path = await _shell.PickSaveFileAsync(name, [".png"], Path.GetDirectoryName(FilePath), png.Length);
        if (string.IsNullOrEmpty(path)) return false;

        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";

        try
        {
            await File.WriteAllBytesAsync(path, png);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError(Str.Format("Markdown.Error.PictureSaveFailedFormat", path, e.Message));
            return false;
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var src   = Markdown ?? string.Empty;
        var lines = src.Length == 0 ? 0 : src.Count(c => c == '\n') + 1;
        var mode  = ViewMode switch
        {
            MarkdownViewMode.Source => "raw source",
            MarkdownViewMode.Split  => "split (raw source beside rendered inline-edit)",
            _                       => "rendered (inline-edit)",
        };
        var dirty = IsDirty ? " (unsaved changes)" : string.Empty;

        var sb = new StringBuilder();
        sb.Append($"Markdown file '{FileName}' at '{FilePath}'{dirty}. ");
        sb.Append($"Showing the {mode} view — {lines} line(s), {src.Length} char(s).");

        var outline = Outline(src);
        sb.Append(outline.Length > 0 ? $"\nOutline:\n{outline}" : " No headings.");
        sb.Append("\n(Use read_document for the full source.)");
        return sb.ToString();
    }

    public string? GetSecurityContext() => FilePath;

    /// <summary>The document this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(FilePath);

    public IReadOnlyList<IClientTool> GetClientTools() => MarkdownTools.For(this);

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

    // ── IContextPreview ───────────────────────────────────────────────────

    /// <summary>A rendered, read-only snapshot of the current document for the conversation context panel.</summary>
    public UserControl CreateContextPreview()
    {
        return new MarkdownSurface
        {
            Markdown                    = Markdown,
            BaseDirectory               = DocumentFolder,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    // ── Heading outline (ATX headings, code-fence aware) ──────────────────

    private static string Outline(string md)
    {
        var sb = new StringBuilder();
        bool fenced = false;
        int shown = 0;
        foreach (var raw in md.Split('\n'))
        {
            var t = raw.TrimEnd('\r').TrimStart();
            if (t.StartsWith("```") || t.StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced) continue;

            int h = 0;
            while (h < t.Length && t[h] == '#') h++;
            if (h is >= 1 and <= 6 && h < t.Length && t[h] == ' ')
            {
                if (shown++ == 30) { sb.Append("  …\n"); break; }
                sb.Append(' ', (h - 1) * 2).Append(t[(h + 1)..].Trim()).Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Recomputes the footer's counts. Runs on every keystroke, like the dirty check beside it — both are a
    /// single pass over the document, which is the same order as the string comparison already there, so the
    /// footer costs nothing the tab was not already paying.
    /// </summary>
    private void UpdateCounts(string text)
    {
        var words = 0;
        var newlines = 0;
        var inWord = false;

        // Counted here rather than with Split: a split allocates an array per keystroke, and "words" is the
        // number of whitespace-to-text transitions, which one pass answers directly.
        foreach (var c in text)
        {
            if (c == '\n') newlines++;

            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; words++; }
        }

        WordCount = words;
        // A trailing newline ends the last line rather than starting an empty one — the same count
        // File.ReadAllLines gives, and what every editor puts in its footer.
        LineCount = text.Length == 0 ? 0 : newlines + (text[^1] == '\n' ? 0 : 1);
    }
}
