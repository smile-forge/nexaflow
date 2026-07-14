using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The LilyPond language, construct by construct — the counterpart to <see cref="AbcSyntaxTests"/>, and asserted
/// against the same IR, because the two dialects have to arrive at the same place for one engraver to draw both.
///
/// The three that have no ABC counterpart get the most attention, because they are where a LilyPond parse can be
/// wrong in a way an ABC parse cannot: bars come from the meter rather than from a typed <c>|</c>, beams come from
/// the meter rather than from the spacing, and a printed accidental is an engraving decision rather than something
/// the source asked for.
/// </summary>
[TestClass]
[CoversNode("ly-core")]
public class LilyPondSyntaxTests
{
    private static Score P(string src) => new LilyPondParser().Parse(src);

    private static List<MusicalEvent> Events(Staff s) => [.. s.Measures.SelectMany(m => m.Events)];

    private static List<Note> Notes(Staff s) => [.. Events(s).OfType<Note>()];

    // ── Pitch entry ─────────────────────────────────────────────────────────

    [TestMethod]
    public void AbsolutePitch_PutsMiddleCAtCPrime()
    {
        // LilyPond's absolute mode: c is C3, c' is middle C.
        var n = Notes(P("{ c4 c' c'' c, }").Staves[0]);
        Assert.AreEqual(3, n[0].Pitch.Octave);
        Assert.AreEqual(4, n[1].Pitch.Octave);
        Assert.AreEqual(5, n[2].Pitch.Octave);
        Assert.AreEqual(2, n[3].Pitch.Octave);
    }

    [TestMethod]
    public void RelativePitch_TakesTheNearestOctave()
    {
        // From c' (C4): g is a fourth below (G3), then c is a fourth above that (C4).
        var n = Notes(P("\\relative c' { c4 g c g, }").Staves[0]);
        Assert.AreEqual("C4", n[0].Pitch.ToString());
        Assert.AreEqual("G3", n[1].Pitch.ToString(), "the nearest g to c' is below it");
        Assert.AreEqual("C4", n[2].Pitch.ToString());
        Assert.AreEqual("G2", n[3].Pitch.ToString(), "the , mark drops it another octave");
    }

    [TestMethod]
    public void Fixed_AnchorsTheOctaveWithoutTracking()
    {
        var n = Notes(P("\\fixed c' { c4 g c' }").Staves[0]);
        Assert.AreEqual("C4", n[0].Pitch.ToString());
        Assert.AreEqual("G4", n[1].Pitch.ToString(), "no relative tracking — g is simply in the c' octave");
        Assert.AreEqual("C5", n[2].Pitch.ToString());
    }

    [TestMethod]
    [DataRow("cis", 1)]
    [DataRow("cisis", 2)]
    [DataRow("ces", -1)]
    [DataRow("ceses", -2)]
    [DataRow("c", 0)]
    public void DutchNoteNames_CarryTheirOwnAlteration(string word, int alter)
    {
        var n = Notes(P($"{{ {word}4 }}").Staves[0]);
        Assert.AreEqual(alter, n[0].Pitch.Alter);
    }

    [TestMethod]
    public void ContractedFlats_AreRead()
    {
        // aes/ees are conventionally written as/es.
        var n = Notes(P("{ as4 es }").Staves[0]);
        Assert.AreEqual(-1, n[0].Pitch.Alter, "as = a flat");
        Assert.AreEqual('A', n[0].Pitch.Letter);
        Assert.AreEqual(-1, n[1].Pitch.Alter, "es = e flat");
        Assert.AreEqual('E', n[1].Pitch.Letter);
    }

    // ── Durations ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Duration_CarriesOverUntilItChanges()
    {
        var e = Events(P("{ c4 d e f8 g a }").Staves[0]);
        Assert.AreEqual(4, e[1].Duration.Base, "d inherits the quarter");
        Assert.AreEqual(4, e[2].Duration.Base);
        Assert.AreEqual(8, e[4].Duration.Base, "g inherits the eighth");
        Assert.AreEqual(8, e[5].Duration.Base);
    }

    [TestMethod]
    public void Durations_FromBreveToSixtyFourth()
    {
        var e = Events(P("{ \\time 4/4 c\\breve c1 c2 c4 c8 c16 c32 c64 }").Staves[0]);
        Assert.AreEqual(8.0, e[0].Duration.QuarterLength, 1e-9, "\\breve is a double whole");
        Assert.IsTrue(e[0].Duration.IsBreve);
        int[] bases = [0, 1, 2, 4, 8, 16, 32, 64];
        for (int i = 0; i < bases.Length; i++)
            Assert.AreEqual(bases[i], e[i].Duration.Base);
    }

    [TestMethod]
    public void Dots_AddHalfEachTime()
    {
        var e = Events(P("{ \\time 4/4 c2. c4.. }").Staves[0]);
        Assert.AreEqual(3.0, e[0].Duration.QuarterLength, 1e-9);
        Assert.AreEqual(1.75, e[1].Duration.QuarterLength, 1e-9);
    }

