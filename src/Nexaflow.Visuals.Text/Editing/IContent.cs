using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What an edit came to: the source as it now stands, where the caret goes, and what to leave selected.
/// </summary>
/// <param name="Source">The whole source afterwards. The element re-lays it and tells the document.</param>
/// <param name="Caret">Where the caret lands, as an offset into that source.</param>
/// <param name="Select">What to leave selected, or nothing — which is what typing normally wants.</param>
public sealed record Edited(string Source, int Caret, (int Start, int Length)? Select = null);

/// <summary>
/// A piece of embedded content, to the element that hosts it: something that can read itself, lay itself
/// out, and say what a keystroke means to it.
///
/// <para>
/// <strong>Deliberately small, and deliberately not about drawing.</strong> Everything that happens to a
/// laid-out tree — painting it, hit-testing it, washing a selection over it, waving under what could not
/// be read, moving a caret through it — is the same work whatever was built, and
/// <see cref="ContentElement"/> does it once. What is left here is what genuinely differs: what the
/// source is, how to lay it out, and what typing into it means.
/// </para>
/// <para>
/// The optional members are for content that wants a press or a drawing of its own before the shared
/// gesture gets it — a formula picking a term up to carry it somewhere else, a barcode striking through a
/// symbol it cannot encode. Each defaults to declining, so content with none of those says nothing.
/// </para>
/// </summary>
public interface IContent
{
    /// <summary>
    /// The source this stands for — what a selection over it yields, and what an edit rewrites.
    ///
    /// <para>
    /// Settable, because editing is the shared half: the element works out what a keystroke did to the
    /// text and hands the answer back, and the content's only job is to lay the new text out. Content
    /// that is a run inside something larger — a barcode's value in its fenced block — splices it into
    /// whatever it keeps around it.
    /// </para>
    /// </summary>
    string Source { get; set; }

    /// <summary>
    /// Where <see cref="Source"/> sits inside the block that produced it, for splicing an edit back.
    /// Negative when the whole block <em>is</em> this content, as a <c>$$…$$</c> formula is.
    /// </summary>
    int SourceStart { get; }

    /// <summary>
    /// Lays it out to fit the room it is given. The one method that knows what is being drawn, and the
    /// last one: everything downstream of it is the same code whatever came back.
    /// </summary>
    Laid Lay(double room, double pixelsPerDip);

    /// <summary>
    /// What typing a character means. Null leaves it to the element, which splices the character in — the
    /// right answer for content that is text, and the wrong one for a key that is a gesture on what is
    /// already there.
    /// </summary>
    Edited? Type(char character, int caret, IReadOnlyList<(int Start, int Length)> selection) => null;

    /// <summary>
    /// What a character actually types, where it is not itself: a note letter typed into a tune becomes a
    /// whole note in the octave the one before it was in. Null types the character, which is what text
    /// does. The element still handles the splice, so nothing here has to know what a selection is.
    /// </summary>
    string? Typing(char character, int caret) => null;

    /// <summary>
    /// A key the content claims before the shared handling gets it — a score's Page Up for an octave.
    /// Null leaves it to the element, and then to the document.
    /// </summary>
    Edited? Press(Key key, ModifierKeys modifiers, int caret, IReadOnlyList<(int Start, int Length)> selection) => null;

    /// <summary>
    /// A ribbon of what this content offers on what is selected, for the host to show where the reader
    /// right-clicked. Null from anything with none, which puts the ordinary text menu back.
    /// </summary>
    FrameworkElement? Ribbon(int caret, IReadOnlyList<(int Start, int Length)> selection,
                             Action<Edited> apply) => null;

    /// <summary>
    /// Anything the content draws over the shared picture — a strike through a symbol that will not
    /// encode, a term being carried to a new place. Drawn last, in the tree's own coordinates.
    /// </summary>
    void PaintOver(DrawingContext dc, Laid laid, Brush ink) { }
}
