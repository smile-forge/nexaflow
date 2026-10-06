using System;
using System.Collections.Generic;

using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// What a key means in a tune: the keys a musician expects, mapped onto the gestures the tune already answers
/// (<see cref="AbcEdit"/>).
///
/// <para>
/// A letter from A to G in the staff writes that note, taking the octave and the length of the note before it; <c>_</c>
/// and <c>#</c> flatten and sharpen that note; <c>+</c> and <c>-</c> make it longer and shorter; Page Up and Page Down
/// move it an octave; and Space puts a pause of a whole note in.
/// </para>
/// <para>
/// Only in the staff. A title and the words under a staff are text, and the engine writes into those itself — this says
/// nothing about them, so what it says nothing about falls through to that.
/// </para>
/// <para>
/// <strong>Read at the tune's own start.</strong> Each gesture is given a reading of the tune's own source from nought,
/// and what it hands back is where the writing landed in that — so the change is the whole of the tune written again,
/// moved to where the tune stands in the document. Asking a gesture about a reading made at the tune's offset instead
/// would mix the document's offsets with the tune's own.
/// </para>
/// </summary>
internal sealed class AbcEdits : IContentLanguage, IOnEdit
{
    /// <summary>The one of these there is: it holds nothing.</summary>
    public static readonly AbcEdits Instance = new();

    private AbcEdits() { }

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <summary>
    /// A tune is a picture of what its source meant, so a key pressed where nothing is written leaves the source alone.
    /// The staff is this handler's; the title and the words under it are the runs of words the engine writes into.
    /// </summary>
    public bool TakesTextOnlyInWords => true;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => Worded(edit) ? null : edit.Kind switch
    {
        EditKind.Typing when edit.Text.Length == 1 => Typed(edit, edit.Text[0]),
        EditKind.Settling when edit.Text == " " => Paused(edit),
        EditKind.Raising => Moved(edit, by: 1),
        EditKind.Lowering => Moved(edit, by: -1),
        _ => null,
    };

    /// <summary>
    /// Whether the caret is in text rather than in the staff — a field's value, a title among them, or a line of lyrics.
    ///
    /// <para>
    /// Every key below means something about a note, and none of them means it there: a G typed in a title is the letter G,
    /// and a plus typed under the staff is a plus. Saying nothing is what hands those to the engine, which writes into a run
    /// of words as a run of words.
    /// </para>
    /// </summary>
    private static bool Worded(ContentEdit edit) =>
        edit.Part is { } part
        && (part.Kind is AbcKinds.Field or AbcKinds.LyricLine
            || part.Ancestors().Any(up => up.Kind is AbcKinds.Field or AbcKinds.LyricLine));

    /// <summary>What a character typed in the staff comes to, or null where it is not a key a tune answers.</summary>
    private static ContentChange? Typed(ContentEdit edit, char key) => key switch
    {
        '_' => Altered(edit, by: -1),
        '#' => Altered(edit, by: 1),
        '+' => Stretched(edit, by: 1),
        '-' => Stretched(edit, by: -1),
        _ when AbcParser.IsNoteLetter(key) => Noted(edit, key),
        _ => null,
    };

    /// <summary>The note a letter spells here, written at the caret: the letter, in the octave and length of the note before it.</summary>
    private static ContentChange? Noted(ContentEdit edit, char letter)
    {
        var (reading, at) = Tune(edit);

        return Written(edit, AbcEdit.NoteAt(reading, at, letter));
    }

    /// <summary>A pause of a whole note, written at the caret.</summary>
    private static ContentChange? Paused(ContentEdit edit)
    {
        var (reading, _) = Tune(edit);

        return Written(edit, AbcEdit.Pause(reading));
    }

    private static ContentChange? Altered(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Accidental, by);

    private static ContentChange? Stretched(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Length, by);

    private static ContentChange? Moved(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Octave, by);

    /// <summary>
    /// What <paramref name="gesture"/> makes of the note the caret stands after — the whole tune written again, since a
    /// gesture hands back the tree it made rather than the characters it changed.
    /// </summary>
    private static ContentChange? Gesture(ContentEdit edit,
                                          Func<ContentReading, IReadOnlyList<ContentPart>, int, AstWrite?> gesture,
                                          int by)
    {
        var (reading, at) = Tune(edit);
        if (AbcEdit.Before(reading, at) is not { } note) return null;
        if (gesture(reading, [note], by) is not { } made) return null;

        return ContentChange.Write(edit.Start, edit.Source.Length, made.Tree.Print(), edit.Start + made.End);
    }

    /// <summary>The tune read from its own source, and where the caret stands in it.</summary>
    private static (ContentReading Reading, int At) Tune(ContentEdit edit) =>
        (ContentReading.Of(AbcParser.Parse(edit.Source), source: edit.Source), edit.State.Caret - edit.Start);

    /// <summary><paramref name="said"/> written at the caret, as the reader means it — the parser makes it safe.</summary>
    private static ContentChange? Written(ContentEdit edit, string said)
    {
        if (said.Length == 0 || edit.Part is not { } part) return null;

        var caret = edit.State.Caret;

        return new ContentChange([ContentWrite.Words(part, caret, 0, said)], caret + said.Length, edit.State.Raw);
    }
}