    [TestMethod]
    public void DurationScale_MultipliesTheWrittenValue()
    {
        var e = Events(P("{ \\time 4/4 c2*2 }").Staves[0]);
        Assert.AreEqual(4.0, e[0].Duration.QuarterLength, 1e-9, "a half note times two is a whole note");
    }

    // ── Bars come from the meter ────────────────────────────────────────────

    [TestMethod]
    public void Measures_AreClosedByTheMeter_WithNoBarLinesTyped()
    {
        // Not one '|' in the source. In LilyPond a bar line is *implied*; getting this wrong is the single
        // biggest way a LilyPond parse can differ from an ABC one that looks identical.
        var st = P("\\relative c' { \\time 3/4 c4 d e f g a b c d }").Staves[0];
        Assert.AreEqual(3, st.Measures.Count);
        foreach (var m in st.Measures) Assert.AreEqual(3, m.Events.Count);
    }

    [TestMethod]
    public void BarCheck_ClosesTheMeasureItChecks()
    {
        var st = P("\\relative c' { \\time 4/4 c4 d e f | g1 }").Staves[0];
        Assert.AreEqual(2, st.Measures.Count);
        Assert.AreEqual(4, st.Measures[0].Events.Count);
        Assert.AreEqual(1, st.Measures[1].Events.Count);
    }

    [TestMethod]
    public void Partial_ShortensTheFirstBarOnly()
    {
        var st = P("\\relative c' { \\time 4/4 \\partial 4 g4 | c4 d e f | g1 }").Staves[0];
        Assert.AreEqual(3, st.Measures.Count);
        Assert.AreEqual(1, st.Measures[0].Events.Count, "the pickup is a bar of one beat");
        Assert.AreEqual(4, st.Measures[1].Events.Count);
    }

    [TestMethod]
    public void MidTuneMeterChange_RebarsWhatFollows()
    {
        var st = P("\\relative c' { \\time 4/4 c4 d e f \\time 3/4 g4 a b c d e }").Staves[0];
        Assert.AreEqual(3, st.Measures.Count);
        Assert.AreEqual(4, st.Measures[0].Events.Count);
        Assert.AreEqual(3, st.Measures[1].Events.Count);
        Assert.AreEqual(new TimeSignature(3, 4), st.Measures[1].TimeChange);
    }

    [TestMethod]
    public void MultiMeasureRest_IsWrittenOutOneBarAtATime()
    {
        var st = P("\\relative c' { \\time 4/4 c1 R1*3 c1 }").Staves[0];
        Assert.AreEqual(5, st.Measures.Count, "R1*3 is three bars, not one long one");
        for (int i = 1; i <= 3; i++)
        {
            var r = (Rest)st.Measures[i].Events[0];
            Assert.IsTrue(r.IsWholeMeasure);
        }
    }

    [TestMethod]
    public void SpacerRest_TakesTimeButPrintsNothing()
    {
        var st = P("\\relative c' { \\time 4/4 s2 c4 d }").Staves[0];
        var r = (Rest)st.Measures[0].Events[0];
        Assert.IsTrue(r.IsInvisible);
        Assert.AreEqual(2.0, r.Duration.QuarterLength, 1e-9);
    }

    // ── Bar lines ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ExplicitBarLine_LandsOnTheBarItFollows_EvenWhenTheMeterAlreadyClosedIt()
    {
        // The meter closes the bar after 'f'; the \bar arrives afterwards and still has to attach to it.
        var st = P("\\relative c' { \\time 4/4 c4 d e f \\bar \"||\" g1 \\bar \"|.\" }").Staves[0];
        Assert.AreEqual(2, st.Measures.Count);
        Assert.AreEqual(BarlineKind.Double, st.Measures[0].EndBarline);
        Assert.AreEqual(BarlineKind.Final, st.Measures[1].EndBarline);
    }

    [TestMethod]
    public void RepeatVolta_BracketsTheBarsAndClosesTheRepeat()
    {
        var st = P("\\relative c' { \\time 4/4 \\repeat volta 2 { c4 d e f | g4 a b c } }").Staves[0];
        Assert.AreEqual(2, st.Measures.Count);
        Assert.AreEqual(BarlineKind.RepeatStart, st.Measures[0].StartBarline);
        Assert.AreEqual(BarlineKind.RepeatEnd, st.Measures[1].EndBarline);
    }

    [TestMethod]
    public void Alternative_NumbersTheBracketsAndEndsTheRepeatOnTheFirst()
    {
        var st = P("\\relative c' { \\time 4/4 \\repeat volta 2 { c4 d e f } " +
                   "\\alternative { { g1 } { a1 } } }").Staves[0];
        Assert.AreEqual(3, st.Measures.Count);
        Assert.AreEqual(BarlineKind.RepeatStart, st.Measures[0].StartBarline);
        Assert.AreEqual("1", st.Measures[1].Volta);
        Assert.AreEqual(BarlineKind.RepeatEnd, st.Measures[1].EndBarline, "the repeat ends at the end of volta 1");
        Assert.AreEqual("2", st.Measures[2].Volta);
    }

