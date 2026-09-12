using System.Windows;
using Nexaflow.Visuals.Text.Markdown.Music;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles the fenced code blocks that hold music — <c>abc</c>, <c>lilypond</c> and <c>ly</c> — engraved.
///
/// <para>
/// Registered here beside the barcode and QR handlers for the same reason: it is not a diagram, but it arrives
/// as one — a fenced block whose info string names a language, rendered to an element in place of its source —
/// and registering it here is what puts it on both markdown surfaces at once. A <c>#% … #%</c> block is the same
/// music spelled another way, and is rendered by this too.
/// </para>
/// <para>
/// <strong>The whole fence body is the source.</strong> Unlike a barcode, whose value is one field of a settings
/// block, every character between the fences is the music — so an edit splices the whole of it and the offset
/// an element reports is the fence body's own, biased by <see cref="DiagramRenderOptions.SourceOffset"/> onto
/// the markdown block it came from.
/// </para>
/// <para>
/// One handler per notation, because a handler is asked to render a body and not told the language that named
/// it; which notation it is, is the one thing each instance knows.
/// </para>
/// </summary>
public sealed class MusicDiagramHandler(MusicDialect dialect) : IDiagramHandler
{
    public bool CanHandle(string language) => MusicDialectExtensions.FromTag(language) == dialect;

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        new MusicScore(dialect, source, options.Palette, options.SourceOffset);
}
