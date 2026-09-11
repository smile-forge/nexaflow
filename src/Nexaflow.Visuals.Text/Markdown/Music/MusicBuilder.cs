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
/// The engraver every notation shares: handed a tune as rows of bars of events, it decides where every piece
/// of it goes and lays it into a tree.
///
/// <para>
/// A notation's own builder is only its reading. ABC and LilyPond write the same music very differently — one
/// bars itself with the lines it types, the other with its meter — but what they amount to is the same:
/// events with heads, lengths and marks, in bars, on staves. Each builder turns its own tree into that, and
/// everything from there to the page is here, once.
/// </para>
/// <para>
/// <strong>It never names a point in the source.</strong> Every event, bar line and syllable carries the part it
/// was drawn from, and where that part is written is the reading's to answer. What nobody wrote — a bar line
/// the meter implied — carries nothing, and is drawn without being selectable.
/// </para>
/// <para>
/// The judgement calls are not here either. Stem direction, beam slope and note spacing live in
/// <see cref="Engraving"/> and <see cref="ScoreMetrics"/>, where they can be asserted rather than eyeballed.
/// </para>
/// </summary>
internal abstract partial class MusicBuilder : ContentBuilder
{
    /// <summary>
    /// How big to set source that could not be engraved at all. A fixed size rather than one derived from
    /// the staff, because in that case there is no staff — this is the fallback for an engraver that threw,
    /// so nothing it would have measured can be trusted.
    /// </summary>
    private const double SourceSize = 13;

    private readonly double _width;
    private readonly Brush _ink;
    private readonly double _ppd;

    /// <summary>How much air this engraving puts between things — see <see cref="ScoreSpacing"/>.</summary>
    private readonly ScoreSpacing _spacing;

    /// <param name="source">The music, as it is written.</param>
    /// <param name="width">How much room it has to lay itself out in.</param>
    /// <param name="spacing">
    /// How much air to leave between things, or null for what the engraver normally uses. A caller passes
    /// something else only to compare two engravings without the comparison being about this.
    /// </param>
    protected MusicBuilder(string source, double width, Brush ink, double pixelsPerDip, ScoreSpacing? spacing)
        : base(source)
    {
        _width = width;
        _ink = ink;
        _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
        _spacing = spacing ?? ScoreSpacing.Current;
    }

    /// <summary>
    /// What a notation's reading hands the engraver: the rows to set, the prose around them, and the reading
    /// they came from — which is where whatever could not be read says so.
    /// </summary>
    protected sealed record Tune(List<Row> Rows, MusicHeader Header, ContentReading Reading);

    /// <summary>Reads the source into rows of bars of events. The one thing each notation writes for itself.</summary>
    protected abstract Tune ReadTune();

    /// <summary>
    /// Reads the music and engraves it to fit, and hands back nothing music-shaped: a tree of pieces, how
    /// much room it wants, and whatever could not be read.
    /// </summary>
    protected sealed override Laid? Read()
    {
        var tune = ReadTune();
        var (tree, size) = Engrave(tune, _width);

        // Asked of the reading rather than collected on the way through it. A piece that could not be read
        // carries the reason, so there is one place the answer lives and no second list to fall out of step
        // with it — and a piece being typed carries nothing, which is how it draws without being complained
        // about.
        var trouble = tune.Reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null && part.Length > 0)
            .Select(part => new Diagnostic(part.Start, part.Length, DiagnosticSeverity.Warning, part.Trouble!)
            {
                Part = part,
            })
            .ToList();

        return new Laid(tree, size, trouble);
    }

    /// <summary>
    /// How music sets characters it could not engrave: monospaced, so a reader can count the bar lines in
    /// what they wrote.
    /// </summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            SourceSize,
            Brushes.Black,   // never used: the mark takes the theme's ink at paint time
            _ppd);

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

        /// <summary>
        /// The beam this was written into, if any — the group a click selects first. Its identity is what
        /// says two events share one, so every event of a group holds the same one.
        /// </summary>
        public ISourcePart? Beam;

        /// <summary>The tuplet this was written into, and the number printed over it.</summary>
        public ISourcePart? Tuplet;
        public int TupletNumber;

        public string? ChordSymbol;

        /// <summary>What named the chord printed over it — what selecting the chord selects.</summary>
        public ISourcePart? ChordPart;

        /// <summary>
        /// The syllables sung on this event: which verse, what it says, and the characters it was written with
        /// — which are wherever the words were written, usually nowhere near the note.
        /// </summary>
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
    /// A bar line: how it is drawn, and what wrote it.
    ///
    /// <para>
    /// Drawn from ABC's spelling, which says what a bar line looks like mark by mark — <c>|</c> a thin line,
    /// <c>[</c> and <c>]</c> a thick one, <c>:</c> the dots of a repeat — so <c>|:</c> is a start repeat and
    /// <c>|]</c> the end. A notation that spells its bar lines otherwise says what it means in this spelling.
    /// </para>
    /// <para>
    /// The part is null for a line nobody wrote. It is drawn all the same, and cannot be selected.
    /// </para>
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
        /// The meter this bar changes to, and the sign it was written as where it was written as one.
        ///
        /// <para>
        /// The sign travels with the change rather than sitting on the builder. A tune may write several
        /// meters — <c>C</c> then <c>C|</c> then <c>6/8</c> — and one field holding "the sign" ends up holding
        /// the last one, which is the wrong answer for every bar including the last.
        /// </para>
        /// </summary>
        public (int Beats, int Unit, int? Sign)? MeterChange;

        /// <summary>The repeat bracket opening at this bar, what wrote it, and its label.</summary>
        public ISourcePart? Volta;
        public string? VoltaLabel;

        /// <summary>True when the line closing this bar ends a repeat, which is where a bracket stops.</summary>
        public bool EndsRepeat;
    }

    /// <summary>One line of music, which is where a system may break.</summary>
    protected sealed class Row
    {
        public List<Bar> Bars = [];
        public ClefKind Clef = ClefKind.Treble;

        /// <summary>The key it opens in, as sharps (positive) or flats (negative).</summary>
        public int Fifths;

        /// <summary>
        /// The meter printed at its head, and the sign it was written as where it was written as one — or
        /// null where none is printed, because none was written or the music keeps no meter.
        /// </summary>
        public (int Beats, int Unit, int? Sign)? Meter;

        /// <summary>
        /// Which voice wrote it, and how many of that voice's lines came before it. The n-th lines of every
        /// voice sound together, which is the whole of what makes a system a system.
        /// </summary>
        public string Voice = "";
        public int Index;

        /// <summary>What to print at the left of the first system, where a voice has a name.</summary>
        public string? Name;
    }

    /// <summary>
    /// A written note value and its dots, from a length in quarter notes. Chosen rather than computed,
    /// because notation has a fixed set of shapes and a length that is not one of them has to be drawn as
    /// the nearest that is.
    /// </summary>
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