    [TestMethod]
    public void RepeatUnfold_IsWrittenOut()
    {
        var st = P("\\relative c' { \\time 4/4 \\repeat unfold 3 { c4 d e f } }").Staves[0];
        Assert.AreEqual(3, st.Measures.Count);
        Assert.AreEqual(BarlineKind.Single, st.Measures[0].StartBarline, "an unfolded repeat draws no repeat bar");
    }

    // ── Beams come from the meter ───────────────────────────────────────────

    [TestMethod]
    public void Eighths_BeamInFoursInCommonTime()
    {
        var e = Events(P("{ \\time 4/4 c8 d e f g a b c }").Staves[0]);
        Assert.AreNotEqual(0, e[0].BeamId);
        Assert.AreEqual(e[0].BeamId, e[3].BeamId, "the first four are one beam");
        Assert.AreNotEqual(e[0].BeamId, e[4].BeamId, "the second half-bar is a beam of its own");
        Assert.AreEqual(e[4].BeamId, e[7].BeamId);
    }

    [TestMethod]
    public void Eighths_BeamInThreesInCompoundTime()
    {
        var e = Events(P("{ \\time 6/8 c8 d e f g a }").Staves[0]);
        Assert.AreEqual(e[0].BeamId, e[2].BeamId, "6/8 groups by the dotted quarter");
        Assert.AreNotEqual(e[0].BeamId, e[3].BeamId);
        Assert.AreEqual(e[3].BeamId, e[5].BeamId);
    }

    [TestMethod]
    public void Eighths_BeamInPairsInThreeFour()
    {
        var e = Events(P("{ \\time 3/4 c8 d e f g a }").Staves[0]);
        Assert.AreEqual(e[0].BeamId, e[1].BeamId);
        Assert.AreNotEqual(e[1].BeamId, e[2].BeamId, "each quarter-note beat gets its own pair");
    }

    [TestMethod]
    public void Sixteenths_BeamByTheBeat()
    {
        var e = Events(P("{ \\time 4/4 c16 d e f g a b c d e f g a b c d }").Staves[0]);
        Assert.AreEqual(e[0].BeamId, e[3].BeamId);
        Assert.AreNotEqual(e[3].BeamId, e[4].BeamId, "sixteenths group by the beat, not the half-bar");
    }

    [TestMethod]
    public void ARest_BreaksTheBeam()
    {
        var e = Events(P("{ \\time 4/4 c8 d r8 e f g a b }").Staves[0]);
        Assert.AreEqual(0, e[2].BeamId, "the rest is not beamed");
        Assert.AreNotEqual(e[0].BeamId, e[3].BeamId, "…and it separates what is either side of it");
    }

    [TestMethod]
    public void ManualBeam_OverridesTheMeter()
    {
        var e = Events(P("{ \\time 4/4 c8[ d e] f g a b c }").Staves[0]);
        Assert.AreEqual(e[0].BeamId, e[2].BeamId, "the bracket beams three, where the meter would beam four");
        Assert.AreNotEqual(e[0].BeamId, e[3].BeamId);
    }

    [TestMethod]
    public void TwoManualBeams_BackToBack_KeepEveryNote()
    {
        // The '[' arrives *after* the note it starts on, so a second bracket opening straight after a ']' closed
        // has to reach back one note. Getting it wrong drops a note out of the bar.
        var e = Events(P("{ \\time 4/4 c8[ d e] f[ g a b c] }").Staves[0]);
        Assert.AreEqual(8, e.Count);
        Assert.AreEqual(e[0].BeamId, e[2].BeamId, "the first bracket beams three");
        Assert.AreNotEqual(e[2].BeamId, e[3].BeamId);
        Assert.AreEqual(e[3].BeamId, e[7].BeamId, "…and the second beams the remaining five");
    }

    [TestMethod]
    public void ABeamNeverCrossesABarLine()
    {
        var e = Events(P("{ \\time 2/4 c8 d e f g a b c }").Staves[0]);
        var byMeasure = P("{ \\time 2/4 c8 d e f g a b c }").Staves[0].Measures;
        Assert.AreEqual(2, byMeasure.Count);
        Assert.AreNotEqual(e[3].BeamId, e[4].BeamId, "the beam stops at the bar line");
    }

    // ── Accidentals are an engraving decision ───────────────────────────────

    [TestMethod]
    public void AKeySignatureNote_PrintsNoAccidental()
    {
        // \key g \major puts F sharp in the signature, so 'fis' prints as a plain note head.
        var n = Notes(P("\\relative c' { \\key g \\major fis4 g a b }").Staves[0]);
        Assert.AreEqual(1, n[0].Pitch.Alter, "it is still an F sharp…");
        Assert.AreEqual(AccidentalKind.None, n[0].Accidental, "…but the signature already said so");
    }

