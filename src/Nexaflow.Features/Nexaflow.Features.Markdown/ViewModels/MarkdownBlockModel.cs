using CommunityToolkit.Mvvm.ComponentModel;
using Markdig.Syntax;

namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// Represents one top-level Markdig block in the live markdown editor.
/// Each block is independently rendered or shown as editable source.
/// </summary>
public sealed partial class MarkdownBlockModel : ObservableObject
{
    /// <summary>Zero-based line in the source where this block starts.</summary>
    public int StartLine { get; init; }

    /// <summary>Zero-based line in the source where this block ends (inclusive).</summary>
    public int EndLine   { get; init; }

    /// <summary>Raw markdown text for this block (the user edits this).</summary>
    public string RawMarkdown { get; set; } = string.Empty;

    /// <summary>The Markdig AST node — updated on each re-parse.</summary>
    public Block ParsedBlock { get; set; } = null!;

    /// <summary>
    /// True while the block is showing a source TextBox — either because the
    /// user clicked it (auto-edit) or because it is <see cref="IsPinned"/>.
    /// </summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>
    /// When true the block stays in source mode even after losing focus.
    /// Set via the "Show source" context menu; cleared by "Render".
    /// </summary>
    [ObservableProperty] private bool _isPinned;
}
