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
/// <strong>Told against what was drawn.</strong> Each gesture is given the tune as the engine read and laid it — the
/// tree its stages worked over, standing where it stands in the document — so a note knows both what it sounds and
/// which characters it was written with. What comes back is the stretches to write, which the engine writes before
/// reading the tune again: the one path an edit takes.
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
    private static ContentChange? Noted(ContentEdit edit, char letter) =>
        Written(edit, AbcEdit.NoteAt(edit.Root, edit.State.Caret, letter));

    /// <summary>A pause of a whole note, written at the caret.</summary>
    private static ContentChange? Paused(ContentEdit edit) => Written(edit, AbcEdit.Pause(edit.Root));

    private static ContentChange? Altered(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Accidental, by);

    private static ContentChange? Stretched(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Length, by);

    private static ContentChange? Moved(ContentEdit edit, int by) => Gesture(edit, AbcEdit.Octave, by);

    /// <summary>
    /// What <paramref name="gesture"/> makes of the note the caret stands after: the stretch of source that note was
    /// written in, given the note spelled again, and nothing else touched.
    /// </summary>
    private static ContentChange? Gesture(ContentEdit edit,
                                          Func<IReadOnlyList<ContentPart>, int, ContentChange?> gesture,
                                          int by) =>
        AbcEdit.Before(edit.Root, edit.State.Caret) is { } note ? gesture([note], by) : null;

    /// <summary><paramref name="said"/> written at the caret, as the reader means it — the parser makes it safe.</summary>
    private static ContentChange? Written(ContentEdit edit, string said)
    {
        if (said.Length == 0 || edit.Part is not { } part) return null;

        var caret = edit.State.Caret;

        return new ContentChange([ContentWrite.Words(part, caret, 0, said)], caret + said.Length, edit.State.Raw);
    }
}