    [TestMethod]
    public void ANoteAgainstTheKey_PrintsAnAccidental()
    {
        var n = Notes(P("\\relative c' { \\key g \\major f4 g a b }").Staves[0]);
        Assert.AreEqual(0, n[0].Pitch.Alter);
        Assert.AreEqual(AccidentalKind.Natural, n[0].Accidental, "F natural in G major needs cancelling");
    }

    [TestMethod]
    public void AnAccidental_IsPrintedOnceAndHoldsForTheBar()
    {
        // The source must spell every one of these 'fis'; only the first is *printed*.
        var st = P("\\relative c' { \\time 4/4 \\key c \\major fis4 fis fis fis | fis4 g a b }").Staves[0];
        var bar1 = st.Measures[0].Events.Cast<Note>().ToList();
        Assert.AreEqual(AccidentalKind.Sharp, bar1[0].Accidental);
        Assert.AreEqual(AccidentalKind.None, bar1[1].Accidental, "still in force");
        Assert.AreEqual(AccidentalKind.None, bar1[3].Accidental);

        var bar2 = st.Measures[1].Events.Cast<Note>().ToList();
        Assert.AreEqual(AccidentalKind.Sharp, bar2[0].Accidental, "a new bar cancels it — print it again");
    }

    [TestMethod]
    public void AnAccidental_HoldsOnlyForItsOwnOctave()
    {
        var st = P("\\relative c' { \\time 4/4 \\key c \\major fis4 fis' fis, fis }").Staves[0];
        var n = st.Measures[0].Events.Cast<Note>().ToList();
        Assert.AreEqual(AccidentalKind.Sharp, n[0].Accidental);
        Assert.AreEqual(AccidentalKind.Sharp, n[1].Accidental, "the octave above is a different pitch");
        Assert.AreEqual(AccidentalKind.None, n[2].Accidental, "…and back down is the one already sharpened");
    }

    // ── Keys and modes ──────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("c", "major", 0)]
    [DataRow("g", "major", 1)]
    [DataRow("f", "major", -1)]
    [DataRow("fis", "major", 6)]
    [DataRow("bes", "major", -2)]
    [DataRow("a", "minor", 0)]
    [DataRow("d", "dorian", 0)]
    [DataRow("g", "mixolydian", 0)]
    [DataRow("f", "lydian", 0)]
    [DataRow("e", "phrygian", 0)]
    [DataRow("b", "locrian", 0)]
    [DataRow("a", "aeolian", 0)]
    public void Key_MapsTonicAndModeToTheCircleOfFifths(string tonic, string mode, int fifths)
    {
        Assert.AreEqual(fifths, P($"{{ \\key {tonic} \\{mode} c4 }}").Staves[0].Key.Fifths);
    }

    [TestMethod]
    public void MidTuneKeyChange_RidesOnItsMeasure()
    {
        var st = P("\\relative c' { \\time 4/4 \\key c \\major c4 d e f \\key g \\major g4 a b c }").Staves[0];
        Assert.AreEqual(0, st.Key.Fifths, "the staff opens in C");
        Assert.AreEqual(1, st.Measures[1].KeyChange?.Fifths, "…and changes at bar 2");
    }

    // ── Meter ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void CommonAndCutTime_PrintAsSymbols_UnlessTheSourceAsksForFigures()
    {
        Assert.AreEqual(TimeSymbol.Common, P("{ \\time 4/4 c1 }").Staves[0].Time.Symbol);
        Assert.AreEqual(TimeSymbol.Cut, P("{ \\time 2/2 c1 }").Staves[0].Time.Symbol);
        Assert.AreEqual(TimeSymbol.Numeric, P("{ \\time 3/4 c2. }").Staves[0].Time.Symbol);
        Assert.AreEqual(TimeSymbol.Numeric, P("{ \\time 4/4 \\numericTimeSignature c1 }").Staves[0].Time.Symbol,
            "\\numericTimeSignature is written after \\time, so it has to reach back");
        Assert.AreEqual(TimeSymbol.Common, P("{ c1 }").Staves[0].Time.Symbol, "LilyPond's default meter is 4/4 as C");
    }

    // ── Ties, slurs, articulations ──────────────────────────────────────────

    [TestMethod]
    public void Tie_MarksTheNoteItStartsOn()
    {
        var n = Notes(P("\\relative c' { \\time 4/4 c4~ c2. }").Staves[0]);
        Assert.IsTrue(n[0].TieStart);
        Assert.IsFalse(n[1].TieStart);
    }

    [TestMethod]
    public void Tie_OnAChord_TiesEveryNoteInIt()
    {
        var st = P("\\relative c' { \\time 4/4 <c e g>4~ <c e g>2. }").Staves[0];
        var chord = (Chord)st.Measures[0].Events[0];
        Assert.IsTrue(chord.Notes.All(n => n.TieStart));
    }

