using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// What an edit means in a formula: LaTeX's rule about how a command is spelled, and what ends one.
///
/// <para>
/// A backslash opens a stretch shown as itself, and letters extend it: that is TeX's own rule for a control word, and it is why
/// <c>\alpha</c> shows as itself while it is written instead of flickering through four failed parses. Anything that is not a
/// letter ends the word — so <c>\alpha+</c> settles the command first and then types the plus, exactly as TeX would read it.
/// Everything else is typed as any character is.
/// </para>
/// <para>
/// Space and Enter settle a command, keeping the space that says where it stopped: without it <c>\alpha</c> followed by a letter
/// is the unknown command <c>\alphax</c>. That space is the character a person editing the source by hand would have typed there,
/// and it is written by the edit that ended the command — never by anything reading the formula, which would write it into
/// source nobody edited and could no longer tell where the caret was. A formula is one expression, so Enter adds no line: the
/// line it was pressed on is the only one there is, and elsewhere Space and Enter write nothing.
/// </para>
/// </summary>
internal sealed class LatexEdits : IOnEdit, IOnMove
{
    public static LatexEdits Instance { get; } = new();

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing when edit.Text.Length == 1 => Typing(edit.State, edit.Text[0]),
        EditKind.Settling => Settling(edit.State, edit.Text == "\n" ? " " : edit.Text),
        _ => null,
    };

    /// <summary>
    /// What carrying something about in a formula means: cells of a table moved, where that is what is being carried
    /// (<see cref="TexMove"/>), and nothing of its own otherwise — a term carried about is characters moving, which is
    /// what the engine does anyway.
    /// </summary>
    public ContentChange? Move(ContentMove move) =>
        move.Holds ? TexMove.Dropped(move.Root, move.Carried, move.To) : null;

    /// <summary>What typing a character does where LaTeX has something to say about it; null everywhere else.</summary>
    private static ContentChange? Typing(EditState state, char character)
    {
        if (state.Raw is { } zone && zone.Holds(state.Caret))
            return char.IsLetter(character)
                ? ContentChange.Typed(state, character.ToString(), zone with { End = zone.End + 1 })
                : ContentChange.Typed(state, character.ToString()) with { Raw = null };

        if (character != '\\') return null;

        var at = state.HasSelection ? state.SelectionStart : state.Caret;
        return ContentChange.Typed(state, "\\", new RawZone(at, at + 1));
    }

    /// <summary>A command being spelled ended, and the separator kept where it says where the command stopped.</summary>
    private static ContentChange Settling(EditState state, string separator)
    {
        if (state.Raw is null) return ContentChange.Stay(state);

        var raw = state.RawText;
        return raw.Length > 1 && raw[0] == '\\' && char.IsLetter(raw[^1])
            ? ContentChange.Typed(state, separator) with { Raw = null }
            : ContentChange.Showing(state, null);
    }
}
