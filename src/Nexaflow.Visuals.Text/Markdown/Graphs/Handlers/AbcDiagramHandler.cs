using System.Windows;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles <c>abc</c> fenced code blocks — ABC notation, engraved.
///
/// <para>
/// Registered here beside the barcode and QR handlers for the same reason: it is not a diagram, but it
/// arrives as one — a fenced block whose info string names a language, rendered to an element in place of
/// its source — and registering it here is what puts it on both markdown surfaces at once.
/// </para>
/// <para>
/// The older <c>#% … #%</c> block is untouched and still goes through its own parser and engraver. Both
/// syntaxes work; this one is the path everything else is moving onto, and the two meet when LilyPond
/// follows.
/// </para>
/// <para>
/// <strong>The whole fence body is the source.</strong> Unlike a barcode, whose value is one field of a
/// settings block, every character between the fences is the tune — so an edit splices the whole of it and
/// the offset an element reports is the fence body's own, biased by
/// <see cref="DiagramRenderOptions.SourceOffset"/> onto the markdown block it came from.
/// </para>
/// </summary>
public sealed class AbcDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("abc", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        new AbcElement(source, options.Palette) { SourceStart = options.SourceOffset };
}