    [TestMethod]
    public void Slur_OpensOnTheNoteItFollows()
    {
        // Unlike ABC, LilyPond writes '(' *after* the note the slur starts on.
        var n = Notes(P("\\relative c' { \\time 4/4 c4( d e f) }").Staves[0]);
        Assert.AreEqual(1, n[0].SlurOpen);
        Assert.AreEqual(0, n[1].SlurOpen);
        Assert.AreEqual(1, n[3].SlurClose);
    }

    [TestMethod]
    public void Slurs_Nest()
    {
        var n = Notes(P("\\relative c' { \\time 4/4 c4( d\\( e\\) f) }").Staves[0]);
        Assert.AreEqual(1, n[0].SlurOpen, "the ordinary slur");
        Assert.AreEqual(1, n[1].SlurOpen, "the phrasing slur bows over the same notes");
        Assert.AreEqual(1, n[2].SlurClose);
        Assert.AreEqual(1, n[3].SlurClose);
    }

    [TestMethod]
    public void Articulations_Shorthand()
    {
        var e = Events(P("\\relative c' { \\time 4/4 c4-. d-> e-- f-^ }").Staves[0]);
        Assert.AreEqual(ArticulationKind.Staccato, e[0].Articulations[0]);
        Assert.AreEqual(ArticulationKind.Accent, e[1].Articulations[0]);
        Assert.AreEqual(ArticulationKind.Tenuto, e[2].Articulations[0]);
        Assert.AreEqual(ArticulationKind.Marcato, e[3].Articulations[0]);
    }

    [TestMethod]
    public void Articulations_ByName()
    {
        var e = Events(P("\\relative c' { \\time 4/4 c4\\staccato d\\fermata e\\trill f\\upbow }").Staves[0]);
        Assert.AreEqual(ArticulationKind.Staccato, e[0].Articulations[0]);
        Assert.AreEqual(ArticulationKind.Fermata, e[1].Articulations[0]);
        Assert.AreEqual(ArticulationKind.Trill, e[2].Articulations[0]);
        Assert.AreEqual(ArticulationKind.UpBow, e[3].Articulations[0]);
    }

    // ── Tuplets ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Tuplet_ThreeInTheTimeOfTwo()
    {
        var st = P("\\relative c' { \\time 4/4 \\tuplet 3/2 { c8 d e } \\tuplet 3/2 { f8 g a } c4 c4 }").Staves[0];
        var e = st.Measures[0].Events;
        Assert.AreEqual(3, e[0].TupletNumber);
        Assert.AreEqual(2, e[0].TupletTime);
        Assert.AreEqual(e[0].TupletId, e[2].TupletId, "the three share one bracket");
        Assert.AreNotEqual(e[0].TupletId, e[3].TupletId, "…and the next triplet is a bracket of its own");

        // Six triplet eighths plus two quarters is exactly one 4/4 bar — the proof the *time* is scaled.
        Assert.AreEqual(1, st.Measures.Count);
        Assert.AreEqual(8, e.Count);
    }

    [TestMethod]
    public void Times_SaysTheSameThingTheOtherWayRound()
    {
        var e = Events(P("\\relative c' { \\time 4/4 \\times 2/3 { c8 d e } c4 c4 c4 }").Staves[0]);
        Assert.AreEqual(3, e[0].TupletNumber, "\\times 2/3 is three in the time of two");
        Assert.AreEqual(2, e[0].TupletTime);
    }

    [TestMethod]
    public void ATuplet_BeamsAsOneGroup()
    {
        var e = Events(P("\\relative c' { \\time 4/4 \\tuplet 3/2 { c8 d e } c4 c4 c4 }").Staves[0]);
        Assert.AreEqual(e[0].BeamId, e[2].BeamId);
        Assert.AreNotEqual(0, e[0].BeamId);
    }

    // ── Chords ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Chord_KeepsEveryNote_LowestFirst()
    {
        var st = P("\\relative c' { \\time 4/4 <c e g>2 <g c e>2 }").Staves[0];
        var first = (Chord)st.Measures[0].Events[0];
        Assert.AreEqual(3, first.Notes.Count, "a chord is not just its lowest note");
        CollectionAssert.AreEqual(new[] { 'C', 'E', 'G' }, first.Notes.Select(n => n.Pitch.Letter).ToArray());
        Assert.AreEqual(2.0, first.Duration.QuarterLength, 1e-9, "the duration is written after the '>'");

        var second = (Chord)st.Measures[0].Events[1];
        CollectionAssert.AreEqual(new[] { 'G', 'C', 'E' }, second.Notes.Select(n => n.Pitch.Letter).ToArray());
    }

    [TestMethod]
    public void AfterAChord_RelativeTracksItsFirstNote()
    {
        // <c e g> then g: the g is measured from the chord's c, not from its g.
        var st = P("\\relative c' { \\time 4/4 <c e g>2 g2 }").Staves[0];
        var g = (Note)st.Measures[0].Events[1];
        Assert.AreEqual("G3", g.Pitch.ToString());
    }

