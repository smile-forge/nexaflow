namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Per-render options for <see cref="BlockRenderer"/>: the colour
/// <see cref="Palette"/> plus an optional link-navigation hook.
///
/// A <see cref="MarkdownPalette"/> converts implicitly to a context (with no
/// navigation hook), so existing palette-only callers are unaffected.
/// </summary>
public sealed class MarkdownRenderContext
{
    public required MarkdownPalette Palette { get; init; }

    /// <summary>
    /// Invoked when a link is clicked. Return <c>true</c> to indicate the link was
    /// handled (e.g. opened in an in-app tab); the renderer then skips its default
    /// behaviour of launching the OS browser. When null, links open externally.
    /// </summary>
    public Func<string, bool>? OnNavigate { get; init; }

    /// <summary>
    /// Optional base directory used to resolve relative <c>![](file.png)</c> image paths to a
    /// local file (e.g. a post-it's attachment folder). Absolute paths and <c>file:</c> URIs
    /// resolve without it; remote <c>http(s)</c> images are never loaded and render as text.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// When true, a diagram wider/taller than the available width is scaled down (uniformly) to fit
    /// rather than getting its own scrollbars. Set by the inline editor: scrollbars inside an editable
    /// surface fight text selection (you can't grab the thumb), so the diagram fits the column instead.
    /// </summary>
    public bool FitContentToWidth { get; init; }

    /// <summary>
    /// When true (with <see cref="FitContentToWidth"/>), a too-wide diagram keeps its natural size and gets a
    /// horizontal scrollbar instead of being scaled down — readable at full size. Only sensible on a read-only
    /// selectable surface (the "As Code" panel) where the scrollbar thumb can actually be grabbed; an editable
    /// surface leaves this off and scales instead. Vertical overflow still flows to the host (full height).
    /// </summary>
    public bool ScrollWideDiagrams { get; init; }

    public static readonly MarkdownRenderContext Dark = MarkdownPalette.Dark;

    public static implicit operator MarkdownRenderContext(MarkdownPalette palette)
        => new() { Palette = palette };
}
