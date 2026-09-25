using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Words as pieces of a layout tree. Deliberately not about music, maths or diagrams: a layout tree is a
/// canvas of things that were drawn, each naming the source it was drawn from, and what kind of thing it
/// is has never mattered to anything that selects, hit-tests or measures — so a title over a tune, a
/// caption under a diagram and the prose between two formulae are one job done once.
/// </summary>
public static class LayoutText
{
    /// <summary>
    /// What text is measured at, everywhere. A layout is in the content's own units and the element scales it as it
    /// paints, so measurement must not vary with the screen it happened to be measured on — otherwise the same
    /// source lays out differently on two monitors, and no test can measure either of them.
    /// </summary>
    public const double Density = 1.0;

    /// <summary>
    /// Places one run of text into <paramref name="into"/> and hands back where the piece went. The text is
    /// aligned within <paramref name="room"/> by the type engine, which also breaks a long run into lines, so
    /// how much room it takes isn't known until broken. The <em>piece</em>'s extent is the letters, not the
    /// column they were aligned in — it comes from the mark, which reports where the words actually landed —
    /// since that's what a reader drags across and a wash covers; a centred title extending to the whole page
    /// would highlight the margins beside itself.
    /// </summary>
    /// <param name="lines">
    /// How the text's characters are set, where the caller has it — the builder's own setting of raw characters — so each line
    /// can be measured on its own for where its letters stand. Measuring a letter inside a text of many lines sets every line of
    /// it again, which for a whole block shown as written is every line for every character.
    /// </param>
    public static int Place(LayoutBuilder into, FormattedText text, Point at, double room,
                            TextAlignment align, ISourcePart? part, string kind,
                            IReadOnlyList<ISourcePart>? letters = null, System.Func<string, FormattedText>? lines = null)
    {
        Bound(text, room, align);

        var piece = into.Open(kind, part, at);

        // At the piece's own origin, which is where the column begins. The alignment shift is the type
        // engine's and the mark reports it, so the piece comes out as wide as the words and no wider.
        into.Draw(new TextMark(text, default, null));

        if (lines is not null && align == TextAlignment.Left && text.Text.Contains('\n')) Lined(into, text.Text, letters, kind, lines);
        else Letters(into, text, letters, kind);

        into.Close();
        return piece;
    }

    /// <summary>
    /// Places a run of text as one piece with a caret position between any two of its letters, and hands back where the
    /// piece went — see <see cref="LayoutWords"/>. What <see cref="Place"/> does with a letter per character, but for
    /// content whose text is a string rather than separately placed glyphs (a label, a value, a caption): the run
    /// answers where inside it a position is, so nothing is stored per character.
    /// </summary>
    /// <param name="degrees">
    /// How far the run is turned about where it starts — nought for the ordinary case. A turned run reaches where its letters
    /// land rather than where they were set, and stands in the letters themselves, so a press on it means the letter under the
    /// pointer and a caret in it stands turned with it (<see cref="Piece.Turned"/>).
    /// </param>
    public static int Words(LayoutBuilder into, FormattedText text, Point at, double room,
                            TextAlignment align, ISourcePart? part, string kind, bool maps = true, bool writes = false,
                            Brush? ink = null, double degrees = 0)
    {
        Bound(text, room, align);

        var turn = degrees == 0 ? null : new LayoutPaint([new RotateTransform(degrees)]);

        // A run that says something about a piece of source rather than showing it is nowhere to put a caret: the share a
        // slice takes is worked out, and a reader pressing it means the slice.
        var piece = into.Open(kind, part, at, maps || writes ? Stops.Both : Stops.None, gathers: turn is null, paints: turn);

        into.Draw(new TextMark(text, default, ink));
        into.Words(new LayoutWords(text, default, maps, writes));

        if (turn is not null)
        {
            var letters = new Rect(0, 0, text.Width, text.Height);
            into.Covers(turn.Turn![0].TransformBounds(letters));
            into.Occupies(new RectangleGeometry(letters));
        }

        into.Close();
        return piece;
    }

