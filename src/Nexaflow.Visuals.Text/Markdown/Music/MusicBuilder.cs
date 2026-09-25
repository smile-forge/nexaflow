using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// The engraver every notation shares: turns a tune (rows of bars of events) into a laid-out tree. A
/// notation's builder is only its reading — ABC and LilyPond bar themselves very differently but both
/// resolve to events with heads, lengths and marks, in bars, on staves; everything past that is here, once.
///
/// <para>
/// Every event, bar line and syllable carries the source part it was drawn from; a bar line nobody wrote
/// carries none and can't be selected. Stem direction, beam slope and spacing live in
/// <see cref="Engraving"/> and <see cref="ScoreMetrics"/>, not here.
/// </para>
/// </summary>
internal abstract partial class MusicBuilder : ContentBuilder
{
    /// <summary>Fallback size for source that couldn't be engraved — fixed, since in that case there's no staff to derive it from.</summary>
    private const double SourceSize = 13;

    
    /// <summary>What a score is drawn in.</summary>
    private Brush _ink => Style.Text;
    /// <summary>How much air this engraving puts between things — see <see cref="ScoreSpacing"/>.</summary>
    /// <summary>How much air this engraving puts between things. One setting, not a knob.</summary>
    private static readonly ScoreSpacing _spacing = ScoreSpacing.Current;

