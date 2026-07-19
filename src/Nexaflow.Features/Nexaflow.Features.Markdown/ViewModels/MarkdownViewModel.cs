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

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// Backing view-model for <see cref="Views.MarkdownView"/>.
///
/// Holds the whole document as a single markdown string, two-way bound to the
/// view's editing surface(s). The default surface is the shared
/// <c>InlineMarkdownEditor</c> (rendered with inline editing); a toolbar toggle
/// swaps to <see cref="SourceOnly"/> mode (the raw markdown in one text box).
/// Both surfaces bind the same <see cref="Markdown"/>, so edits carry across.
/// </summary>
public sealed partial class MarkdownViewModel : ObservableObject, IPageViewModel, IContextPreview
{
    // ── File state ────────────────────────────────────────────────────────

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Heading title-path to scroll to once rendered (set when opened from a snaplink), or null.</summary>
    public IReadOnlyList<string>? InitialHeading { get; }

    private readonly IShellServices _shell;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    // ── Document ──────────────────────────────────────────────────────────

    /// <summary>The whole document. Two-way bound to the editing surface(s).</summary>
    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>True = show the raw markdown source; false = rendered + inline editing (default).</summary>
    [ObservableProperty]
    private bool _sourceOnly;

    /// <summary>Last value written to / read from disk; the dirty baseline.</summary>
    private string _savedText;

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownViewModel(string filePath, IShellServices shell, IReadOnlyList<string>? initialHeading = null)
    {
        FilePath       = filePath;
        _shell         = shell;
        InitialHeading = initialHeading;
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

    partial void OnMarkdownChanged(string value) => IsDirty = value != _savedText;

    // ── AI surface helpers (used by the client tools) ─────────────────────
    // Mutating tools run off the UI thread; the Markdown/SourceOnly properties are two-way bound to WPF
    // editors, so every write is marshalled through IShellServices.RunOnUiAsync.

    /// <summary>Replaces the whole document (marks dirty via <see cref="OnMarkdownChanged"/>).</summary>
    internal Task SetDocumentAsync(string text) => _shell.RunOnUiAsync(() => { Markdown = text; });

    /// <summary>Switches between the raw-source and rendered views (parity with the toolbar toggle).</summary>
    internal Task SetSourceModeAsync(bool sourceOnly) => _shell.RunOnUiAsync(() => { SourceOnly = sourceOnly; });

    /// <summary>Saves through the same command the toolbar uses; false when there was nothing to save.</summary>
    internal async Task<bool> SaveFromToolAsync()
    {
        if (!IsDirty) return false;
        await _shell.RunOnUiAsync(() => { if (SaveCommand.CanExecute(null)) SaveCommand.Execute(null); });
        return true;
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var src   = Markdown ?? string.Empty;
        var lines = src.Length == 0 ? 0 : src.Count(c => c == '\n') + 1;
        var mode  = SourceOnly ? "raw source" : "rendered (inline-edit)";
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
}