    // ── Grace notes ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Grace_HangsOffTheNoteThatFollowsIt()
    {
        var st = P("\\relative c' { \\time 4/4 \\grace d8 c4 d e f }").Staves[0];
        Assert.AreEqual(4, st.Measures[0].Events.Count, "a grace note takes no time of its own");
        Assert.AreEqual(1, st.Measures[0].Events[0].Graces.Count);
        Assert.AreEqual('D', st.Measures[0].Events[0].Graces[0].Pitch.Letter);
    }

    [TestMethod]
    public void GraceGroup_AndAcciaccatura()
    {
        var st = P("\\relative c' { \\time 4/4 \\grace { d16 e } c4 \\acciaccatura d8 e4 f4 g4 }").Staves[0];
        Assert.AreEqual(2, st.Measures[0].Events[0].Graces.Count);
        Assert.IsFalse(st.Measures[0].Events[0].GraceSlashed, "\\grace draws no slash");
        Assert.AreEqual(1, st.Measures[0].Events[1].Graces.Count);
        Assert.IsTrue(st.Measures[0].Events[1].GraceSlashed, "\\acciaccatura does");
    }

    // ── Text ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Markup_AboveAndBelow()
    {
        var e = Events(P("\\relative c' { \\time 4/4 c4^\"Fine\" d_\"dolce\" e4 f4 }").Staves[0]);
        Assert.AreEqual("Fine", e[0].Annotation);
        Assert.AreEqual(AnnotationPlacement.Above, e[0].AnnotationPlacement);
        Assert.AreEqual("dolce", e[1].Annotation);
        Assert.AreEqual(AnnotationPlacement.Below, e[1].AnnotationPlacement);
    }

    [TestMethod]
    public void ChordNames_SitAboveTheNoteTheyStartOn()
    {
        var st = P("""
            <<
              \new ChordNames \chordmode { c1 | g1:7 }
              \new Staff \relative c' { \time 4/4 c4 e g c | b4 d g b }
            >>
            """).Staves[0];

        Assert.AreEqual("C", st.Measures[0].Events[0].ChordSymbol);
        Assert.IsNull(st.Measures[0].Events[1].ChordSymbol, "one symbol per chord, not one per note");
        Assert.AreEqual("G7", st.Measures[1].Events[0].ChordSymbol);
    }

    [TestMethod]
    public void ChordNames_SpellTheQualityTheLeadSheetWay()
    {
        var st = P("""
            <<
              \new ChordNames \chordmode { d2:m7 bes2:maj g1:m }
              \new Staff \relative c' { \time 4/4 c2 d2 | e1 }
            >>
            """).Staves[0];

        Assert.AreEqual("Dm7", st.Measures[0].Events[0].ChordSymbol);
        Assert.AreEqual("Bbmaj7", st.Measures[0].Events[1].ChordSymbol);
        Assert.AreEqual("Gm", st.Measures[1].Events[0].ChordSymbol);
    }

    // ── Lyrics ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddLyrics_LinesSyllablesUpUnderTheNotes()
    {
        var score = P("""
            \relative c' { \time 4/4 c4 d e f | g4 a b c }
            \addlyrics { One two three four five six sev -- en }
            """);

        var e = Events(score.Staves[0]);
        Assert.AreEqual("One", e[0].Lyrics[0].Text);
        Assert.AreEqual("four", e[3].Lyrics[0].Text);
        Assert.AreEqual("sev", e[6].Lyrics[0].Text);
        Assert.IsTrue(e[6].Lyrics[0].Hyphen, "-- prints a hyphen and keeps the word together");
        Assert.AreEqual("en", e[7].Lyrics[0].Text);
    }

    [TestMethod]
    public void Lyrics_SkipRests_AndTakeExtendersAndBlanks()
    {
        var score = P("""
            \relative c' { \time 4/4 c4 r4 d4 e4 | f4 g4 a4 b4 }
            \addlyrics { one two __ _ four five six }
            """);

        var sung = Events(score.Staves[0]).Where(x => x is not Rest).ToList();
        Assert.AreEqual("one", sung[0].Lyrics[0].Text);
        Assert.AreEqual("two", sung[1].Lyrics[0].Text, "the rest takes no syllable");
        Assert.IsTrue(sung[2].Lyrics[0].Melisma, "__ holds the word over the next note");
        Assert.AreEqual("", sung[3].Lyrics[0].Text, "_ is a note with no syllable");
        Assert.AreEqual("four", sung[4].Lyrics[0].Text);
    }

