namespace Nexaflow.Features.Markdown.ViewModels;

/// <summary>
/// What the Markdown tab shows, as the toolbar's three-stop slider reads left to right.
/// The numbers are the slider's stops, so <c>MarkdownViewModel.ViewModeIndex</c> is a cast.
/// </summary>
public enum MarkdownViewMode
{
    /// <summary>The document rendered, with inline editing. The default.</summary>
    Rendered = 0,

    /// <summary>Both surfaces at once: the raw source on the left, the rendered document on the right.</summary>
    Split = 1,

    /// <summary>The raw markdown alone, in one plain text box.</summary>
    Source = 2,
}
