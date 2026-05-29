using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig.Syntax;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.ObjectModel;
using System.IO;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// Backing view-model for <see cref="Views.MarkdownView"/>.
///
/// Maintains the raw markdown text and a list of <see cref="MarkdownBlockModel"/>
/// objects — one per top-level Markdig block.  The view renders each block
/// independently, toggling between a formatted view and a source TextBox.
/// </summary>
public sealed partial class MarkdownViewModel : ObservableObject, IPageViewModel
{
    // ── File state ────────────────────────────────────────────────────────

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    // ── Raw text ──────────────────────────────────────────────────────────

    private string _rawText;

    // ── Block list ────────────────────────────────────────────────────────

    /// <summary>
    /// Live list of rendered/editable blocks.  Updated by <see cref="Reparse"/>.
    /// Blocks that are currently being edited (<see cref="MarkdownBlockModel.IsEditing"/>)
    /// keep their existing model instance across reparses so the TextBox is not disturbed.
    /// </summary>
    public ObservableCollection<MarkdownBlockModel> Blocks { get; } = [];

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownViewModel(string filePath)
    {
        FilePath = filePath;
        _rawText = File.Exists(filePath) ? File.ReadAllText(filePath).ReplaceLineEndings("\n") : string.Empty;
        Reparse();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        File.WriteAllText(FilePath, _rawText);
        IsDirty = false;
    }

    // ── View → VM callbacks ───────────────────────────────────────────────

    /// <summary>
    /// Called by the view whenever the source TextBox for a block changes.
    /// Updates raw text and marks the file dirty; does NOT re-render immediately
    /// (rendering happens on blur or explicit pin-toggle).
    /// </summary>
    public void OnBlockTextEdited(MarkdownBlockModel block, string newText)
    {
        block.RawMarkdown = newText.ReplaceLineEndings("\n");
        _rawText          = ReconstructRawText();
        IsDirty           = true;
    }

    /// <summary>
    /// Called when a block's TextBox loses focus.
    /// If the block is not pinned, exits source mode and re-parses.
    /// </summary>
    public void OnBlockBlurred(MarkdownBlockModel block)
    {
        if (!block.IsPinned)
            block.IsEditing = false;
        Reparse();
    }

    /// <summary>
    /// Switches a rendered block into editing mode (called on left-click).
    /// </summary>
    public void BeginEdit(MarkdownBlockModel block)
    {
        block.IsEditing = true;
    }

    /// <summary>
    /// Context-menu "Show source" / "Render" toggle.
    /// Pinned blocks stay in source mode even when they lose focus.
    /// </summary>
    public void TogglePin(MarkdownBlockModel block)
    {
        if (block.IsPinned)
        {
            block.IsPinned  = false;
            block.IsEditing = false;
            Reparse();
        }
        else
        {
            block.IsPinned  = true;
            block.IsEditing = true;
        }
    }

    // ── Parse / render ────────────────────────────────────────────────────

    private void Reparse()
    {
        var doc      = Markdig.Markdown.Parse(_rawText, MarkdownPipelineFactory.Default);
        var rawLines = _rawText.Length > 0 ? _rawText.Split('\n') : [];

        // Index existing blocks so we can preserve editing state
        var byStart = Blocks.ToDictionary(b => b.StartLine);

        var newList = new List<MarkdownBlockModel>();

        foreach (var mdBlock in doc)
        {
            int start = mdBlock.Line;
            int end   = BlockEndLine(mdBlock);
            end = Math.Clamp(end, start, Math.Max(rawLines.Length - 1, start));

            int safeStart = Math.Clamp(start, 0, Math.Max(rawLines.Length - 1, 0));
            int safeEnd   = Math.Clamp(end,   0, Math.Max(rawLines.Length - 1, 0));
            var raw = rawLines.Length > 0
                ? string.Join("\n", rawLines[safeStart..(safeEnd + 1)])
                : string.Empty;

            if (byStart.TryGetValue(start, out var existing) && existing.IsEditing)
            {
                // Keep the same instance — its TextBox must not be disturbed
                existing.ParsedBlock = mdBlock;
                newList.Add(existing);
            }
            else
            {
                newList.Add(new MarkdownBlockModel
                {
                    StartLine   = start,
                    EndLine     = end,
                    RawMarkdown = raw,
                    ParsedBlock = mdBlock,
                    IsPinned    = byStart.TryGetValue(start, out var prev) ? prev.IsPinned : false,
                    IsEditing   = byStart.TryGetValue(start, out var prev2) && prev2.IsPinned,
                });
            }
        }

        Blocks.Clear();
        foreach (var b in newList) Blocks.Add(b);
    }

    // ── Raw text reconstruction ───────────────────────────────────────────

    private string ReconstructRawText()
        => string.Join("\n\n", Blocks.Select(b => b.RawMarkdown.TrimEnd('\n', '\r')));

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int BlockEndLine(Block block)
    {
        // FencedCodeBlock (and MathBlock which extends it) stores only the
        // content lines in Lines — the opening and closing fence lines are
        // handled by the parser and are NOT included.  Total span is therefore:
        //   block.Line (opening fence) + Lines.Count (content) + 1 (closing fence)
        if (block is FencedCodeBlock fcb)
            return block.Line + fcb.Lines.Count + 1;

        if (block is LeafBlock lb && lb.Lines.Count > 0)
            return block.Line + lb.Lines.Count - 1;

        if (block is ContainerBlock cb && cb.Count > 0)
            return BlockEndLine(cb[cb.Count - 1]);

        return block.Line;
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext()
    {
        var dirty = IsDirty ? " (unsaved changes)" : string.Empty;
        return $"Markdown file: '{FileName}' at '{FilePath}'{dirty}.";
    }

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
}
