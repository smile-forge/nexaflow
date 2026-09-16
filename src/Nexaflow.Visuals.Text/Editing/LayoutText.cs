using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Words as pieces of a layout tree.
///
/// <para>
/// This is not about music, maths or diagrams, which is the whole point of it being here. A layout tree
/// is a canvas of things that were drawn, each naming the source it was drawn from, and what kind of
/// thing it is has never mattered to anything that selects, hit-tests or measures. So a title over a
/// tune, a caption under a diagram and the prose between two formulae are one job done once.
/// </para>
/// <para>
/// It arrived the other way round, which is worth remembering. The first version of this lived inside
/// the score builder, as a music engraver that had learnt to draw a title. Everything in it that was
/// really about music — where a title goes relative to a staff, that verses set in two columns — stayed
/// there; everything else was general and had no business being in one content type.
/// </para>
/// </summary>
public static class LayoutText
{
    /// <summary>
    /// Places one run of text into <paramref name="into"/> and hands back where the piece went.
    ///
    /// <para>
    /// The text is aligned within <paramref name="room"/> by the type engine, which is also what breaks a
    /// long run into lines — a title too wide for its page is a paragraph, and how much room it takes is
    /// not known until it has been broken. But the <em>piece</em> is the letters rather than the column
    /// they were aligned in: its extent comes from the mark, which reports where the words actually
    /// landed. That is what a reader drags across and what a wash covers, and a centred title whose
    /// extent was the whole page would highlight the margins either side of itself.
    /// </para>
    /// </summary>
    /// <param name="at">Where the column begins, in the frame of whatever is open.</param>
    /// <param name="part">
    /// The source this text was written in. Without one the piece is drawn and cannot be selected, which
    /// is the right answer for a label the content invented and the wrong one for anything a reader typed.
    /// </param>
    /// <param name="letters">
    /// Where each character of the text was written, so that words select the way words select — a letter,
    /// a word, a phrase — rather than all or nothing. There is nothing special about prose here: a typeset
    /// formula has a piece per glyph and always has, which is why its letters could be picked out one by
    /// one while a title could not.
    /// <para>
    /// Handed in rather than worked out. Whether the nth character of what is drawn is the nth character of
    /// what was written is a fact about the content — a label with a prefix, or two fields set as one line,
    /// is neither — and nothing here is in a position to know it. Null draws the run as one piece.
    /// </para>
    /// </param>
    public static int Place(LayoutBuilder into, FormattedText text, Point at, double room,
                            TextAlignment align, ISourcePart? part, string kind,
                            IReadOnlyList<ISourcePart>? letters = null)
    {
        text.MaxTextWidth = System.Math.Max(1, room);
        text.TextAlignment = align;

        var piece = into.Open(kind, part, at);

        // At the piece's own origin, which is where the column begins. The alignment shift is the type
        // engine's and the mark reports it, so the piece comes out as wide as the words and no wider.
        into.Draw(new TextMark(text, default, null));

        Letters(into, text, letters, kind);

        into.Close();
        return piece;
    }

    /// <summary>
    /// Places a run of text as one piece with a caret position between any two of its letters, and hands back where the
    /// piece went — see <see cref="LayoutWords"/>.
    ///
    /// <para>
    /// What <see cref="Place"/> does with a letter per character, for content whose text is a string rather than a
    /// sequence of separately placed glyphs: a label, a value, a caption. The run answers where inside it a position
    /// is, so nothing is stored per character.
    /// </para>
    /// </summary>
    /// <param name="maps">
    /// Whether what is drawn is what was written, character for character. False for text worked out rather than typed
    /// — a percentage, a number set to two decimal places — which a caret cannot go inside.
    /// </param>
    /// <param name="ink">
    /// What the run is painted in, or null for the foreground of whatever it is drawn on. A text run's own brush is only
    /// how it was measured: painting sets the ink, so a colour asked for has to be asked for here.
    /// </param>
    public static int Words(LayoutBuilder into, FormattedText text, Point at, double room,
                            TextAlignment align, ISourcePart? part, string kind, bool maps = true, bool writes = false,
                            Brush? ink = null)
    {
        text.MaxTextWidth = System.Math.Max(1, room);
        text.TextAlignment = align;

        // A run that says something about a piece of source rather than showing it is nowhere to put a caret: the share a
        // slice takes is worked out, and a reader pressing it means the slice.
        var piece = into.Open(kind, part, at, maps || writes ? Stops.Both : Stops.None);

        into.Draw(new TextMark(text, default, ink));
        into.Words(new LayoutWords(text, default, maps, writes));

        into.Close();
        return piece;
    }

    /// <summary>The kind of piece a hole is drawn as — see <see cref="Hole"/>.</summary>
    public const string HoleKind = "Hole";

