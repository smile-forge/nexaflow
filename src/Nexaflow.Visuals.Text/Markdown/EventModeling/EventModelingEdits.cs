using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.EventModeling;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.EventModeling;

/// <summary>
/// What an edit means in an event model, and what a frame offers where it is right-clicked.
///
/// <para>
/// Enter on a frame starts the next one under it: a time frame numbered past every number written, of the type that usually comes
/// next — a ui's command, a command's event, an event's read model, a read model's ui, a processor's command — indented as the frame
/// is, with its name still to write and the caret there. Backspace in a frame's name with nothing written in it takes the frame away,
/// and the caret back to where the row before it ends. Everything else written in an event model is a run of words — a name, data, a
/// note — which the engine writes into, and the parser makes safe.
/// </para>
/// <para>
/// A right-click on a frame offers its entity type, and whether it is a time frame or a reset frame — the one that starts a flow
/// afresh, fed by nothing above it. What is chosen is written over the word that says it, spelled as that word was: short where it was
/// short, long where it was long.
/// </para>
/// </summary>
internal sealed class EventModelingEdits : IContentLanguage, IOnEdit
{
    /// <summary>The one of these there is: it holds nothing.</summary>
    public static readonly EventModelingEdits Instance = new();

    /// <summary>The highest number a frame may be written with: three digits.</summary>
    private const int LastNumber = 999;

    /// <summary>
    /// Every entity type, in the order the ribbon offers them: its short word and its long one (a ui has only the one), the type
    /// that usually comes next in a flow, and what it is called.
    /// </summary>
    private static readonly (string Short, string Long, string Follows, string Tip)[] Types =
    [
        ("ui", "ui", "cmd", "Text.EventModeling.Type.Ui"),
        ("pcr", "processor", "cmd", "Text.EventModeling.Type.Processor"),
        ("cmd", "command", "evt", "Text.EventModeling.Type.Command"),
        ("rmo", "readmodel", "ui", "Text.EventModeling.Type.ReadModel"),
        ("evt", "event", "rmo", "Text.EventModeling.Type.Event"),
    ];

    /// <summary>The two kinds of frame: its short word, its long one, and what it is called.</summary>
    private static readonly (string Short, string Long, string Tip)[] Frames =
    [
        (EventModelingParser.TimeFrameWord, EventModelingParser.TimeFrameLongWord, "Text.EventModeling.Frame.Time"),
        (EventModelingParser.ResetFrameWord, EventModelingParser.ResetFrameLongWord, "Text.EventModeling.Frame.Reset"),
    ];

