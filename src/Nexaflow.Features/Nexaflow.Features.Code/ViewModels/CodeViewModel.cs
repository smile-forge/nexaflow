using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Editor;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Features.Code.ViewModels;

/// <summary>
/// "As Code" view-model: the shared <see cref="FileTextEditorViewModel"/> (full-file editing, save, highlighting)
/// plus a markdown <b>code-intelligence panel</b> built from the file's structure — dependency links and a
/// clickable class diagram whose members jump to their definition line.
///
/// Structure is extracted on the live editor buffer (so it tracks edits) by <see cref="CodeStructureExtractor"/>.
/// Member links carry an AST path; <see cref="NavigateToAst"/> resolves it against the current text and asks the
/// view to scroll there via <see cref="NavigateToLineRequested"/> (the only editor-control coupling).
/// </summary>
public sealed partial class CodeViewModel : FileTextEditorViewModel
{
    private readonly CodeStructureExtractor _extractor = new();
    private string? _pendingAst;

    public CodeViewModel(string filePath, IShellServices shell, long maxEditableBytes, string? initialAst = null)
        : base(filePath, shell, maxEditableBytes)
    {
        _pendingAst = initialAst;
        GrammarId = HighlightingRegistry.Resolve(FileName).TreeSitterLanguage;
    }

    /// <summary>The tree-sitter grammar id for this file, or null when the file has no code grammar (the panel
    /// then stays hidden).</summary>
    public string? GrammarId { get; }

    [ObservableProperty] private string _intelligenceMarkdown = string.Empty;

    [NotifyPropertyChangedFor(nameof(PanelVisible))]
    [ObservableProperty] private bool _isPanelOpen = true;

    [NotifyPropertyChangedFor(nameof(PanelVisible))]
    [ObservableProperty] private bool _hasIntelligence;

    /// <summary>True when there is something to show AND the user hasn't collapsed the panel.</summary>
    public bool PanelVisible => HasIntelligence && IsPanelOpen;

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    /// <summary>Re-extracts the structure from the current buffer and rebuilds the panel markdown. Cheap (one
    /// tree-sitter parse); called debounced on edit. Only reassigns the markdown when it actually changed, so a
    /// keystroke pause inside a method body doesn't rebuild the flow document.</summary>
    public void RegenerateIntelligence()
    {
        if (GrammarId is not { } grammar)
        {
            HasIntelligence = false;
            return;
        }

        var dir = Path.GetDirectoryName(FilePath);
        var outline = _extractor.Extract(grammar, Document.Text, dir);
        if (!outline.HasContent)
        {
            HasIntelligence = false;
            IntelligenceMarkdown = string.Empty;
            return;
        }

        var md = CodeIntelligenceMarkdown.Build(FilePath, outline);
        if (md != IntelligenceMarkdown) IntelligenceMarkdown = md;
        HasIntelligence = true;

        // First generation after opening via a cross-file member link: jump to the seeded member.
        if (_pendingAst is { } ast)
        {
            _pendingAst = null;
            if (_extractor.ResolveLine(grammar, Document.Text, ast, dir) is int line) RequestScrollToLine(line);
        }
    }

    /// <summary>Resolves an AST path against the current buffer and scrolls there (for same-file member links).</summary>
    public void NavigateToAst(string astPath)
    {
        if (GrammarId is not { } grammar) return;
        var dir = Path.GetDirectoryName(FilePath);
        if (_extractor.ResolveLine(grammar, Document.Text, astPath, dir) is int line) RequestScrollToLine(line);
    }

    /// <summary>Opens another file As Code in a new tab (for dependency / cross-file links).</summary>
    public void OpenCode(string path, string? astPath)
    {
        var p = new Dictionary<string, string> { ["path"] = path };
        if (!string.IsNullOrEmpty(astPath)) p["ast"] = astPath;
        Shell.OpenTab("Code", p);
    }
}
