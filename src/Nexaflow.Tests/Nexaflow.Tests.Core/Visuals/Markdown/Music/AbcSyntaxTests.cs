using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Music;

/// <summary>
/// The rest of the ABC language, past the note-and-bar-line core that <see cref="AbcParserTests"/> covers:
/// note lengths out to the breve, broken rhythm, tuplets, ties and slurs, chord symbols, decorations, grace
/// notes, chords, repeat brackets, the mode names, mid-tune signature changes, lyrics and the header fields
/// that print around the score.
/// </summary>
[TestClass]
[CoversNode("abc-notation")]
public class AbcSyntaxTests
{
    /// <summary>Parses a one-line tune body under the given header, returning its only staff.</summary>
    private static Staff Staff(string header, string body) =>
        new AbcParser().Parse($"X:1\n{header}\nK:C\n{body}\n").Staves[0];

    private static Note[] Notes(string header, string body) =>
        Staff(header, body).Measures.SelectMany(m => m.Events).OfType<Note>().ToArray();

    // ── Note lengths ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    public void NoteLengths_ScaleFromTheUnitLength_UpToTheBreve()
    {
        // M:C with no L: → the unit note length is an eighth, so A16 is two whole notes: a breve.
        var n = Notes("M:C", "A/4 A/2 A/ A A2 A3 A4 A6 A7 A8 A12 A16 |]");
        Assert.AreEqual(12, n.Length);

        Assert.AreEqual(32, n[0].Duration.Base, "A/4 = a thirty-second");
        Assert.AreEqual(16, n[1].Duration.Base, "A/2 = a sixteenth");
        Assert.AreEqual(16, n[2].Duration.Base, "a bare slash halves once");
        Assert.AreEqual(8, n[3].Duration.Base, "A = the unit length");
        Assert.AreEqual(4, n[4].Duration.Base, "A2 = a quarter");

        Assert.AreEqual(4, n[5].Duration.Base);
        Assert.AreEqual(1, n[5].Duration.Dots, "A3 = a dotted quarter");
        Assert.AreEqual(2, n[7].Duration.Base);
        Assert.AreEqual(1, n[7].Duration.Dots, "A6 = a dotted half");
        Assert.AreEqual(2, n[8].Duration.Dots, "A7 = a double-dotted half");

        Assert.AreEqual(1, n[9].Duration.Base, "A8 = a whole note");
        Assert.AreEqual(1, n[10].Duration.Dots, "A12 = a dotted whole");
        Assert.IsTrue(n[11].Duration.IsBreve, "A16 = a breve (double whole), not a dotted anything");
        Assert.AreEqual(8.0, n[11].Duration.QuarterLength, 0.001);
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void UnitNoteLength_Redefines_WhatEveryMultiplierMeans()
    {
        // The same written note under three L: values — all three lines say "a whole note last".
        var st = Staff("M:C", "L:1/16\nA A16 |]\nL:1/8\nA A8 |]\nL:1/4\nA A4 |]");
        var whole = st.Measures.SelectMany(m => m.Events).OfType<Note>().Where(x => x.Duration.Base == 1).ToArray();
        Assert.AreEqual(3, whole.Length, "each line ends on a whole note however it is written");
    }

    // ── Broken rhythm ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void BrokenRhythm_LengthensOneSideAndShortensTheOther()
    {
        var n = Notes("M:3/4", "A>A A<A |]");

        Assert.AreEqual(8, n[0].Duration.Base);
        Assert.AreEqual(1, n[0].Duration.Dots, "A>A: the first note gains a dot");
        Assert.AreEqual(16, n[1].Duration.Base, "…and the second halves to a sixteenth");

        Assert.AreEqual(16, n[2].Duration.Base, "A<A is the mirror image");
        Assert.AreEqual(8, n[3].Duration.Base);
        Assert.AreEqual(1, n[3].Duration.Dots);

        // The pair still adds up to what it displaced.
        Assert.AreEqual(1.0, n[0].Duration.QuarterLength + n[1].Duration.QuarterLength, 0.001);
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void BrokenRhythm_Doubled_TakesTwoDotsAndQuartersTheOther()
    {
        var n = Notes("M:3/4", "A>>A |]");
        Assert.AreEqual(2, n[0].Duration.Dots);
        Assert.AreEqual(32, n[1].Duration.Base);
        Assert.AreEqual(1.0, n[0].Duration.QuarterLength + n[1].Duration.QuarterLength, 0.001);
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void BrokenRhythm_KeepsTheBeam_WhenBothHalvesStillCarryFlags()
    {
        var n = Notes("M:3/4", "A>A |]");
        Assert.AreNotEqual(0, n[0].BeamId);
        Assert.AreEqual(n[0].BeamId, n[1].BeamId, "a dotted eighth and its sixteenth stay beamed");
    }

    // ── Tuplets ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Tuplet_MarksItsMembers_WithTheRatioItPlaysAt()
    {
        var n = Notes("M:C", "(3ABA A |]");
        Assert.IsTrue(n.Take(3).All(x => x.TupletId != 0 && x.TupletNumber == 3 && x.TupletTime == 2),
            "(3 = three notes in the time of two");
        Assert.AreEqual(0, n[3].TupletId, "the tuplet covers exactly three notes");
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Tuplet_OddNumbers_ReadTheMeter_ForHowLongTheyLast()
    {
        // (5 is five-in-the-time-of-two in a simple meter, five-in-the-time-of-three in a compound one.
        Assert.AreEqual(2, Notes("M:C", "(5ABABA |]")[0].TupletTime);
        Assert.AreEqual(3, Notes("M:6/8", "(5ABABA |]")[0].TupletTime);
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Tuplet_ExplicitRatio_Wins()
    {
        var n = Notes("M:C", "(3:2:2AB A |]");
        Assert.AreEqual(3, n[0].TupletNumber);
        Assert.AreEqual(2, n[0].TupletTime);
        Assert.AreEqual(0, n[2].TupletId, "…for exactly two notes, as the third number says");
    }

    // ── Ties and slurs ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Tie_MarksTheNoteItStartsOn_AcrossABarLine()
    {
        var st = Staff("M:C", "A-A | A2-|A4 |]");
        var m0 = st.Measures[0].Events.OfType<Note>().ToArray();
        Assert.IsTrue(m0[0].TieStart);
        Assert.IsFalse(m0[1].TieStart, "the tie ends here, it doesn't start another");
        Assert.IsTrue(st.Measures[1].Events.OfType<Note>().Last().TieStart, "a tie may reach over the bar line");
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Slurs_Nest()
    {
        var n = Notes("M:C", "(A(A)A) |]");
        Assert.AreEqual(1, n[0].SlurOpen, "the outer slur opens on the first note");
        Assert.AreEqual(1, n[1].SlurOpen, "the inner one opens on the second");
        Assert.AreEqual(1, n[1].SlurClose, "…and closes on it too");
        Assert.AreEqual(1, n[2].SlurClose, "the outer slur closes last");
    }

    // ── Text on the score ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void ChordSymbols_AndPlacedAnnotations_AreDifferentThings()
    {
        var n = Notes("M:C", "\"Gm7\"D \"^Fine\"A \"_below\"A |]");
        Assert.AreEqual("Gm7", n[0].ChordSymbol);
        Assert.IsNull(n[0].Annotation, "a bare quoted string is a chord symbol, not free text");

        Assert.AreEqual("Fine", n[1].Annotation);
        Assert.AreEqual(AnnotationPlacement.Above, n[1].AnnotationPlacement);
        Assert.AreEqual(AnnotationPlacement.Below, n[2].AnnotationPlacement);
    }

    // ── Decorations ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Decorations_ShorthandAndBangForm_BothLand()
    {
        var n = Notes("M:C", "~A .A vA uA !fermata!A !trill!A |]");
        Assert.AreEqual(ArticulationKind.Roll, n[0].Articulations.Single());
        Assert.AreEqual(ArticulationKind.Staccato, n[1].Articulations.Single());
        Assert.AreEqual(ArticulationKind.DownBow, n[2].Articulations.Single());
        Assert.AreEqual(ArticulationKind.UpBow, n[3].Articulations.Single());
        Assert.AreEqual(ArticulationKind.Fermata, n[4].Articulations.Single());
        Assert.AreEqual(ArticulationKind.Trill, n[5].Articulations.Single());
    }

    // ── Grace notes ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void GraceNotes_AttachToTheNoteTheyPrecede()
    {
        var n = Notes("M:6/8", "{g}A3 {gAGAG}A3 |]");
        Assert.AreEqual(1, n[0].Graces.Count);
        Assert.AreEqual('G', n[0].Graces[0].Pitch.Letter);
        Assert.IsTrue(n[0].GraceSlashed, "a lone grace note is an acciaccatura");

        Assert.AreEqual(5, n[1].Graces.Count, "a whole grace run attaches to one note");
        Assert.AreEqual(2, n.Length, "grace notes are not events of their own");
    }

    // ── Chords ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Chord_IsOneEvent_CarryingItsNotes_LowestFirst()
    {
        var st = Staff("M:2/4", "[CEGc] [A4d4] |]");
        var evs = st.Measures[0].Events;
        var first = (Chord)evs[0];
        Assert.AreEqual(4, first.Notes.Count);
        CollectionAssert.AreEqual(new[] { 'C', 'E', 'G', 'C' }, first.Notes.Select(x => x.Pitch.Letter).ToArray());

        // M:2/4 → the unit length is a sixteenth, so [A4d4] is a quarter.
        var second = (Chord)evs[1];
        Assert.AreEqual(4, second.Duration.Base, "the chord takes its members' length");
    }

    // ── Bar lines and repeat brackets ───────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    public void BarLines_EveryForm()
    {
        var st = Staff("M:C", "[| A4 A4 | A4 A4 || A4 A4 |: A4 A4 :: A4 A4 :| A4 A4 |]");
        var m = st.Measures;
        Assert.AreEqual(6, m.Count);
        Assert.AreEqual(BarlineKind.HeavyLight, m[0].StartBarline, "[| opens a section");
        Assert.AreEqual(BarlineKind.Double, m[1].EndBarline, "|| is a thin double bar");
        Assert.AreEqual(BarlineKind.RepeatStart, m[3].StartBarline, "|:");
        Assert.AreEqual(BarlineKind.RepeatEnd, m[3].EndBarline, ":: ends a repeat…");
        Assert.AreEqual(BarlineKind.RepeatStart, m[4].StartBarline, "…and opens the next in the same stroke");
        Assert.AreEqual(BarlineKind.RepeatEnd, m[4].EndBarline, ":|");
        Assert.AreEqual(BarlineKind.Single, m[5].StartBarline, ":| closes a repeat without opening one");
        Assert.AreEqual(BarlineKind.Final, m[5].EndBarline, "|]");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void RepeatBrackets_LabelTheBarTheyOpenOn()
    {
        var st = Staff("M:C", "A4 A4 |1 A4 A4 :|2 A4 A4 |]");
        Assert.IsNull(st.Measures[0].Volta);
        Assert.AreEqual("1", st.Measures[1].Volta, "|1 opens the first-time bar");
        Assert.AreEqual(BarlineKind.RepeatEnd, st.Measures[1].EndBarline);
        Assert.AreEqual("2", st.Measures[2].Volta, ":|2 ends the repeat and opens the second-time bar");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void RepeatBracket_WithoutItsOwnBarLine_StillLands()
    {
        // "|[1" — the bar line and the bracket are written separately.
        var st = Staff("M:C", "A4 A4 |[1 A4 A4 :|");
        Assert.AreEqual("1", st.Measures[1].Volta);
    }

    // ── Keys and modes ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    [DataRow("C", 0)]
    [DataRow("CMAJOR", 0)]
    [DataRow("Cmajor", 0)]
    [DataRow("C maj", 0)]
    [DataRow("C major", 0)]
    [DataRow("C Major", 0)]
    [DataRow("C Lydian", 1)]
    [DataRow("C Ionian", 0)]
    [DataRow("C Mixolydian", -1)]
    [DataRow("C Dorian", -2)]
    [DataRow("C Minor", -3)]
    [DataRow("Cm", -3)]
    [DataRow("C Aeolian", -3)]
    [DataRow("C Phrygian", -4)]
    [DataRow("C Locrian", -5)]
    [DataRow("G", 1)]
    [DataRow("Bb", -2)]
    [DataRow("F#", 6)]
    [DataRow("Dm", -1)]
    public void Key_TonicAndMode_MapToTheCircleOfFifths(string field, int fifths)
    {
        var (key, _) = AbcParser.ParseKeyField(field);
        Assert.AreEqual(fifths, key.Fifths, $"K:{field}");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void Key_CarriesAClef_AndModeSurvivesBesideIt()
    {
        var (key, clef) = AbcParser.ParseKeyField("D Dorian clef=bass");
        Assert.AreEqual(ClefKind.Bass, clef);
        Assert.AreEqual(0, key.Fifths, "D dorian has no accidentals — the clef word must not eat the mode");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void Key_None_IsCMajor_WithNoAccidentals()
    {
        Assert.AreEqual(0, AbcParser.ParseKeyField("none").key.Fifths);
    }

    // ── Mid-tune changes ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    public void MidTuneKeyChange_RidesOnTheBarItTakesEffectAt_LeavingTheHeaderAlone()
    {
        var st = new AbcParser().Parse("X:1\nM:C\nK:C\nCDEF |\nK:G\nCDEF |]\n").Staves[0];
        Assert.AreEqual(0, st.Key.Fifths, "the staff still opens in the key the header gave it");
        Assert.IsNull(st.Measures[0].KeyChange);
        Assert.AreEqual(1, st.Measures[1].KeyChange!.Fifths, "the change lands on the bar it applies to");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void MidTuneMeterChange_IsRecordedOnItsBar()
    {
        var st = new AbcParser().Parse("X:1\nM:9/8\nK:G\nGFG GAG G2D |\nM:12/8\nE2E EFE E2E EFG |]\n").Staves[0];
        Assert.AreEqual(new TimeSignature(9, 8), st.Time);
        Assert.AreEqual(new TimeSignature(12, 8), st.Measures[1].TimeChange);
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void MidTuneTitle_BecomesASectionHeading()
    {
        var st = new AbcParser().Parse("X:1\nM:C\nK:C\nCDEF |\nT:Second strain\nGABc |]\n").Staves[0];
        Assert.AreEqual("Second strain", st.Measures[1].SectionLabel);
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void CommonAndCutTime_KeepTheirSymbols()
    {
        // M:C means the symbol. Engraving it as "4/4" would print something the writer didn't ask for.
        var common = new AbcParser().Parse("X:1\nM:C\nK:C\nCDEF |]\n").Staves[0].Time;
        Assert.AreEqual(TimeSymbol.Common, common.Symbol);
        Assert.AreEqual(4.0, common.QuarterLengthPerMeasure, 0.001, "…while still counting as 4/4");

        var cut = new AbcParser().Parse("X:1\nM:C|\nK:C\nCDEF |]\n").Staves[0].Time;
        Assert.AreEqual(TimeSymbol.Cut, cut.Symbol);

        Assert.AreEqual(TimeSymbol.Numeric, new AbcParser().Parse("X:1\nM:4/4\nK:C\nCDEF |]\n").Staves[0].Time.Symbol,
            "a tune that wrote the figures gets the figures");
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void FreeMeter_PrintsNoTimeSignature()
    {
        Assert.IsFalse(new AbcParser().Parse("X:1\nM:none\nK:C\nCDEF |]\n").Staves[0].ShowTime);
        Assert.IsTrue(new AbcParser().Parse("X:1\nM:C\nK:C\nCDEF |]\n").Staves[0].ShowTime);
    }

    // ── Header fields that print around the score ───────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    public void HeaderFields_LandWhereTheEngraverPrintsThem()
    {
        var score = new AbcParser().Parse(
            "X:1\nT:Dusty Miller\nT:Binny's Jig\nC:Trad.\nO:English\nR:DH\nS:Offord MSS\n" +
            "Z:originally in C\nN:see also Playford\nM:3/4\nK:G\nGABc |]\nW:Hey, the dusty miller\n");

        Assert.AreEqual("Dusty Miller", score.Title);
        CollectionAssert.AreEqual(new[] { "Binny's Jig" }, score.Subtitles, "a second T: is a subtitle");
        Assert.AreEqual("Trad.", score.Composer);
        Assert.AreEqual("English", score.Origin);
        Assert.AreEqual("DH", score.Rhythm);
        Assert.AreEqual("Offord MSS", score.Source);
        Assert.AreEqual("originally in C", score.Transcription);
        CollectionAssert.AreEqual(new[] { "see also Playford" }, score.Notes);
        CollectionAssert.AreEqual(new[] { "Hey, the dusty miller" }, score.Verses);
    }

    [TestMethod]
    [CoversNode("abc-core")]
    public void TrailingComments_AreStripped_FromFieldsAndMusicAlike()
    {
        var score = new AbcParser().Parse("X:1\nT:Dusty Miller % title\nM:3/4  % meter\nK:G % key\nGAB % notes\n");
        Assert.AreEqual("Dusty Miller", score.Title);
        Assert.AreEqual(new TimeSignature(3, 4), score.Staves[0].Time);
        Assert.AreEqual(3, score.Staves[0].Measures[0].Events.Count, "the comment is not three more notes");
    }

    // ── Lyrics ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Lyrics_AlignSyllablesToNotes_WithHyphensSkipsAndHolds()
    {
        var n = Notes("M:4/4\nL:1/4", "A A A A | A A A A |\nw:syl-la-ble * word_ done");
        Assert.AreEqual("syl", n[0].Lyrics[0].Text);
        Assert.IsTrue(n[0].Lyrics[0].Hyphen, "a trailing hyphen means the word carries on");
        Assert.AreEqual("la", n[1].Lyrics[0].Text);
        Assert.AreEqual("ble", n[2].Lyrics[0].Text);
        Assert.IsFalse(n[2].Lyrics[0].Hyphen);

        Assert.AreEqual(0, n[3].Lyrics.Count, "* leaves a note unsung");

        Assert.AreEqual("word", n[4].Lyrics[0].Text);
        Assert.IsTrue(n[5].Lyrics[0].Melisma, "_ holds the previous syllable over this note");
        Assert.AreEqual("done", n[6].Lyrics[0].Text);
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Lyrics_BarSymbol_SkipsToTheNextBar()
    {
        // "|" is a sync point: whatever is left of the bar goes unsung.
        var n = Notes("M:4/4\nL:1/4", "A A A A | A A A A |\nw:one | two");
        Assert.AreEqual("one", n[0].Lyrics[0].Text);
        Assert.AreEqual(0, n[1].Lyrics.Count);
        Assert.AreEqual(0, n[3].Lyrics.Count, "the rest of bar one is skipped");
        Assert.AreEqual("two", n[4].Lyrics[0].Text, "…and the next syllable lands on the downbeat of bar two");
    }

    [TestMethod]
    [CoversNode("abc-decorations")]
    public void Lyrics_StackedWLines_BecomeVerses()
    {
        var n = Notes("M:4/4\nL:1/4", "A A A A |\nw:one two three four\nw:un deux trois quatre");
        Assert.AreEqual("one", n[0].Lyrics[0].Text);
        Assert.AreEqual("un", n[0].Lyrics[1].Text);
        Assert.AreEqual(2, new AbcParser().Parse(
            "X:1\nM:4/4\nL:1/4\nK:C\nA A A A |\nw:one two three four\nw:un deux trois quatre\n").LyricVerses);
    }

    // ── Voices ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-multivoice")]
    public void Voices_BecomeOneStaffEach()
    {
        var score = new AbcParser().Parse(
            "X:1\nM:C\nV: P1 name=\"Soprano\"\nV: P2 name=\"Bass\"\nK:C\n[V: P1] cdef |]\n[V: P2] C,,D,,E,,F,, |]\n");

        Assert.AreEqual(2, score.Staves.Count);
        Assert.AreEqual("Soprano", score.Staves[0].Name);
        Assert.AreEqual("Bass", score.Staves[1].Name);
        Assert.AreEqual(0, score.Warnings.Count, "voices are engraved as a bracketed system, not warned about");
    }

    [TestMethod]
    [CoversNode("abc-multivoice")]
    public void AVoiceTakesTheClefItAsksFor()
    {
        var score = new AbcParser().Parse(
            "X:1\nM:C\nV: P1\nV: P2 clef=bass\nK:C\n[V: P1] CDEF |]\n[V: P2] CDEF |]\n");
        Assert.AreEqual(ClefKind.Treble, score.Staves[0].Clef);
        Assert.AreEqual(ClefKind.Bass, score.Staves[1].Clef, "clef=bass on the V: line");
    }

    /// <summary>A part song writes its lower voices with no clef at all and expects the engraver to know.
    /// Reading it off the range keeps a bass line out of six ledger lines — without moving an ordinary tune.</summary>
    [TestMethod]
    [CoversNode("abc-multivoice")]
    public void AVoiceThatNamesNoClef_HasOneReadOffItsRange()
    {
        var score = new AbcParser().Parse(
            "X:1\nM:C\nV: P1\nV: P2\nK:C\n[V: P1] cdef gabc' |]\n[V: P2] C,,D,,E,,F,, |]\n");
        Assert.AreEqual(ClefKind.Treble, score.Staves[0].Clef);
        Assert.AreEqual(ClefKind.Bass, score.Staves[1].Clef, "a voice living below the treble staff is a bass part");
    }

    [TestMethod]
    [CoversNode("abc-multivoice")]
    public void ASingleVoiceTune_StaysInTreble_HoweverLowItDips()
    {
        // The inference is for part songs only. A one-voice tune takes ABC's default, as the standard says.
        Assert.AreEqual(ClefKind.Treble, new AbcParser().Parse("X:1\nM:C\nK:C\nC,,D,,E,,F,, |]\n").Staves[0].Clef);
    }

    // ── Rests ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("abc-core")]
    public void Rests_VisibleInvisibleAndWholeBar()
    {
        var evs = Staff("M:3/4", "z2 x2 Z |]").Measures.SelectMany(m => m.Events).OfType<Rest>().ToArray();
        Assert.AreEqual(3, evs.Length);
        Assert.IsFalse(evs[0].IsInvisible);
        Assert.IsTrue(evs[1].IsInvisible, "x occupies time but prints nothing");
        Assert.IsTrue(evs[2].IsWholeMeasure);
        Assert.AreEqual(3.0, evs[2].Duration.QuarterLength, 0.001, "Z fills the 3/4 bar");
    }
}