    /// <summary>A syllable may carry a duration ("Ly4 -- rics4"), which is not printed — but a full stop is
    /// punctuation, and a sung line that ends a sentence has to keep it.</summary>
    [TestMethod]
    public void ASyllablesDuration_IsStripped_ButItsPunctuationIsNot()
    {
        var score = P("""
            \relative c' { \time 4/4 c4 d e f }
            \addlyrics { Ly4 -- rics4. in the sky. }
            """);

        var e = Events(score.Staves[0]);
        Assert.AreEqual("Ly", e[0].Lyrics[0].Text, "the 4 is a duration");
        Assert.AreEqual("rics", e[1].Lyrics[0].Text, "…and so is the 4.");
        Assert.AreEqual("in", e[2].Lyrics[0].Text);
        Assert.AreEqual("the", e[3].Lyrics[0].Text);

        var ended = P("""
            \relative c' { \time 4/4 c4 d e f }
            \addlyrics { up in the sky. }
            """);
        Assert.AreEqual("sky.", Events(ended.Staves[0])[3].Lyrics[0].Text, "a bare '.' is a full stop, not a dotted note");
    }

    [TestMethod]
    public void Lyrics_StackIntoVerses()
    {
        var score = P("""
            \relative c' { \time 4/4 c4 d e f }
            \addlyrics { one two three four }
            \addlyrics { ein zwei drei vier }
            """);

        Assert.AreEqual(2, score.LyricVerses);
        var e = Events(score.Staves[0]);
        Assert.AreEqual("one", e[0].Lyrics[0].Text);
        Assert.AreEqual("ein", e[0].Lyrics[1].Text);
    }

    [TestMethod]
    public void LyricsTo_FindsItsVoiceByName()
    {
        var score = P("""
            <<
              \new Staff \new Voice = "melody" \relative c' { \time 4/4 c4 d e f }
              \new Lyrics \lyricsto "melody" { one two three four }
            >>
            """);

        Assert.AreEqual("three", Events(score.Staves[0])[2].Lyrics[0].Text);
    }

    // ── Several staves ──────────────────────────────────────────────────────

    [TestMethod]
    public void EachNewStaff_IsAStaff_AndKeepsItsOwnClefAndName()
    {
        var score = P("""
            \score {
              \new ChoirStaff <<
                \new Staff \with { instrumentName = "Soprano" }
                  \relative c'' { \time 4/4 \key g \major g4 a b c | d1 }
                \new Staff \with { instrumentName = "Bass" }
                  { \clef bass \time 4/4 \key g \major g,4 fis, e, d, | g,1 }
              >>
            }
            """);

        Assert.AreEqual(2, score.Staves.Count);
        Assert.AreEqual("Soprano", score.Staves[0].Name);
        Assert.AreEqual(ClefKind.Treble, score.Staves[0].Clef);
        Assert.AreEqual("Bass", score.Staves[1].Name);
        Assert.AreEqual(ClefKind.Bass, score.Staves[1].Clef);
        Assert.AreEqual(1, score.Staves[1].Key.Fifths);
        Assert.AreEqual(2, score.Staves[0].Measures.Count);
        Assert.AreEqual(2, score.Staves[1].Measures.Count, "the two run in step — the engraver will bracket them");
    }

    [TestMethod]
    public void Polyphony_WithinOneStaff_IsReported_NotSilentlyMangled()
    {
        var score = P("\\new Staff { \\time 4/4 << { c'4 d' e' f' } \\\\ { a4 b c' d' } >> }");
        Assert.AreEqual(1, score.Staves.Count);
        Assert.AreEqual(4, Events(score.Staves[0]).Count, "one strand engraved…");
        Assert.IsTrue(score.Warnings.Any(w => w.Contains("olyphony")), "…and the reader is told so");
    }

    // ── Header ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Header_MapsOntoWhereLilyPondPrintsEachField()
    {
        var score = P("""
            \header {
              title = "Speed the Plough"
              subtitle = "a reel"
              composer = "Trad."
              opus = "Op. 1"
              poet = "Reel"
              source = "Playford"
            }
            \relative c' { c4 d e f }
            """);

        Assert.AreEqual("Speed the Plough", score.Title);
        CollectionAssert.Contains(score.Subtitles, "a reel");
        Assert.AreEqual("Trad.", score.Composer);
        Assert.AreEqual("Op. 1", score.Origin);
        Assert.AreEqual("Reel", score.Rhythm);
        Assert.AreEqual("Playford", score.Source);
    }

    [TestMethod]
    public void SectionLabel_HeadsTheMeasureItOpens()
    {
        var st = P("\\relative c' { \\time 4/4 c4 d e f \\sectionLabel \"Chorus\" g4 a b c }").Staves[0];
        Assert.AreEqual("Chorus", st.Measures[1].SectionLabel);
        Assert.IsTrue(st.Measures[0].SystemBreak, "a heading needs a line to sit on");
    }

    [TestMethod]
    public void Break_AsksForASystemBreak()
    {
        var st = P("\\relative c' { \\time 4/4 c4 d e f \\break g4 a b c }").Staves[0];
        Assert.IsTrue(st.Measures[0].SystemBreak);
    }