    private EventModelingEdits() { }

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <summary>
    /// An event model is a picture of what its source meant, so a key pressed where nothing is written leaves the source alone. The
    /// names, data, notes and specifications are the runs of words the engine writes into.
    /// </summary>
    public bool TakesTextOnlyInWords => true;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Settling when edit.Text == "\n" => Entered(edit),
        EditKind.Erasing => Erased(edit),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || FrameOf(ask.Part) is not { } frame) return [];

        var controls = new List<MiniRibbonControl>();

        if (frame.Part(EventModelingRoles.Type) is { } type)
            controls.Add(new MiniRibbonChoice("eventmodeling.type", Str.Get("Text.EventModeling.Type"),
                                              [.. Types.Select(option => new MiniRibbonOption(option.Short, Str.Get(option.Tip)) { Letters = option.Short })],
                                              static (edit, option) => Respelled(edit, EventModelingRoles.Type, option.Id))
            {
                Current = TypeOf(type.Text)?.Short,
            });

        if (frame.Part(Roles.Name) is { } word)
            controls.Add(new MiniRibbonChoice("eventmodeling.frame", Str.Get("Text.EventModeling.Frame"),
                                              [.. Frames.Select(option => new MiniRibbonOption(option.Short, Str.Get(option.Tip)) { Letters = option.Short })],
                                              static (edit, option) => Respelled(edit, Roles.Name, option.Id))
            {
                Current = FrameKindOf(word.Text)?.Short,
            });

        return controls;
    }

    // ── Keys ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enter on a frame: the next frame under it, its name still to write and the caret there. In data written as a block, a line
    /// broken like any other — and anywhere that is not a frame, nothing of the event model's own.
    /// </summary>
    private static ContentChange? Entered(ContentEdit edit)
    {
        if (edit.State.HasSelection || FrameOf(edit.Part) is not { } frame || InBlock(edit.Part)) return null;
        if (Unused(edit.Root) is not { } number) return ContentChange.Stay(edit.State);

        var document = edit.State.Document;
        var numbered = frame.Part(EventModelingRoles.Frame)?.Length ?? 2;
        var frameWord = SpelledAs(frame.Part(Roles.Name)?.Text, (EventModelingParser.TimeFrameWord, EventModelingParser.TimeFrameLongWord));

        var written = frame.Part(EventModelingRoles.Type)?.Text;
        var follows = TypeOf(TypeOf(written)?.Follows) ?? Types[0];
        var typeWord = SpelledAs(written, (follows.Short, follows.Long));

        var at = RowEnd(frame);
        var line = document.LineEnding + Indent(document, frame.Start) + frameWord + " "
                   + number.ToString(CultureInfo.InvariantCulture).PadLeft(numbered, '0') + " " + typeWord + " ";

        return ContentChange.Write(at, 0, line);
    }

    /// <summary>
    /// Backspace in a frame's name with nothing written in it: the frame taken away — its row, where it is all its row holds, the caret
    /// at the end of the row before; only the frame, where the row holds something else too.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        if (edit.State.HasSelection || edit.Part is not { } part) return null;

        var name = part.Kind == EventModelingKinds.Name ? part : part.Ancestors().FirstOrDefault(up => up.Kind == EventModelingKinds.Name);
        if (name is not { Text.Length: 0, Parent: { Kind: EventModelingKinds.Frame } frame } || edit.State.Caret != name.Start) return null;

        var document = edit.State.Document;

        var lead = frame.Start;
        while (lead > 0 && document[lead - 1] is ' ' or '\t') lead--;

        var tail = frame.End;
        while (tail < document.Length && document[tail] is ' ' or '\t') tail++;

        var rowBefore = lead == 0 || document[lead - 1] is '\n' or '\r';
        var rowAfter = tail == document.Length || document[tail] is '\r' or '\n';

        (int From, int To) taken = (rowBefore, rowAfter) switch
        {
            (true, true) when lead > 0 => (lead - EndingBefore(document, lead), tail),
            (true, true) => (lead, tail + EndingAt(document, tail)),
            (false, true) => (lead, tail),
            _ => (frame.Start, tail),
        };

        return ContentChange.Write(taken.From, taken.To - taken.From, string.Empty, taken.From);
    }

    /// <summary>
    /// What a choice on the ribbon writes: the word in the frame <paramref name="role"/> names, given the one the option stands for,
    /// spelled long where that word was long.
    /// </summary>
    private static ContentChange? Respelled(ContentEdit edit, string role, string chosen)
    {
        if (FrameOf(edit.Part) is not { } frame || frame.Part(role) is not { } word) return null;

        var said = role == EventModelingRoles.Type
            ? TypeOf(chosen) is { } type ? SpelledAs(word.Text, (type.Short, type.Long)) : null
            : FrameKindOf(chosen) is { } kind ? SpelledAs(word.Text, (kind.Short, kind.Long)) : null;

        if (said is null || said == word.Text) return ContentChange.Stay(edit.State);

        var caret = edit.State.Caret;
        if (caret >= word.End) caret += said.Length - word.Length;
        else if (caret > word.Start) caret = word.Start + said.Length;

        return new ContentChange([new ContentWrite(word, said)], caret, null);
    }

    /// <summary>
    /// <paramref name="now"/> spelled as <paramref name="was"/> is: long where <paramref name="was"/> is a long word — a word with a
    /// short one beside it — and short everywhere else.
    /// </summary>
    private static string SpelledAs(string? was, (string Short, string Long) now)
    {
        var written = TypeOf(was) is { } type ? (type.Short, type.Long) : FrameKindOf(was) is { } kind ? (kind.Short, kind.Long) : default;
        return written.Long is not null && was == written.Long && written.Long != written.Short ? now.Long : now.Short;
    }

    // ── What the source says ────────────────────────────────────────────────

    /// <summary>The frame <paramref name="part"/> is, or is in — or null where it is in none.</summary>
    private static ContentPart? FrameOf(ContentPart? part) =>
        part is null ? null : part.Kind == EventModelingKinds.Frame ? part : part.Ancestors().FirstOrDefault(up => up.Kind == EventModelingKinds.Frame);

    /// <summary>Whether <paramref name="part"/> is in data written as a block, where a line ending is part of what is said.</summary>
    private static bool InBlock(ContentPart? part) =>
        part is not null && part.Ancestors().Prepend(part).Any(up => up.Kind == EventModelingKinds.Value && up.Text.Contains('\n'));

    /// <summary>
    /// The number the next frame is written with: one past the highest written, or where that is past three digits, the lowest none
    /// is written with — null where every one is.
    /// </summary>
    private static int? Unused(ContentPart root)
    {
        var used = root.Children
            .Where(child => child.Kind == EventModelingKinds.Frame)
            .Select(frame => frame.Part(EventModelingRoles.Frame)?.Text)
            .Select(text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1)
            .Where(number => number >= 0)
            .ToHashSet();

        var next = used.Count == 0 ? 1 : used.Max() + 1;
        if (next <= LastNumber) return next;

        for (var number = 1; number <= LastNumber; number++)
            if (!used.Contains(number)) return number;

        return null;
    }

    /// <summary>
    /// Where the row <paramref name="frame"/> ends — before the line ending past the trivia written after it, or past a comment that
    /// runs over rows; or right after it, where something else follows it on its row.
    /// </summary>
    private static int RowEnd(ContentPart frame)
    {
        var at = frame.End;
        if (frame.Parent is not { } block) return at;

        foreach (var next in block.Children.SkipWhile(child => child != frame).Skip(1))
        {
            if (next.Role != Roles.Trivia) break;

            var text = next.Text;
            var ending = text.IndexOf('\n');

            if (ending >= 0 && next.Kind == Kinds.Space)
                return next.Start + (ending > 0 && text[ending - 1] == '\r' ? ending - 1 : ending);

            at = next.End;
            if (ending >= 0) break;
        }

        return at;
    }

    /// <summary>How long the line ending just before <paramref name="at"/> is — none, where no row ends there.</summary>
    private static int EndingBefore(ContentDocument document, int at) =>
        at == 0 ? 0
        : document[at - 1] == '\n' ? (at > 1 && document[at - 2] == '\r' ? 2 : 1)
        : document[at - 1] == '\r' ? 1
        : 0;

    /// <summary>How long the line ending starting at <paramref name="at"/> is — none, where no row ends there.</summary>
    private static int EndingAt(ContentDocument document, int at) =>
        at >= document.Length ? 0
        : document[at] == '\r' ? (at + 1 < document.Length && document[at + 1] == '\n' ? 2 : 1)
        : document[at] == '\n' ? 1
        : 0;

    /// <summary>The space opening the row <paramref name="at"/> stands in.</summary>
    private static string Indent(ContentDocument document, int at)
    {
        var (row, _) = document.LineAt(at);
        var opening = document.Slice(row, at - row);
        return opening[..(opening.Length - opening.TrimStart(' ', '\t').Length)];
    }

    /// <summary>The entity type a word spells, either way it may be spelled — or null where it spells none.</summary>
    private static (string Short, string Long, string Follows, string Tip)? TypeOf(string? word) =>
        Types.FirstOrDefault(type => type.Short == word || type.Long == word) is { Short: not null } found ? found : null;

    /// <summary>The kind of frame a word spells, either way it may be spelled — or null where it spells none.</summary>
    private static (string Short, string Long, string Tip)? FrameKindOf(string? word) =>
        Frames.FirstOrDefault(frame => frame.Short == word || frame.Long == word) is { Short: not null } found ? found : null;
}
