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

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// Backing view-model for <see cref="Views.MarkdownView"/>.
///
/// Holds the whole document as a single markdown string, two-way bound to the
/// view's editing surface(s). The default surface is the shared
/// <c>InlineMarkdownEditor</c> (rendered with inline editing); the toolbar's
/// three-stop slider swaps to the raw markdown in one text box, or shows the two
/// side by side - see <see cref="MarkdownViewMode"/>.
/// Both surfaces bind the same <see cref="Markdown"/>, so edits carry across.
/// </summary>
public sealed partial class MarkdownViewModel : ObservableObject, IPageViewModel, IContextPreview
{
    // ── File state ────────────────────────────────────────────────────────

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Heading title-path to scroll to once rendered (set when opened from a snaplink), or null.</summary>
    public IReadOnlyList<string>? InitialHeading { get; }

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
    public string? SingleBlock { get; }

    private readonly IShellServices _shell;

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

    public MarkdownViewModel(string filePath, IShellServices shell, IReadOnlyList<string>? initialHeading = null)
    {
        FilePath       = filePath;
        _shell         = shell;
        InitialHeading = initialHeading;
        SingleBlock    = SingleBlockFiles.LanguageOf(filePath);
        _savedText = VirtualFileSystem.Instance.Exists(filePath)
            ? VirtualFileSystem.Instance.ReadAllText(filePath).ReplaceLineEndings("\n")
            : string.Empty;
        Markdown = _savedText;   // OnMarkdownChanged sees value == _savedText → stays clean
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        VirtualFileSystem.Instance.WriteAllText(FilePath, Markdown);
        _savedText = Markdown;
        IsDirty    = false;
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
        var path = await _shell.PickSaveFileAsync(name, [".png"], Path.GetDirectoryName(FilePath));
        if (string.IsNullOrEmpty(path)) return false;

        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";

        try
        {
            await File.WriteAllBytesAsync(path, png);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError($"Could not save the picture to {path}: {e.Message}");
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
        var dir = Path.GetDirectoryName(FilePath);
        return new SelectableMarkdownView
        {
            Markdown                    = Markdown,
            BaseDirectory               = string.IsNullOrEmpty(dir) ? null : dir,
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
