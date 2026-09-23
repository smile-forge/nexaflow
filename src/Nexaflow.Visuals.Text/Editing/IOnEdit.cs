using System;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where an edit landed, told to the language whose source it landed in: the whole of what is being edited, and
/// the stretch of it that language was written as.
///
/// <para>
/// <strong>Offsets are the document's.</strong> A language laid inside another is laid at the offset its source
/// starts at, so every part it drew names the characters a reader is typing between; the caret, a hole and a run of
/// words all agree without anything being moved. <see cref="Local"/> and <see cref="Back"/> are for what reads the
/// language's own source on its own — a grammar handed a line, a block read from the top.
/// </para>
/// </summary>
/// <param name="Landing">The document as it stands, what was drawn of it, and where the caret is.</param>
/// <param name="Start">Where the language's own source begins in the document.</param>
/// <param name="Length">How long it is.</param>
public readonly record struct ContentEdit(Landing Landing, int Start, int Length)
{
    /// <summary>Everything being edited, not only this language's part of it — an edit hands one of these back.</summary>
    public EditState State => Landing.State;

    /// <summary>Where the language's own source ends in the document.</summary>
    public int End => Start + Length;

    /// <summary>The language's own source, exactly as it was typed.</summary>
    public string Source => State.Source.Substring(Start, Length);

    /// <summary>The same edit, counted from the start of the language's own source.</summary>
    public EditState Local
    {
        get
        {
            var start = Start;
            var length = Length;

            return new EditState(
                Source,
                Math.Clamp(State.Caret - start, 0, length),
                [.. State.Selection
                    .Select(range => (From: Math.Clamp(range.Start - start, 0, length), To: Math.Clamp(range.End - start, 0, length)))
                    .Where(range => range.To > range.From)
                    .Select(range => new EditRange(range.From, range.To - range.From))],
                State.Raw is { } raw && raw.Start >= start && raw.End <= start + length
                    ? new RawZone(raw.Start - start, raw.End - start)
                    : null);
        }
    }

    /// <summary>What a state worked out over <see cref="Local"/> is, put back into the document it was taken from.</summary>
    public EditState Back(EditState local)
    {
        var start = Start;

        return new(State.Source[..start] + local.Source + State.Source[End..],
                   local.Caret + start,
                   [.. local.Selection.Select(range => new EditRange(range.Start + start, range.Length))],
                   local.Raw is { } raw ? new RawZone(raw.Start + start, raw.End + start) : null);
    }

    /// <summary>
    /// The same stretch after <paramref name="after"/> was made of the document — grown or shrunk by what the edit
    /// wrote, which is always inside it, because an edit is only ever told to the language it landed in.
    /// </summary>
    public ContentEdit After(EditState after) =>
        this with
        {
            Landing = Landing with { State = after },
            Length = Math.Max(Length + after.Source.Length - State.Source.Length, 0),
        };
}

/// <summary>
/// What a language says an edit means in its own source, where that is something other than the characters of it.
///
/// <para>
/// <strong>The exception, never the rule.</strong> Editing is shared: from the caret up the layout to the part it
/// stands in, and the edit is done to that part's source — typed characters are inserted, backspace takes one back.
/// A language that needs something else of a key registers this for itself, and is asked only for keys that land in
/// its own source: which language that is was settled by a stage and is on the syntax tree, so nothing looks it up.
/// </para>
/// <para>
/// Null everywhere is the ordinary answer, and hands the edit back to be done as any edit is. After whatever
/// happened, <see cref="Edited"/> is told what it came to, which is where one edit's consequences elsewhere in the
/// same source go — a name renamed where it is declared, renamed where it is used.
/// </para>
/// </summary>
public interface IOnEdit
{
    /// <summary>What writing <paramref name="text"/> at the caret means here, or null for the characters themselves.</summary>
    EditState? Typing(ContentEdit edit, string text) => null;

    /// <summary>What Space and Enter mean here — ending whatever is half-written — or null for the separator itself.</summary>
    EditState? Settling(ContentEdit edit, string separator) => null;

    /// <summary>What taking back a character means here — before the caret, or after it for delete — or null for a character.</summary>
    EditState? Erasing(ContentEdit edit, bool forward) => null;

    /// <summary>What an edit made here comes to, however it was made.</summary>
    /// <param name="before">Where it landed, before it was made.</param>
    /// <param name="after">What it made of the whole document.</param>
    EditState Edited(ContentEdit before, EditState after) => after;
}
