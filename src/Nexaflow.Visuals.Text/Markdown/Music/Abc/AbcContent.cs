using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// A tune, as the element that hosts it sees one: something that engraves itself, and a handful of keys
/// that mean something to music and to nothing else on the page.
///
/// <para>
/// Everything a score used to own beyond this — the wash, the wave, the blinking caret, the drag grown
/// out to whole constructs, the arrow keys — is <see cref="ContentElement"/>'s, and is the same code a
/// formula and a barcode run. What is left here is an octave, an accidental and a length.
/// </para>
/// </summary>
internal sealed class AbcContent : IContent
{
    private readonly Brush _ink;
    private readonly ScoreSpacing? _spacing;

    private AbcLayout? _layout;

    public AbcContent(string abc, Brush ink, int sourceStart = 0, ScoreSpacing? spacing = null)
    {
        Source = abc;
        _ink = ink;
        SourceStart = sourceStart;
        _spacing = spacing;
    }

    public string Source { get; set; }

    public int SourceStart { get; }

    /// <summary>
    /// The engraved tune — the reading behind it as well as the pieces, which is what the gestures need
    /// and what a test asks about. Null until it has been laid out once.
    /// </summary>
    public AbcLayout? Layout => _layout;

    public Laid Lay(double room, double pixelsPerDip)
    {
        _layout = AbcLayout.Build(Source, room, _ink, pixelsPerDip, spacing: _spacing);
        return _layout.Laid;
    }

    /// <summary>
    /// A note letter is a whole note, in the octave the one before it was in. Everything else types
    /// itself, because a tune is text and half of what a reader types is a field or a bar line.
    /// </summary>
    public string? Typing(char character, int caret) =>
        _layout is not null && AbcParser.IsNoteLetter(character)
            ? AbcEdit.NoteAt(_layout.Reading, caret, character)
            : null;

    /// <summary>An accidental or a length key, which is a gesture on the note already there.</summary>
    public Edited? Type(char character, int caret, IReadOnlyList<(int Start, int Length)> selection)
    {
        if (_layout is null) return null;

        return character switch
        {
            '#' => Gesture(notes => AbcEdit.Accidental(_layout.Reading, notes, 1), caret, selection),
            '+' => Gesture(notes => AbcEdit.Length(_layout.Reading, notes, 1), caret, selection),

            // Only where there is a note to act on. Otherwise they are characters a tune is written with:
            // `_` flattens a note and also spells one in a field, and `-` ties.
            '_' when Target(caret, selection).Count > 0 =>
                Gesture(notes => AbcEdit.Accidental(_layout.Reading, notes, -1), caret, selection),

            '-' when Target(caret, selection).Count > 0 =>
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, -1), caret, selection),

            _ => null,
        };
    }

    /// <summary>
    /// The keys a score claims for itself. Page Up and Page Down are the octave, because nothing else on
    /// a page wants them and an octave is the gesture a reader reaches for most.
    /// </summary>
    public Edited? Press(Key key, ModifierKeys modifiers, int caret,
                         IReadOnlyList<(int Start, int Length)> selection)
    {
        if (_layout is null || modifiers.HasFlag(ModifierKeys.Control)) return null;

        return key switch
        {
            Key.PageUp => Gesture(notes => AbcEdit.Octave(_layout.Reading, notes, 1), caret, selection),
            Key.PageDown => Gesture(notes => AbcEdit.Octave(_layout.Reading, notes, -1), caret, selection),

            // The shift-less spellings of + and -, which arrive as keys rather than as text on a numeric
            // keypad and on the top row of most layouts.
            Key.Add or Key.OemPlus when modifiers.HasFlag(ModifierKeys.Shift) =>
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, 1), caret, selection),

            Key.Subtract => Gesture(notes => AbcEdit.Length(_layout.Reading, notes, -1), caret, selection),

            Key.OemMinus when Target(caret, selection).Count > 0 =>
                Gesture(notes => AbcEdit.Length(_layout.Reading, notes, -1), caret, selection),

            _ => null,
        };
    }

    /// <summary>
    /// The actions a score offers on what is selected, or on the note that was clicked. The same set the
    /// keys reach, because a reader who cannot remember which key sharpens a note should not have to.
    /// </summary>
    public FrameworkElement? Ribbon(int caret, IReadOnlyList<(int Start, int Length)> selection,
                                    Action<Edited> apply)
    {
        if (_layout is null) return null;

        return new AbcRibbon(action =>
        {
            var edited = action switch
            {
                AbcAction.OctaveUp => Gesture(n => AbcEdit.Octave(_layout.Reading, n, 1), caret, selection),
                AbcAction.OctaveDown => Gesture(n => AbcEdit.Octave(_layout.Reading, n, -1), caret, selection),
                AbcAction.Longer => Gesture(n => AbcEdit.Length(_layout.Reading, n, 1), caret, selection),
                AbcAction.Shorter => Gesture(n => AbcEdit.Length(_layout.Reading, n, -1), caret, selection),
                AbcAction.Sharpen => Gesture(n => AbcEdit.Accidental(_layout.Reading, n, 1), caret, selection),
                AbcAction.Flatten => Gesture(n => AbcEdit.Accidental(_layout.Reading, n, -1), caret, selection),
                _ => null,
            };

            if (edited is not null) apply(edited);
        });
    }

    /// <summary>How the ribbon reaches the element, which is the only thing that can apply an edit.</summary>
    // ── The gestures ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a note gesture to the selection, or to the note before the caret where nothing is
    /// selected. <strong>The tree it hands back is provisional</strong> — the stages between the parser
    /// and the builder do not re-derive themselves — so it is printed and read again rather than drawn
    /// from directly.
    /// </summary>
    private Edited? Gesture(Func<IReadOnlyList<ContentPart>, AstWrite?> change, int caret,
                            IReadOnlyList<(int Start, int Length)> selection)
    {
        if (Target(caret, selection) is not { Count: > 0 } notes) return null;
        if (change(notes) is not { } write) return null;

        return new Edited(write.Tree.Print(), write.End,
                          selection.Count > 0 && write.Length > 0 ? (write.Start, write.Length) : null);
    }

    /// <summary>
    /// The notes a gesture acts on: everything selected, or the note the caret is in or has just passed.
    /// <para>
    /// Two answers to one question, and the second is what makes the keys usable — a reader who has typed
    /// a note and wants it an octave up should not have to select it first.
    /// </para>
    /// </summary>
    private IReadOnlyList<ContentPart> Target(int caret, IReadOnlyList<(int Start, int Length)> selection)
    {
        if (_layout is null) return [];

        var notes = _layout.Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == AbcKinds.Note && !part.Derived && part.Part(AbcRoles.Letter) is not null)
            .ToList();

        if (selection.Count > 0)
            return [.. notes.Where(note => selection.Any(
                range => note.Start >= range.Start && note.End <= range.Start + range.Length))];

        var here = notes.Where(note => caret > note.Start && caret <= note.End).ToList();
        if (here.Count > 0) return here;

        var before = notes.Where(note => note.End <= caret).OrderByDescending(note => note.End).FirstOrDefault();
        return before is null ? [] : [before];
    }
}
