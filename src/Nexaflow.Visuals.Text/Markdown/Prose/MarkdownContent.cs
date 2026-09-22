using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A markdown document as a kind of content: how its source is laid out, and what writing into the picture means.
///
/// <para>
/// A document is written in as it stands. Typing into a heading types into the heading, and the hashes that are not on
/// the screen are not in the way of anything, because every run of words names the characters it was set from and the
/// caret goes straight into them.
/// </para>
/// <para>
/// <strong>Backspace at the end of a line shows the line as it was written.</strong> There is machinery on that line
/// that is not on the screen — the hashes of a heading, the marks round a word set heavy, an item's marker — and a key
/// that takes back a character has nothing to take where the character it would take is not drawn. So it shows the
/// characters instead, which is the one thing a reader wanting to change the markup is reaching for. Moving away puts
/// it back. It is the same answer a formula gives at the edge of a construct, for the same reason.
/// </para>
/// </summary>
/// <param name="lay">Lays the state out at a width, told whether anybody can write in it — the builder, as the element asks for it.</param>
public sealed class MarkdownContent(Func<EditState, double, bool, Laid> lay) : IContent
{
    /// <summary>
    /// The ordinary case: a document drawn in one style, by the one builder that draws markdown.
    ///
    /// <para>
    /// The pipeline is assembled here rather than by the builder, because which language reads a fenced block is
    /// looked up in a table the host assembled — and a builder's business is turning a tree into a layout, not
    /// asking a host anything.
    /// </para>
    /// </summary>
    public static MarkdownContent Of(StyleFormat style, DiagramRenderOptions? options = null)
    {
        var read = MarkdownParser.Reader
            .Then(new Stages.WithNested(style, options))
            .Then(new Stages.WithImages(options?.Pictures))
            .Then(new Stages.WithLinks(options?.Links));

        return new((state, room, readOnly) =>
            MarkdownBuilder.Lay(state.Source, style, room, state.Raw, readOnly, reader: read));
    }

    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, bool readOnly) => lay(state, room, readOnly);

    /// <inheritdoc/>
    /// <remarks>
    /// A line shown as its characters stays shown as its characters while it is written in — typed at its end it grows
    /// to hold what was typed, or the markup would turn back into what it reads as under the caret.
    /// </remarks>
    public EditState? Typing(Landing landing, string text)
    {
        var state = landing.State;
        if (state.HasSelection || text.Length == 0) return null;

        return state.Raw is { } shown && shown.Holds(state.Caret)
            ? state.Write(text, shown with { End = shown.End + text.Length })
            : null;
    }

    /// <inheritdoc/>
    public EditState? Erasing(Landing landing, bool forward)
    {
        var state = landing.State;
        if (state.HasSelection || forward) return null;

        // Already shown as its characters: it is text to its ends, and no further.
        if (state.Raw is { } raw)
            return raw.Holds(state.Caret) && state.Caret == raw.Start ? state : null;

        return Written(landing) is { } line ? state with { Raw = line } : null;
    }

    /// <summary>
    /// What ticking an item off writes: the character between the brackets, swapped for the other one.
    ///
    /// <para>
    /// Written into the source rather than held beside it, which is what makes this the whole of it. The tick is
    /// recorded where a reader would look for it, so it survives being saved and read back; the document goes round
    /// the same loop any other edit does, so the box is drawn from what the tree now says; and taking it back is the
    /// undo the reader already has rather than a second one for ticks.
    /// </para>
    /// </summary>
    /// <param name="tick">The three characters a box was drawn over — what the layout says the press landed on.</param>
    public static EditState? Ticked(EditState state, ISourcePart? tick)
    {
        if (tick is not { Length: >= 3 } box) return null;

        var source = state.Source;
        var opens = box.Start;
        if (opens < 0 || opens + 2 >= source.Length || source[opens] != '[' || source[opens + 2] != ']') return null;

        var done = source[opens + 1] is not (' ' or '\t');

        return state with
        {
            Source = source[..(opens + 1)] + (done ? " " : "x") + source[(opens + 2)..],
            Selected = null,
        };
    }

    /// <summary>
    /// The line the caret stands at the end of, where that line is drawn as something other than the characters it was
    /// written with — or null where it is drawn as itself, and taking back a character means taking back a character.
    /// </summary>
    private static RawZone? Written(Landing landing)
    {
        var source = landing.State.Source;
        var caret = Math.Clamp(landing.State.Caret, 0, source.Length);

        var start = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;
        var ending = source.IndexOf('\n', caret);
        var end = ending < 0 ? source.Length
                : ending > start && source[ending - 1] == '\r' ? ending - 1
                : ending;

        if (caret != end || start >= end) return null;

        return Shows(landing, start, end) ? null : new RawZone(start, end);
    }

    /// <summary>
    /// Whether every character of a line is on the screen as itself. Asked of what was drawn rather than of the tree,
    /// because the question is what a reader can see: a run says whether what it shows is what it was set from, and a
    /// line whose runs do not account for all of it has something on it that is not being shown.
    /// </summary>
    private static bool Shows(Landing landing, int start, int end)
    {
        var shown = 0;

        foreach (var piece in landing.Laid.Root.SelfAndDescendants())
            if (piece.Words is { Maps: true }
                && piece.Part is { } part
                && part.Start >= start
                && part.Start + part.Length <= end)
                shown += part.Length;

        return shown >= end - start;
    }
}