    /// <summary>
    /// Gives <paramref name="text"/> its room and alignment — only where they differ from what it has, because either one
    /// set throws away everything the type engine worked out about the text, and a run measured to find where its line
    /// breaks would then be shaped all over again to find where its piece reaches. Room that is unbounded leaves the text
    /// unbounded: a run already cut to a line is never broken again.
    /// </summary>
    private static void Bound(FormattedText text, double room, TextAlignment align)
    {
        if (double.IsFinite(room))
        {
            var most = System.Math.Max(1, room);
            if (text.MaxTextWidth != most) text.MaxTextWidth = most;
        }

        if (text.TextAlignment != align) text.TextAlignment = align;
    }

    /// <summary>The kind of piece a hole is drawn as — see <see cref="Hole"/>.</summary>
    public const string HoleKind = "Hole";

    /// <summary>How far a hole runs across for how far up it runs: squat enough to read as a slot, as a formula's does.</summary>
    private const double HoleAspect = 0.55 / 0.62;

    /// <summary>How wide a hole standing among letters like <paramref name="letter"/> is drawn.</summary>
    public static double HoleWidth(FormattedText letter) => letter.Extent * HoleAspect;

    /// <summary>
    /// Places a hole: the hollow box standing where something is still to be written, which a formula draws in an argument
    /// left empty. Stands for no characters — its part is empty, sitting where they will go — so it's somewhere to put
    /// the caret, and what is typed at it lands where it stands. As tall as a small letter and resting on the line, so it
    /// reads as a letter still to come rather than a box drawn over the words.
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

    /// <summary>One piece per character, so the run can be selected through rather than only as a whole. These draw nothing themselves (the run drew it all in one go, keeping the type engine's kerning and line breaking intact) — just geometry and a place in the source, which is all a selection ever asks of a piece.</summary>
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

    /// <summary>
    /// A letter for each character, measured a line at a time: each line set on its own, and its letters placed a line's height
    /// further down for every line above it — which is where a text of many lines, one high each and never broken, sets them.
    /// </summary>
    private static void Lined(LayoutBuilder into, string source, IReadOnlyList<ISourcePart>? letters, string kind,
                              System.Func<string, FormattedText> lines)
    {
        if (letters is null || letters.Count != source.Length) return;

        var blank = lines(" ").Height;
        var top = 0.0;

        for (var start = 0; start <= source.Length;)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0) end = source.Length;

            // A line's own ending is where it stops, not a letter of it.
            var shown = end > start && source[end - 1] == '\r' ? end - 1 : end;
            var line = shown > start ? lines(source[start..shown]) : null;

            for (var at = start; at < shown; at++)
            {
                if (line!.BuildHighlightGeometry(new Point(0, top), at - start, 1) is not { } box) continue;

                var bounds = box.Bounds;
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;

                into.Open(kind + "-letter", letters[at], bounds.TopLeft);
                into.Covers(new Rect(0, 0, bounds.Width, bounds.Height));
                into.Close();
            }

            top += line?.Height ?? blank;
            start = end + 1;
        }
    }

    /// <summary>What a piece laid out as its own characters is called — see <see cref="Shown"/>.</summary>
    public const string SourceKind = "Source";

    /// <summary>
    /// The source itself, laid out as the characters it is written with — what a builder hands back when it
    /// cannot read what it was given. A whole layout rather than a special case: caret, selection, hit-test
    /// and painter all work on it unchanged, since it is a tree of pieces naming source like any other, so
    /// nothing hosting content needs a second path for content that would not read and a reader can keep
    /// typing in the very thing that is broken. One piece per character, so a selection can take part of it —
    /// unreadable source is exactly what somebody is in the middle of fixing.
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
    public static Laid Shown(string source, FormattedText text, IReadOnlyList<Diagnostic> trouble, int at = 0)
    {
        source ??= string.Empty;

        // Only where what is drawn is what was written. A blank standing in for empty source is not the
        // reader's character and must not become a place they can put the caret.
        var letters = text.Text.Length == source.Length
            ? (IReadOnlyList<ISourcePart>)[.. Enumerable.Range(at, source.Length).Select(letter => (ISourcePart)new SourceSpan(letter, 1))]
            : null;

        var width = text.WidthIncludingTrailingWhitespace;
        var height = text.Height;

        var build = new LayoutBuilder();
        Place(build, text, default, width, TextAlignment.Left, new SourceSpan(at, source.Length), SourceKind, letters);

        return new Laid(build.Seal(), new Size(width, height), trouble);
    }
}