    /// <summary>How far a hole runs across for how far up it runs: squat enough to read as a slot, as a formula's does.</summary>
    private const double HoleAspect = 0.55 / 0.62;

    /// <summary>How wide a hole standing among letters like <paramref name="letter"/> is drawn.</summary>
    public static double HoleWidth(FormattedText letter) => letter.Extent * HoleAspect;

    /// <summary>
    /// Places a hole: the hollow box standing where something is still to be written, which a formula draws in an argument
    /// left empty.
    ///
    /// <para>
    /// It stands for no characters — its part is empty, and sits where they will go — so it is somewhere to put the caret,
    /// and what is typed at it lands where it stands. As tall as a small letter and resting on the line, so it reads as a
    /// letter still to come rather than a box drawn over the words, and as tall as the line to anything asking where a caret
    /// in it goes.
    /// </para>
    /// </summary>
    /// <param name="hole">The place in the source it stands at.</param>
    /// <param name="letter">A small letter in the type it stands among: its ink is how tall the box is, and its baseline where it sits.</param>
    public static int Hole(LayoutBuilder into, ISourcePart hole, Point at, FormattedText letter, Brush ink)
    {
        var height = letter.Extent;
        var width = HoleWidth(letter);
        var hairline = System.Math.Max(height * 0.07, 0.75);

        var box = new RectangleGeometry(new Rect(hairline / 2, letter.Baseline - height + (hairline / 2),
                                                 System.Math.Max(width - hairline, 0), System.Math.Max(height - hairline, 0)));
        box.Freeze();

        var piece = into.Open(HoleKind, hole, at);
        into.Draw(new GeometryMark(box, null, ink, hairline));
        into.Covers(new Rect(0, 0, width, letter.Height));
        into.Close();
        return piece;
    }

    /// <summary>
    /// One piece per character, so the run can be selected through rather than only as a whole.
    ///
    /// <para>
    /// They draw nothing — the run drew all of it in one go, which is what keeps the type engine's kerning
    /// and line breaking intact. These are geometry and a place in the source, which is all a selection
    /// ever asks of a piece.
    /// </para>
    /// </summary>
    private static void Letters(LayoutBuilder into, FormattedText text,
                                IReadOnlyList<ISourcePart>? letters, string kind)
    {
        if (letters is null || letters.Count != text.Text.Length) return;

        for (var i = 0; i < letters.Count; i++)
        {
            if (text.BuildHighlightGeometry(default, i, 1) is not { } box) continue;

            var bounds = box.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

            into.Open(kind + "-letter", letters[i], bounds.TopLeft);
            into.Covers(new Rect(0, 0, bounds.Width, bounds.Height));
            into.Close();
        }
    }

    /// <summary>What a piece laid out as its own characters is called — see <see cref="Shown"/>.</summary>
    public const string SourceKind = "Source";

    /// <summary>
    /// The source itself, laid out as the characters it is written with — what a builder hands back when it
    /// cannot read what it was given.
    ///
    /// <para>
    /// A whole layout rather than a special case, which is the point. The caret, the selection, the hit
    /// test and the painter all work on it unchanged, because it is a tree of pieces naming source like any
    /// other — so nothing hosting content needs a second path for content that would not read, and a reader
    /// can go on typing in the very thing that is broken until it is not.
    /// </para>
    /// <para>
    /// One piece per character, so a selection can take part of it. That is the same treatment prose gets
    /// and it matters more here: unreadable source is exactly what somebody is in the middle of fixing.
    /// </para>
    /// </summary>
    /// <param name="source">
    /// The characters the content was asked to read. Empty is allowed and is not an error — it is what an
    /// empty formula in a document being written looks like, and it still wants somewhere to put a caret.
    /// </param>
    /// <param name="text">
    /// How this content sets raw characters — its typeface and size, which are the content's own. It may
    /// hold something other than <paramref name="source"/>: a blank stands in for empty source so the line
    /// has a height, and then the characters name nothing, which is correct.
    /// </param>
    /// <param name="trouble">What went wrong, or nothing where the source was simply empty.</param>
    public static Laid Shown(string source, FormattedText text, IReadOnlyList<Diagnostic> trouble)
    {
        source ??= string.Empty;

        // Only where what is drawn is what was written. A blank standing in for empty source is not the
        // reader's character and must not become a place they can put the caret.
        var letters = text.Text.Length == source.Length
            ? (IReadOnlyList<ISourcePart>)[.. Enumerable.Range(0, source.Length).Select(at => (ISourcePart)new SourceSpan(at, 1))]
            : null;

        var width = text.WidthIncludingTrailingWhitespace;
        var height = text.Height;

        var build = new LayoutBuilder();
        Place(build, text, default, width, TextAlignment.Left, new SourceSpan(0, source.Length), SourceKind, letters);

        return new Laid(build.Seal(), new Size(width, height), trouble);
    }
}