    /// <param name="spacing">Null uses the engraver's normal spacing; pass another only to compare two engravings without the comparison being about spacing.</param>
    protected MusicBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting)
    {
    
    
    
    
    }

    /// <summary>What a notation's reading hands the engraver, including where anything unreadable is reported.</summary>
    protected sealed record Tune(List<Row> Rows, MusicHeader Header, ContentReading Reading);

    /// <summary>The reading as rows of bars of events. The one thing each notation works out for itself.</summary>
    protected abstract Tune ReadTune();

    /// <summary>
    /// Reads and engraves the tune — given a page, in a share of its width, set in the middle of it.
    ///
    /// <para>
    /// The block is the whole width with the margins inside. A score set edge to edge across a wide window is a score nobody can
    /// read: the eye has to travel the whole width to follow one system, and the systems stop looking like lines of music.
    /// Printed music has margins for the same reason prose does.
    /// </para>
    /// </summary>
    protected sealed override Laid? Build()
    {
        if (!double.IsFinite(Room)) return Score(Unbounded);

        var score = Score(Room * PageWidth);
        var page = new LayoutBuilder();
        var width = Math.Max(Room, score.Size.Width);

        new ContentInset(score).Set(page, new Point((width - score.Size.Width) / 2, 0), MusicPiece.Page);

        return new Laid(page.Seal(), new Size(width, score.Size.Height), score.Trouble);
    }

    /// <summary>How much of a page's width the music takes.</summary>
    private const double PageWidth = 0.8;

    /// <summary>A width to engrave against where there is no page, since a score set to infinity has nowhere to break.</summary>
    private const double Unbounded = 420;

    /// <summary>The tune engraved at <paramref name="room"/>, returning the laid-out tree, its size, and anything unreadable.</summary>
    private Laid Score(double room)
    {
        var tune = ReadTune();

        // A tune is only ever read where it is drawn — nothing in a score is typed into — so anything wrong in it is put right
        // in its source: shown as written, with each wrong part marked and why.
        var troubled = tune.Reading.Root.SelfAndDescendants().Where(part => part.Trouble is not null && part.Length > 0 && !part.Derived).ToList();
        if (troubled.Count > 0) return AsSource([.. troubled.Select(part => (part, part.Trouble!))], room);

        var (tree, size) = Engrave(tune, room);
        return new Laid(tree, size, []);
    }

    /// <summary>Unengraveable source is set monospaced, so a reader can count bar lines in it.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(Style.MonoFont),
            SourceSize,
            Brushes.Black,   // never used: the mark takes the theme's ink at paint time
            Editing.LayoutText.Density);

    // ── What there is to draw ───────────────────────────────────────────────

    /// <summary>One event, read off the tree and not yet placed.</summary>
    protected sealed class Event
    {
        /// <summary>What was written — what selecting the note selects.</summary>
        public required ISourcePart Part;

        /// <summary>Where each of its heads sits, as a diatonic index (C4 is 28), lowest first.</summary>
        public int[] Heads = [];

        /// <summary>The accidental printed before each head, or null for none.</summary>
        public int?[] Accidentals = [];

        public int BaseValue = 4;      // 1 = whole, 2 = half, 4 = quarter, 8 = eighth …
        public int Dots;
        public bool IsRest;
        public bool Invisible;
        public bool WholeBar;
        public double Quarters;

        /// <summary>The beam this event belongs to, if any; identity (not value) is what says two events share a group.</summary>
        public ISourcePart? Beam;

        /// <summary>The tuplet this was written into, and the number printed over it.</summary>
        public ISourcePart? Tuplet;
        public int TupletNumber;

        public string? ChordSymbol;

        /// <summary>What named the chord printed over it — what selecting the chord selects.</summary>
        public ISourcePart? ChordPart;

        /// <summary>Syllables sung on this event; the source part is wherever the words were written, usually far from the note.</summary>
        public List<(int Verse, string Text, bool Hyphen, bool Melisma, ISourcePart? Part)> Lyrics = [];

        /// <summary>Marks that hug the head, on the side the stem is not: staccato, tenuto, accent.</summary>
        public List<int> HeadMarks = [];

        /// <summary>Marks that stack clear of the staff: a fermata, an ornament, a bowing, a segno.</summary>
        public List<int> StaffMarks = [];

        /// <summary>Text placed relative to this event.</summary>
        public List<(string Text, AnnotationPlacement Where)> Annotations = [];

        /// <summary>True when a tie runs from this event to the next of the same pitch.</summary>
        public bool TieStart;

        /// <summary>How many slurs open on this event, and how many close on it. Slurs may nest.</summary>
        public int SlurOpen;
        public int SlurClose;

        /// <summary>Grace notes crushed in before it, each a diatonic index and a written value.</summary>
        public List<(int Half, int Value)> Graces = [];

        /// <summary>True for an acciaccatura — the group is drawn with a slash through its stem.</summary>
        public bool GraceSlashed;

        // Placed, once the system is laid out.
        public double SlotWidth;
        public double AccidentalWidth;
        public double GraceWidth;
        public double X;

        public bool Beamable => BaseValue >= 8 && !IsRest;
    }

    /// <summary>
    /// A bar line: how it's drawn — from ABC's spelling, mark by mark (<c>|</c> thin, <c>[</c>/<c>]</c> thick,
    /// <c>:</c> repeat dots; a notation spelling bar lines otherwise says what it means in this spelling) —
    /// and what wrote it. Part is null for a line nobody wrote; still drawn, never selectable.
    /// </summary>
    protected sealed record Barline(string Drawn, ISourcePart? Part);

    /// <summary>One bar, and the lines that opened and closed it.</summary>
    protected sealed class Bar
    {
        /// <summary>What wrote the bar, where anything did as a whole.</summary>
        public ISourcePart? Part;

        public Barline? Opened;
        public Barline? Closed;
        public List<Event> Events = [];
        public double Width;
        public double X;

        /// <summary>Room taken at the head of this bar by a key or meter written in the middle of a tune.</summary>
        public double SignatureWidth;
        public KeySignature? KeyChange;

        /// <summary>
        /// The meter this bar changes to, with its own sign — per-bar because a tune may write several meters
        /// (<c>C</c> then <c>C|</c> then <c>6/8</c>), and a single builder-level field would only hold the last.
        /// </summary>
        public (int Beats, int Unit, int? Sign)? MeterChange;

        /// <summary>The repeat bracket opening at this bar, what wrote it, and its label.</summary>
        public ISourcePart? Volta;
        public string? VoltaLabel;

        /// <summary>True when the line closing this bar ends a repeat, which is where a bracket stops.</summary>
        public bool EndsRepeat;


        /// <summary>True at the last of a set of numbered endings — LilyPond closes the bracket there, not at line end.</summary>
        public bool EndsBracket;
    }

    /// <summary>One line of music, which is where a system may break.</summary>
    protected sealed class Row
    {
        public List<Bar> Bars = [];
        public ClefKind Clef = ClefKind.Treble;

        /// <summary>The key it opens in, as sharps (positive) or flats (negative).</summary>
        public int Fifths;

        /// <summary>The meter printed at its head and its sign, or null when nothing is printed.</summary>
        public (int Beats, int Unit, int? Sign)? Meter;

        /// <summary>Which voice wrote it and its index among that voice's lines — the n-th lines of every voice sound together as one system.</summary>
        public string Voice = "";
        public int Index;


        /// <summary>Which piece it belongs to, where one source holds several (LilyPond scores each top-level music expression separately). Rows of different pieces never share a system.</summary>
        public int Piece;

        /// <summary>What to print at the left of the first system, where a voice has a name.</summary>
        public string? Name;
    }

    /// <summary>The written value and dots nearest a length in quarter notes — chosen, since notation has a fixed set of shapes.</summary>
    protected static (int Value, int Dots) Value(double quarters)
    {
        if (quarters <= 0) return (4, 0);

        var best = (Value: 4, Dots: 0);
        var bestError = double.MaxValue;

        foreach (var value in (int[])[1, 2, 4, 8, 16, 32, 64])
            for (var dots = 0; dots <= 3; dots++)
            {
                var length = 4.0 / value * (2.0 - Math.Pow(0.5, dots));
                var error = Math.Abs(length - quarters);
                if (error >= bestError) continue;

                bestError = error;
                best = (value, dots);
            }

        // A breve is twice a whole note and has no place in the loop above, which counts downward from one.
        if (Math.Abs(quarters - 8) < Math.Abs(4.0 / best.Value * (2.0 - Math.Pow(0.5, best.Dots)) - quarters))
            return (0, 0);

        return best;
    }
}
