using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.IO;

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
public sealed partial class MarkdownViewModel : ObservableObject, IPageViewModel
{
    // ── File state ────────────────────────────────────────────────────────

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Heading title-path to scroll to once rendered (set when opened from a snaplink), or null.</summary>
    public IReadOnlyList<string>? InitialHeading { get; }

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

    public MarkdownViewModel(string filePath, IReadOnlyList<string>? initialHeading = null)
    {
        FilePath       = filePath;
        InitialHeading = initialHeading;
        _savedText = File.Exists(filePath)
            ? File.ReadAllText(filePath).ReplaceLineEndings("\n")
            : string.Empty;
        Markdown = _savedText;   // OnMarkdownChanged sees value == _savedText → stays clean
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        File.WriteAllText(FilePath, Markdown);
        _savedText = Markdown;
        IsDirty    = false;
    }

    // ── Dirty tracking ────────────────────────────────────────────────────

    partial void OnMarkdownChanged(string value) => IsDirty = value != _savedText;

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
