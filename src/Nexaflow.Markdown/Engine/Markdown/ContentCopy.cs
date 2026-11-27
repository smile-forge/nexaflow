using Nexaflow.Markdown.Export;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What copying a stretch of content amounts to: the source as written, the stretch as it is shown, and — for a block that draws
/// a picture worth keeping — that picture.
///
/// <para>
/// <strong>Saying it is the engine's; spelling it and putting it anywhere is not.</strong> Which characters were chosen and what
/// they mean is what the engine knows, so it says what a copy holds. How a clipboard spells words, marked-up text and a picture is
/// the clipboard's, and the clipboard is the application's — shared with every other thing in the window, and subject to whatever
/// the host has to say about what may leave it — so a copy is handed over rather than set.
/// </para>
/// </summary>
/// <param name="Markdown">The source, as written.</param>
/// <param name="Shown">
/// The stretch as it is laid out, for whatever puts it somewhere to spell as words with the marks taken off and as marked-up text.
/// Null where what was copied is words with nothing marked up in them — pieces chosen whole, a block of columns, code — and then
/// the words are <paramref name="Markdown"/> itself.
/// </param>
/// <param name="Picture">What the block copied draws, in the medium it was laid out in.</param>
public sealed record ContentCopy(string Markdown, ExportDocument? Shown = null, LayoutPicture? Picture = null)
{
    /// <summary>Whether it is a block of columns, a line of it a line — said so, for a paste to put it back as one.</summary>
    public bool Column { get; init; }
}
