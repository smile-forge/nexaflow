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

    public static readonly MarkdownRenderContext Dark = MarkdownPalette.Dark;

    public static implicit operator MarkdownRenderContext(MarkdownPalette palette)
        => new() { Palette = palette };
}
