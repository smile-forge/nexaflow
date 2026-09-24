using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// What an edit means in a formula: LaTeX's rule about how a command is spelled, and what ends one.
///
/// <para>
/// A backslash opens a command and letters extend it, so <c>\alpha</c> shows as itself while it is being written
/// rather than flickering through four failed parses (<see cref="LatexWriting"/>). Space and Enter settle it, and what
/// follows is a space either way: a formula is one expression, so the line the reader pressed Enter on is the only line
/// there is. Everything else is typed as any character is.
/// </para>
/// <para>
/// Told in the document's offsets, which is all either rule needs: both are about the caret and the stretch being
/// spelled, and never read the formula from the top.
/// </para>
/// </summary>
internal sealed class LatexEdits : IOnEdit
{
    public static LatexEdits Instance { get; } = new();

    /// <inheritdoc/>
    public EditState? Typing(ContentEdit edit, string text) =>
        text.Length == 1 ? edit.State.Typing(text[0]) : null;

    /// <inheritdoc/>
    public EditState? Settling(ContentEdit edit, string separator) =>
        edit.State.Settle(separator == "\n" ? " " : separator);
}