    // ── Tolerance ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Scheme_Dynamics_AndOtherUnengravables_AreSkipped_NotFatal()
    {
        var score = P("""
            \version "2.24.0"
            #(set-global-staff-size 20)
            \relative c' {
              \time 4/4
              \set Staff.instrumentName = "Flute"
              c4\f d\< e f\!
              \override NoteHead.color = #red
              g1\fermata
            }
            """);

        Assert.AreEqual(1, score.Staves.Count);
        Assert.AreEqual("Flute", score.Staves[0].Name);
        Assert.AreEqual(2, score.Staves[0].Measures.Count);
        Assert.AreEqual(4, score.Staves[0].Measures[0].Events.Count, "the dynamics don't eat the notes");
        Assert.IsTrue(score.Warnings.Any(w => w.Contains("ynamics")));
    }

    [TestMethod]
    public void Comments_AreNotMusic()
    {
        var score = P("""
            % a line comment with c4 d4 in it
            \relative c' {
              \time 4/4
              c4 d %{ e f %} e f
            }
            """);
        Assert.AreEqual(4, Events(score.Staves[0]).Count);
    }

    [TestMethod]
    public void CadenzaOn_IsFreeMeter_LikeAbcsMNone()
    {
        var st = P("{ \\cadenzaOn \\autoBeamOff c\\breve c1 c2 c4 c8 c16 \\bar \"|.\" }").Staves[0];
        Assert.IsFalse(st.ShowTime, "free meter prints no signature");
        Assert.AreEqual(1, st.Measures.Count, "…and nothing but a \\bar closes a measure");
        Assert.AreEqual(6, st.Measures[0].Events.Count);
    }

    // ── Parity with the other dialect ───────────────────────────────────────

    /// <summary>
    /// The point of the whole exercise, asserted directly: one tune, written once in each notation, has to arrive at
    /// the same score — same pitches, same durations, the same sixteen bars, the same two repeats — because one
    /// engraver draws both. Any drift between the parsers (a mis-scaled duration, an octave off, a bar closed in the
    /// wrong place) shows up here as a disagreement, and nowhere else does it show up at all.
    ///
    /// It is also what keeps the sample document honest. Both halves of this tune are what
    /// <c>music-abc.md</c> and <c>music-lilypond.md</c> print side by side, so a transcription error in either doc
    /// fails here rather than shipping as a wrong note in the manual.
    /// </summary>
    [TestMethod]
    public void TheSameTune_WrittenInBothDialects_LandsOnTheSameScore()
    {
        var abc = new AbcParser().Parse("""
            X:1
            T:Speed the Plough
            M:4/4
            L:1/8
            K:G
            |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
              GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|
            |:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|
              g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|
            """);

        var ly = new LilyPondParser().Parse("""
            \header { title = "Speed the Plough" }
            \relative c'' {
              \numericTimeSignature \time 4/4 \key g \major
              \repeat volta 2 {
                g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | c4 a a b8 a |
                g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | a4 fis g2
              }
              \repeat volta 2 {
                g'4 g8 fis g d b d | g4 fis e d | c4 e8 c b4 d8 b | c4 a a d8 fis |
                g4 g8 fis g4 b,8 d | g4 fis e d | c4 e8 c b4 d8 b | a4 fis g2
              }
            }
            """);

        Assert.AreEqual(abc.Title, ly.Title);
        Assert.AreEqual(abc.Staves[0].Key.Fifths, ly.Staves[0].Key.Fifths, "one sharp");
        Assert.AreEqual(16, abc.Staves[0].Measures.Count);
        Assert.AreEqual(abc.Staves[0].Measures.Count, ly.Staves[0].Measures.Count, "sixteen bars either way");

        var a = Notes(abc.Staves[0]);
        var l = Notes(ly.Staves[0]);
        Assert.AreEqual(a.Count, l.Count, "the same notes");

        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].Pitch.ToString(), l[i].Pitch.ToString(), $"note {i + 1}: pitch");
            Assert.AreEqual(a[i].Duration.QuarterLength, l[i].Duration.QuarterLength, 1e-9, $"note {i + 1}: duration");
        }

        for (int m = 0; m < abc.Staves[0].Measures.Count; m++)
        {
            Assert.AreEqual(abc.Staves[0].Measures[m].StartBarline, ly.Staves[0].Measures[m].StartBarline,
                $"bar {m + 1}: opening bar line");
            Assert.AreEqual(abc.Staves[0].Measures[m].EndBarline, ly.Staves[0].Measures[m].EndBarline,
                $"bar {m + 1}: closing bar line");
        }

        Assert.AreEqual(BarlineKind.RepeatStart, ly.Staves[0].Measures[0].StartBarline, "…and both halves repeat");
        Assert.AreEqual(BarlineKind.RepeatEnd, ly.Staves[0].Measures[7].EndBarline);
        Assert.AreEqual(BarlineKind.RepeatStart, ly.Staves[0].Measures[8].StartBarline);
        Assert.AreEqual(BarlineKind.RepeatEnd, ly.Staves[0].Measures[15].EndBarline);
    }
}
